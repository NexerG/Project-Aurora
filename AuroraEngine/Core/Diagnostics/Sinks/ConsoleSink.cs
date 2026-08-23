using System.Runtime.InteropServices;
using System.Text;

namespace ArctisAurora.Core.Diagnostics.Sinks
{
    // Standard output, written as raw UTF-8 by the spool thread and nobody else, so no game thread
    // ever takes the console lock.
    internal sealed class ConsoleSink
    {
        private const int stdOutputHandle = -11;
        private const uint enableVirtualTerminalProcessing = 0x0004;

        [DllImport("kernel32.dll")] private static extern nint GetStdHandle(int handle);
        [DllImport("kernel32.dll")] private static extern bool GetConsoleMode(nint handle, out uint mode);
        [DllImport("kernel32.dll")] private static extern bool SetConsoleMode(nint handle, uint mode);

        // Built rather than written as literals, so no bare escape byte sits in the source file.
        private static readonly byte[] reset = Ansi("[0m");
        private static readonly byte[][] colours =
        {
            Ansi("[90m"),          // Hot
            Ansi("[90m"),          // Trace
            Ansi("[34m"),          // Debug
            Array.Empty<byte>(),   // Info
            Ansi("[33m"),          // Warn
            Ansi("[31m"),          // Error
            Ansi("[1;31m"),        // Fatal
        };

        // field colours, laid over the level colour
        private static readonly byte[] originColour = Ansi("[95m");
        private static readonly byte[] durationColour = Ansi("[36m");

        private readonly Stream _out;
        private readonly bool _colour;

        public ConsoleSink()
        {
            _out = Console.OpenStandardOutput();
            _colour = EnableVirtualTerminal();

            // The engine's own messages are full of em dashes; without this they arrive as mojibake.
            try { Console.OutputEncoding = Encoding.UTF8; } catch { }
        }

        public void Write(LogLevel level, ReadOnlySpan<byte> line, int originStart, int originEnd)
        {
            if (!_colour)
            {
                _out.Write(line);
                return;
            }

            byte[] colour = (int)level < colours.Length ? colours[(int)level] : Array.Empty<byte>();
            if (colour.Length != 0) _out.Write(colour);

            if (originEnd > originStart && originEnd <= line.Length)
            {
                _out.Write(line.Slice(0, originStart));
                Segment(line.Slice(originStart, originEnd - originStart), originColour, colour);
                WriteDurations(line.Slice(originEnd), colour);
            }
            else WriteDurations(line, colour);

            _out.Write(reset);
        }

        private void Segment(ReadOnlySpan<byte> span, byte[] colour, byte[] restore)
        {
            _out.Write(colour);
            _out.Write(span);
            _out.Write(reset);
            if (restore.Length != 0) _out.Write(restore);
        }

        // A digit run ending in "ms", coloured as one token.
        private void WriteDurations(ReadOnlySpan<byte> span, byte[] restore)
        {
            int at = 0;

            for (int i = 1; i + 1 < span.Length; i++)
            {
                if (span[i] != (byte)'m' || span[i + 1] != (byte)'s') continue;

                int start = i;
                while (start > at && IsNumeric(span[start - 1])) start--;
                while (start < i && !IsDigit(span[start])) start++;
                if (start == i || !IsDigit(span[i - 1])) continue;

                _out.Write(span.Slice(at, start - at));
                Segment(span.Slice(start, i + 2 - start), durationColour, restore);
                at = i + 2;
                i++;
            }

            _out.Write(span.Slice(at));
        }

        private static bool IsDigit(byte value) => value >= (byte)'0' && value <= (byte)'9';

        private static bool IsNumeric(byte value) => IsDigit(value) || value == (byte)'.' || value == (byte)',';

        public void Flush() => _out.Flush();

        private static byte[] Ansi(string code)
        {
            byte[] sequence = new byte[code.Length + 1];
            sequence[0] = 0x1B;
            Encoding.ASCII.GetBytes(code, sequence.AsSpan(1));
            return sequence;
        }

        private static bool EnableVirtualTerminal()
        {
            try
            {
                nint handle = GetStdHandle(stdOutputHandle);
                if (!GetConsoleMode(handle, out uint mode)) return false;
                if ((mode & enableVirtualTerminalProcessing) != 0) return true;
                return SetConsoleMode(handle, mode | enableVirtualTerminalProcessing);
            }
            catch
            {
                return false;
            }
        }
    }
}
