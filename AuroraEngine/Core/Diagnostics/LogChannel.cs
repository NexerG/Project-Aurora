using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace ArctisAurora.Core.Diagnostics
{
    // One subsystem's entry point, held as a static readonly field so a call is a field load and a
    // predicted branch rather than a name lookup:
    //
    //     static readonly LogChannel Log = LogChannel.For("Renderer");
    //     Log.Warn($"no device matching '{preferred}' — using {DeviceName(gpu)}.");
    //
    // The minimum every channel gates on is the floor across all sinks, not the console's — the
    // flight recorder captures below what anything prints, so a line has to be formatted for it even
    // when nothing will show it.
    public sealed class LogChannel
    {
        private static LogChannel[] _all = Array.Empty<LogChannel>();
        private static readonly Lock _registry = new Lock();

        public static IReadOnlyList<LogChannel> All => Volatile.Read(ref _all);

        public string Name { get; }

        private volatile LogLevel _min;
        internal LogLevel min => _min;

        private LogChannel(string name, LogLevel min)
        {
            Name = name;
            _min = min;
        }

        public static LogChannel For(string name)
        {
            LogSpool.EnsureStarted();

            LogChannel[] snapshot = Volatile.Read(ref _all);
            for (int i = 0; i < snapshot.Length; i++)
                if (snapshot[i].Name == name) return snapshot[i];

            lock (_registry)
            {
                for (int i = 0; i < _all.Length; i++)
                    if (_all[i].Name == name) return _all[i];

                LogChannel channel = new LogChannel(name, LogSpool.floor);
                LogChannel[] grown = new LogChannel[_all.Length + 1];
                Array.Copy(_all, grown, _all.Length);
                grown[^1] = channel;
                Volatile.Write(ref _all, grown);
                return channel;
            }
        }

        // Pushed by LogSpool.Configure once the sinks know what they want.
        internal static void SetFloor(LogLevel level)
        {
            LogChannel[] snapshot = Volatile.Read(ref _all);
            for (int i = 0; i < snapshot.Length; i++)
                snapshot[i]._min = level;
        }

        public bool Enabled(LogLevel level) => level >= _min;

        // ---- levels ----

        [Conditional("DEBUG")]
        public void Hot([InterpolatedStringHandlerArgument("")] ref HotHandler message,
            [CallerFilePath] string file = "", [CallerLineNumber] int line = 0) => message.Emit(this, file, line);

        public void Trace([InterpolatedStringHandlerArgument("")] ref TraceHandler message,
            [CallerFilePath] string file = "", [CallerLineNumber] int line = 0) => message.Emit(this, file, line);

        public void Debug([InterpolatedStringHandlerArgument("")] ref DebugHandler message,
            [CallerFilePath] string file = "", [CallerLineNumber] int line = 0) => message.Emit(this, file, line);

        public void Info([InterpolatedStringHandlerArgument("")] ref InfoHandler message,
            [CallerFilePath] string file = "", [CallerLineNumber] int line = 0) => message.Emit(this, file, line);

        public void Warn([InterpolatedStringHandlerArgument("")] ref WarnHandler message,
            [CallerFilePath] string file = "", [CallerLineNumber] int line = 0) => message.Emit(this, file, line);

        public void Error([InterpolatedStringHandlerArgument("")] ref ErrorHandler message,
            [CallerFilePath] string file = "", [CallerLineNumber] int line = 0) => message.Emit(this, file, line);

        public void Fatal([InterpolatedStringHandlerArgument("")] ref FatalHandler message,
            [CallerFilePath] string file = "", [CallerLineNumber] int line = 0) => message.Emit(this, file, line);

        // Whole exception, stack included. Not an interpolated call — a stack trace runs past the
        // scratch line, so this one takes the allocating path.
        public void Exception(LogLevel level, System.Exception exception, string context,
            [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
            => LogSpool.WriteText(this, level, file, line, context + " — " + exception);

        // ---- rate gates ----

        // Both key on the call site, which [CallerFilePath] and [CallerLineNumber] make a compile-time
        // constant, so no key has to be invented at the call site.
        public LogGate Every(int ms, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
            => new LogGate(this, LogSites.Ready(file, line, ms));

        public LogGate Once([CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
            => new LogGate(this, LogSites.Ready(file, line, -1));
    }

    // A channel plus the answer to "has this site said anything recently". The handlers read the
    // flag in their constructor, so a suppressed call still evaluates none of its holes.
    public readonly struct LogGate
    {
        internal readonly LogChannel channel;
        internal readonly bool allowed;

        internal LogGate(LogChannel channel, bool allowed)
        {
            this.channel = channel;
            this.allowed = allowed;
        }

        [Conditional("DEBUG")]
        public void Hot([InterpolatedStringHandlerArgument("")] ref HotHandler message,
            [CallerFilePath] string file = "", [CallerLineNumber] int line = 0) => message.Emit(channel, file, line);

        public void Trace([InterpolatedStringHandlerArgument("")] ref TraceHandler message,
            [CallerFilePath] string file = "", [CallerLineNumber] int line = 0) => message.Emit(channel, file, line);

        public void Debug([InterpolatedStringHandlerArgument("")] ref DebugHandler message,
            [CallerFilePath] string file = "", [CallerLineNumber] int line = 0) => message.Emit(channel, file, line);

        public void Info([InterpolatedStringHandlerArgument("")] ref InfoHandler message,
            [CallerFilePath] string file = "", [CallerLineNumber] int line = 0) => message.Emit(channel, file, line);

        public void Warn([InterpolatedStringHandlerArgument("")] ref WarnHandler message,
            [CallerFilePath] string file = "", [CallerLineNumber] int line = 0) => message.Emit(channel, file, line);

        public void Error([InterpolatedStringHandlerArgument("")] ref ErrorHandler message,
            [CallerFilePath] string file = "", [CallerLineNumber] int line = 0) => message.Emit(channel, file, line);

        public void Fatal([InterpolatedStringHandlerArgument("")] ref FatalHandler message,
            [CallerFilePath] string file = "", [CallerLineNumber] int line = 0) => message.Emit(channel, file, line);
    }

    internal static class LogSites
    {
        private static readonly Dictionary<(string, int), long> _seen = new Dictionary<(string, int), long>();
        private static readonly Lock _lock = new Lock();

        // A negative period means once and never again.
        internal static bool Ready(string file, int line, int ms)
        {
            long now = Stopwatch.GetTimestamp();

            lock (_lock)
            {
                if (_seen.TryGetValue((file, line), out long previous))
                {
                    if (ms < 0) return false;
                    if ((now - previous) * 1000.0 / Stopwatch.Frequency < ms) return false;
                }
                _seen[(file, line)] = now;
                return true;
            }
        }
    }
}
