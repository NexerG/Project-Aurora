namespace ArctisAurora.Core.Diagnostics
{
    // One line's metadata. The text itself lives in the lane's arena, at arenaOffset for length
    // bytes, already formatted as UTF-8.
    internal struct LogRecord
    {
        // when and where
        public long timestamp;
        public int epoch;
        public byte systemId;
        public int threadId;

        // what
        public LogChannel channel;
        public LogLevel level;
        public string file;
        public int line;

        // the text
        public int arenaOffset;
        public int length;
    }

    // One thread's outbound log queue: a ring of records plus a byte arena holding their text.
    //
    // Structurally the same as CommandLane and CommandArena in Core.Data.Commands — a fixed
    // power-of-two ring, four cursors, one volatile store each way. It is a copy rather than a
    // reference because CommandApplier, DataPool and DataManager all log, and a diagnostics
    // namespace that depends on the command system would close that loop.
    //
    // Single producer, single consumer, and that is load-bearing: the owning thread is the only
    // writer, the spool thread is the only reader.
    internal sealed class LogLane
    {
        private readonly LogRecord[] _ring;
        private readonly int _ringMask;
        private readonly byte[] _arena;
        private readonly int _arenaMask;

        private long _writeSeq;              // producer only
        private long _publishedWrite;        // producer -> consumer
        private long _readSeq;               // consumer only
        private long _publishedRead;         // consumer -> producer

        private long _arenaWrite;            // producer only
        private long _arenaPublishedWrite;   // producer -> consumer
        private long _arenaPublishedRead;    // consumer -> producer

        private long _dropped;

        public LogLane(int records, int arenaBytes)
        {
            int ringCapacity = RoundUpPow2(records);
            _ring = new LogRecord[ringCapacity];
            _ringMask = ringCapacity - 1;

            int arenaCapacity = RoundUpPow2(arenaBytes);
            _arena = new byte[arenaCapacity];
            _arenaMask = arenaCapacity - 1;
        }

        // Records lost to a full ring or arena. The spool reports the count rather than letting the
        // loss go unmentioned.
        public long Dropped => Volatile.Read(ref _dropped);

        public long TakeDropped()
        {
            long seen = Volatile.Read(ref _dropped);
            if (seen != 0) Volatile.Write(ref _dropped, 0);
            return seen;
        }

        // ---- producer side ----

        // Text never straddles the arena's end: a run that would wrap skips the tail and starts at
        // zero, so the consumer always reads one contiguous span.
        public bool TryWrite(in LogRecord meta, ReadOnlySpan<byte> text)
        {
            if (_writeSeq - Volatile.Read(ref _publishedRead) >= _ring.Length)
            {
                Volatile.Write(ref _dropped, _dropped + 1);
                return false;
            }

            int offset = (int)(_arenaWrite & _arenaMask);
            long need = text.Length;
            if (offset + text.Length > _arena.Length)
            {
                need += _arena.Length - offset;
                offset = 0;
            }

            long used = _arenaWrite - Volatile.Read(ref _arenaPublishedRead);
            if (need > _arena.Length - used)
            {
                Volatile.Write(ref _dropped, _dropped + 1);
                return false;
            }

            text.CopyTo(_arena.AsSpan(offset));
            _arenaWrite += need;

            LogRecord record = meta;
            record.arenaOffset = offset;
            record.length = text.Length;

            _ring[_writeSeq & _ringMask] = record;
            _writeSeq++;

            // Arena first, so a record the consumer can see already has its bytes visible.
            Volatile.Write(ref _arenaPublishedWrite, _arenaWrite);
            Volatile.Write(ref _publishedWrite, _writeSeq);
            return true;
        }

        // ---- consumer side ----

        public void BeginDrain(out long from, out long to, out long arenaTo)
        {
            arenaTo = Volatile.Read(ref _arenaPublishedWrite);
            from = _readSeq;
            to = Volatile.Read(ref _publishedWrite);
        }

        public ref readonly LogRecord At(long seq) => ref _ring[seq & _ringMask];

        public ReadOnlySpan<byte> TextOf(in LogRecord record) => _arena.AsSpan(record.arenaOffset, record.length);

        public void EndDrain(long to, long arenaTo)
        {
            _readSeq = to;
            Volatile.Write(ref _publishedRead, to);
            Volatile.Write(ref _arenaPublishedRead, arenaTo);
        }

        private static int RoundUpPow2(int value)
        {
            int result = 1;
            while (result < value) result <<= 1;
            return result;
        }
    }
}
