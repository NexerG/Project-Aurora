using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ArctisAurora.Core.Data.Commands
{
    // Payload store for one producer's commands. A bump allocator over a flat byte buffer: writes
    // are sequential and cost a memcpy, reads are sequential in the same order the planner walks
    // the command stream.
    //
    // One arena per producing system, so writes are single-threaded by construction and need no
    // synchronisation. The consuming thread reads the arena during its drain, which means Reset()
    // must not run until that drain has finished — a producer that writes and resets while the
    // owner is still reading will hand it garbage. Producers that can outrun a drain need two
    // arenas and a swap on publish; a single arena is only correct while the producer waits for
    // its drain to complete.
    //
    // Capacity is fixed on purpose. Growing would reallocate the buffer out from under a consumer
    // that is mid-read, which is exactly the class of bug this whole design exists to remove.
    // Overflow is a real condition to handle at the lane level, not something to paper over here.
    public sealed class CommandArena
    {
        private readonly byte[] _buffer;
        private int _head;
        private readonly string _name;

        public CommandArena(string name, int capacityBytes)
        {
            _name = name;
            _buffer = new byte[capacityBytes];
        }

        public int Capacity => _buffer.Length;
        public int Used => _head;
        public int Remaining => _buffer.Length - _head;

        // Returns the byte offset the value was written at, for SystemCommand.ArenaOffset.
        public int Write<T>(in T value) where T : unmanaged
        {
            int size = Unsafe.SizeOf<T>();
            int offset = Reserve(size);
            MemoryMarshal.Write(_buffer.AsSpan(offset, size), in value);
            return offset;
        }

        public int Write<T>(ReadOnlySpan<T> values) where T : unmanaged
        {
            int size = Unsafe.SizeOf<T>() * values.Length;
            int offset = Reserve(size);
            MemoryMarshal.AsBytes(values).CopyTo(_buffer.AsSpan(offset, size));
            return offset;
        }

        public ref readonly T Read<T>(int offset) where T : unmanaged => ref MemoryMarshal.AsRef<T>(_buffer.AsSpan(offset, Unsafe.SizeOf<T>()));

        public ReadOnlySpan<T> Read<T>(int offset, int count) where T : unmanaged => MemoryMarshal.Cast<byte, T>(_buffer.AsSpan(offset, Unsafe.SizeOf<T>() * count));

        // Called by the producer once its drain has completed. See the lifetime note above.
        public void Reset() => _head = 0;

        private int Reserve(int size)
        {
            if (_head + size > _buffer.Length)
                throw new InvalidOperationException($"[CommandArena] '{_name}' overflowed: needed {size} bytes, {Remaining} of {_buffer.Length} free. Raise the arena capacity or drain more often.");

            int offset = _head;
            _head += size;
            return offset;
        }
    }
}
