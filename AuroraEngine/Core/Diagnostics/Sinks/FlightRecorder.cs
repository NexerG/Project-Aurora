namespace ArctisAurora.Core.Diagnostics.Sinks
{
    // A circular buffer of the most recent lines, captured below whatever the file and console are
    // filtering to and never written to disk in the steady state. It exists for the crash: a hard
    // fault out of the driver leaves no chance to flush, so the tail has to already be in memory.
    internal sealed class FlightRecorder
    {
        private readonly byte[] _buffer;
        private readonly Lock _lock = new Lock();

        private int _pos;
        private bool _wrapped;

        public LogLevel minLevel = LogLevel.Trace;

        public FlightRecorder(int capacityBytes)
        {
            _buffer = new byte[Math.Max(capacityBytes, 4096)];
        }

        public void Append(ReadOnlySpan<byte> line)
        {
            if (line.Length >= _buffer.Length) return;

            lock (_lock)
            {
                int room = _buffer.Length - _pos;
                if (line.Length <= room)
                {
                    line.CopyTo(_buffer.AsSpan(_pos));
                    _pos += line.Length;
                    if (_pos == _buffer.Length)
                    {
                        _pos = 0;
                        _wrapped = true;
                    }
                    return;
                }

                line.Slice(0, room).CopyTo(_buffer.AsSpan(_pos));
                line.Slice(room).CopyTo(_buffer.AsSpan(0));
                _pos = line.Length - room;
                _wrapped = true;
            }
        }

        // Oldest first, with the partial line the wrap cut in half dropped. Allocates, and is meant
        // to — this runs once, on the way out.
        public byte[] Snapshot()
        {
            lock (_lock)
            {
                if (!_wrapped) return _buffer.AsSpan(0, _pos).ToArray();

                byte[] ordered = new byte[_buffer.Length];
                int tail = _buffer.Length - _pos;
                _buffer.AsSpan(_pos).CopyTo(ordered);
                _buffer.AsSpan(0, _pos).CopyTo(ordered.AsSpan(tail));

                int start = Array.IndexOf(ordered, (byte)'\n');
                return start < 0 ? ordered : ordered.AsSpan(start + 1).ToArray();
            }
        }
    }
}
