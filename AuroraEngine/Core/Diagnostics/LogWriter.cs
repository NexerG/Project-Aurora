using System.Buffers;
using System.Globalization;
using System.Text.Unicode;

namespace ArctisAurora.Core.Diagnostics
{
    // Formats one line straight into the calling thread's scratch buffer as UTF-8, then hands the
    // bytes to the spool. Nothing here allocates and nothing here throws — a logger that can fail
    // is worse than no logger.
    //
    // Started is false when the level was gated off, and the interpolated string handlers that wrap
    // this never call in at all in that case: the compiler skips every AppendFormatted, so the
    // holes are not evaluated.
    public ref struct LogWriter
    {
        private Span<byte> _buffer;
        private int _pos;
        private bool _started;
        private bool _truncated;

        internal readonly bool Started => _started;

        internal static LogWriter Begin(LogChannel? channel, LogLevel level, out bool enabled)
        {
            if (channel == null || level < channel.min)
            {
                enabled = false;
                return default;
            }

            enabled = true;
            LogWriter writer = default;
            writer._buffer = LogSpool.Scratch();
            writer._started = true;
            return writer;
        }

        internal static LogWriter Begin(in LogGate gate, LogLevel level, out bool enabled)
        {
            if (!gate.allowed)
            {
                enabled = false;
                return default;
            }
            return Begin(gate.channel, level, out enabled);
        }

        public void AppendLiteral(string value) => AppendChars(value.AsSpan());

        public void AppendFormatted<T>(T value)
        {
            if (!_started || _truncated) return;

            // Value types instantiate their own generic code, so this test folds away and the
            // interface call devirtualizes — no box on the int/float/uint path.
            if (value is IUtf8SpanFormattable formattable)
            {
                if (formattable.TryFormat(_buffer.Slice(_pos), out int written, default, CultureInfo.InvariantCulture)) _pos += written;
                else _truncated = true;
                return;
            }

            string? text = value?.ToString();
            AppendChars(text.AsSpan());
        }

        public void AppendFormatted<T>(T value, string? format)
        {
            if (!_started || _truncated) return;

            if (value is IUtf8SpanFormattable formattable)
            {
                if (formattable.TryFormat(_buffer.Slice(_pos), out int written, format, CultureInfo.InvariantCulture)) _pos += written;
                else _truncated = true;
                return;
            }
            if (value is IFormattable legacy)
            {
                AppendChars(legacy.ToString(format, CultureInfo.InvariantCulture).AsSpan());
                return;
            }

            string? text = value?.ToString();
            AppendChars(text.AsSpan());
        }

        public void AppendFormatted<T>(T value, int alignment, string? format = null)
        {
            int start = _pos;
            AppendFormatted(value, format);
            Pad(start, alignment);
        }

        public void AppendFormatted(string? value) => AppendChars(value.AsSpan());

        public void AppendFormatted(string? value, int alignment = 0, string? format = null)
        {
            int start = _pos;
            AppendChars(value.AsSpan());
            Pad(start, alignment);
        }

        public void AppendFormatted(ReadOnlySpan<char> value) => AppendChars(value);

        public void AppendFormatted(ReadOnlySpan<char> value, int alignment = 0, string? format = null)
        {
            int start = _pos;
            AppendChars(value);
            Pad(start, alignment);
        }

        internal void Emit(LogChannel channel, LogLevel level, string file, int line)
        {
            if (!_started) return;
            if (_truncated) MarkTruncated();
            LogSpool.Write(channel, level, file, line, _buffer.Slice(0, _pos));
        }

        private void AppendChars(ReadOnlySpan<char> value)
        {
            if (!_started || _truncated || value.IsEmpty) return;

            OperationStatus status = Utf8.FromUtf16(value, _buffer.Slice(_pos), out _, out int written,
                replaceInvalidSequences: true, isFinalBlock: true);
            _pos += written;
            if (status != OperationStatus.Done) _truncated = true;
        }

        private void Pad(int start, int alignment)
        {
            if (!_started || _truncated || alignment == 0) return;

            int written = _pos - start;
            int width = alignment < 0 ? -alignment : alignment;
            if (written >= width) return;

            int padding = width - written;
            if (padding > _buffer.Length - _pos)
            {
                _truncated = true;
                return;
            }

            if (alignment < 0)
            {
                _buffer.Slice(_pos, padding).Fill((byte)' ');
            }
            else
            {
                _buffer.Slice(start, written).CopyTo(_buffer.Slice(start + padding));
                _buffer.Slice(start, padding).Fill((byte)' ');
            }
            _pos += padding;
        }

        private void MarkTruncated()
        {
            int at = Math.Min(_pos, _buffer.Length - 3);
            if (at < 0) return;

            _buffer[at] = (byte)'.';
            _buffer[at + 1] = (byte)'.';
            _buffer[at + 2] = (byte)'.';
            _pos = at + 3;
        }
    }
}
