namespace ArctisAurora.Core.Data.Commands
{
    // A one-way channel from one producing system to one owning system: a ring of fixed-size
    // commands plus the arena holding their payloads.
    //
    // One lane per (producer, owner) pair, not one shared inbox per owner. That is the reason there
    // is no central message manager: per-pair, every ring has one writer and one reader, so enqueue
    // is a store and a cursor bump — no lock, no interlocked op. A shared inbox would have every
    // producer CAS on the same head, serialising all systems through one cache line. The cost is
    // N*M lanes, which for three systems is six.
    //
    // Commands and payloads are split rather than one variable-length record because SystemCommand
    // is fixed at 24 bytes so a drained run can be radix-sorted on SortKey and collapsed into
    // contiguous copies. Variable-length records cannot be sorted in place.
    public sealed class CommandLane
    {
        private readonly SystemCommand[] _ring;
        private readonly int _mask;
        private readonly CommandArena _arena;

        private long _writeSeq;        // producer only
        private long _publishedWrite;  // producer -> consumer
        private long _readSeq;         // consumer only
        private long _publishedRead;   // consumer -> producer

        public byte ProducerId { get; }
        public byte OwnerId { get; }
        public CommandArena Arena => _arena;

        public CommandLane(byte producerId, byte ownerId, int commandCapacity, int arenaBytes)
        {
            ProducerId = producerId;
            OwnerId = ownerId;
            int capacity = RoundUpPow2(commandCapacity);
            _ring = new SystemCommand[capacity];
            _mask = capacity - 1;
            _arena = new CommandArena($"lane {producerId}->{ownerId}", arenaBytes);
        }

        public int Capacity => _ring.Length;

        public long Pending => Volatile.Read(ref _publishedWrite) - _readSeq;

        public bool IsEmpty => Volatile.Read(ref _publishedWrite) == _readSeq;

        // Producer-side and exact: only the producer moves _writeSeq, so there is no race to lose.
        public bool HasSpace => _writeSeq - Volatile.Read(ref _publishedRead) < _ring.Length;

        // ---- producer side ----

        public bool TryWritePayload<T>(in T value, out int offset) where T : unmanaged => _arena.TryWrite(value, out offset);

        public bool TryWritePayload<T>(ReadOnlySpan<T> values, out int offset) where T : unmanaged => _arena.TryWrite(values, out offset);

        public bool TryEnqueue(in SystemCommand cmd)
        {
            if (_writeSeq - Volatile.Read(ref _publishedRead) >= _ring.Length) return false;
            _ring[_writeSeq & _mask] = cmd;
            _writeSeq++;
            return true;
        }

        // Publish everything written since the last commit. Arena first, so any command the
        // consumer can see already has its payload visible; the reverse order lets a drain read a
        // command pointing at bytes that have not landed.
        public void Commit()
        {
            _arena.Commit();
            Volatile.Write(ref _publishedWrite, _writeSeq);
        }

        // ---- consumer side ----

        // Snapshot of the run to apply this drain. The arena cursor is sampled BEFORE the ring
        // cursor: releasing bytes the drain has not reached hands them back to the producer to
        // overwrite, whereas sampling first can only under-release, which costs slack and is never
        // wrong.
        public void BeginDrain(out long from, out long to, out long arenaTo)
        {
            arenaTo = _arena.PublishedWrite;
            from = _readSeq;
            to = Volatile.Read(ref _publishedWrite);
        }

        public ref readonly SystemCommand At(long seq) => ref _ring[seq & _mask];

        public void EndDrain(long to, long arenaTo)
        {
            _readSeq = to;
            Volatile.Write(ref _publishedRead, to);
            _arena.ReleaseTo(arenaTo);
        }

        private static int RoundUpPow2(int v)
        {
            if (v < 16) return 16;
            v--;
            v |= v >> 1; v |= v >> 2; v |= v >> 4; v |= v >> 8; v |= v >> 16;
            return v + 1;
        }
    }
}
