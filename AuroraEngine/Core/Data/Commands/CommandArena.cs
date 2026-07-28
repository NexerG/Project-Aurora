using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ArctisAurora.Core.Data.Commands
{
    // Payload store for one producer's commands: a byte ring with published cursors. Writes are
    // sequential memcpys, reads are sequential in the order the drain walks the command stream.
    //
    // This was a bump allocator with a Reset(), which is only correct while the producer waits for
    // its drain. Nothing waits any more — main paces at 120Hz, render runs free — so a producer
    // routinely gets several ticks ahead and a Reset() under a live reader would hand it garbage.
    // A reader cursor is the general fix: the producer cannot allocate over unreleased bytes.
    //
    // Single producer, single consumer, and that is load-bearing: _writeSeq is touched only by the
    // producer, _publishedRead only by the consumer, and one volatile long each way is the whole
    // of the synchronisation.
    //
    // Capacity is fixed and a power of two. Growing would reallocate under a mid-read consumer, so
    // a full ring is a condition the lane reports rather than something to paper over.
    public sealed class CommandArena
    {
        private readonly byte[] _buffer;
        private readonly int _mask;
        private readonly string _name;

        private long _writeSeq;        // producer only — monotonic, never wraps in practice
        private long _publishedWrite;  // producer -> consumer
        private long _publishedRead;   // consumer -> producer

        public CommandArena(string name, int capacityBytes)
        {
            _name = name;
            int capacity = RoundUpPow2(capacityBytes);
            _buffer = new byte[capacity];
            _mask = capacity - 1;
        }

        public int Capacity => _buffer.Length;

        public long PublishedWrite => Volatile.Read(ref _publishedWrite);

        public long Used => _writeSeq - Volatile.Read(ref _publishedRead);

        public int Remaining => (int)(_buffer.Length - Used);

        // Returns the byte offset the value was written at, for SystemCommand.ArenaOffset. False
        // when the ring is too full to take it — the caller decides what that means.
        public bool TryWrite<T>(in T value, out int offset) where T : unmanaged
        {
            int size = Unsafe.SizeOf<T>();
            if (!TryReserve(size, out offset)) return false;
            MemoryMarshal.Write(_buffer.AsSpan(offset, size), in value);
            return true;
        }

        public bool TryWrite<T>(ReadOnlySpan<T> values, out int offset) where T : unmanaged
        {
            int size = Unsafe.SizeOf<T>() * values.Length;
            if (!TryReserve(size, out offset)) return false;
            MemoryMarshal.AsBytes(values).CopyTo(_buffer.AsSpan(offset, size));
            return true;
        }

        public ref readonly T Read<T>(int offset) where T : unmanaged => ref MemoryMarshal.AsRef<T>(_buffer.AsSpan(offset, Unsafe.SizeOf<T>()));

        public ReadOnlySpan<T> Read<T>(int offset, int count) where T : unmanaged => MemoryMarshal.Cast<byte, T>(_buffer.AsSpan(offset, Unsafe.SizeOf<T>() * count));

        public ReadOnlySpan<byte> ReadBytes(int offset, int length) => _buffer.AsSpan(offset, length);

        // Producer: publish everything written so far. The release write is what guarantees the
        // payload bytes land before the cursor advertising them.
        public void Commit() => Volatile.Write(ref _publishedWrite, _writeSeq);

        // Consumer: everything below this sequence is read and its bytes may be reused. Called once
        // per drain with the write cursor snapshotted at its start, since a drain consumes the
        // whole published run in order.
        public void ReleaseTo(long seq) => Volatile.Write(ref _publishedRead, seq);

        // A record must not straddle the end of the buffer, or Read needs two copies and every
        // consumer has to know about the split. Padding to the boundary costs at most one record
        // of slack per lap and keeps reads a single span.
        private bool TryReserve(int size, out int offset)
        {
            offset = -1;
            if (size > _buffer.Length) return false;

            int physical = (int)(_writeSeq & _mask);
            int pad = physical + size <= _buffer.Length ? 0 : _buffer.Length - physical;

            long free = _buffer.Length - (_writeSeq - Volatile.Read(ref _publishedRead));
            if (pad + size > free) return false;

            _writeSeq += pad;
            offset = (int)(_writeSeq & _mask);
            _writeSeq += size;
            return true;
        }

        private static int RoundUpPow2(int v)
        {
            if (v < 64) return 64;
            v--;
            v |= v >> 1; v |= v >> 2; v |= v >> 4; v |= v >> 8; v |= v >> 16;
            return v + 1;
        }

        public override string ToString() => $"[CommandArena] '{_name}' {Used}/{_buffer.Length} bytes";
    }
}
