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
            Ansi("[36m"),          // Debug
            Array.Empty<byte>(),   // Info
            Ansi("[33m"),          // Warn
            Ansi("[31m"),          // Error
            Ansi("[1;31m"),        // Fatal
        };

        private readonly Stream _out;
        private readonly bool _colour;

        public ConsoleSink()
        {
            _out = Console.OpenStandardOutput();
            _colour = EnableVirtualTerminal();

            // The engine's own messages are full of em dashes; without this they arrive as mojibake.
            try { Console.OutputEncoding = Encoding.UTF8; } catch { }
        }

        public void Write(LogLevel level, ReadOnlySpan<byte> line)
        {
            byte[] colour = _colour && (int)level < colours.Length ? colours[(int)level] : Array.Empty<byte>();

            if (colour.Length != 0) _out.Write(colour);
            _out.Write(line);
            if (colour.Length != 0) _out.Write(reset);
        }

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
