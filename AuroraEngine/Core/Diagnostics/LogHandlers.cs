using System.Runtime.CompilerServices;

namespace ArctisAurora.Core.Diagnostics
{
    // One handler per level, because the level has to reach the handler's constructor and
    // [InterpolatedStringHandlerArgument("")] only carries the receiver. Two constructors each:
    // one for a plain channel call, one for a rate-gated LogGate call.
    //
    // The whole point of these is the out bool. When it comes back false the compiler emits none of
    // the AppendFormatted calls, so the interpolation holes are never evaluated and a disabled level
    // costs one field load and a predicted branch.

    [InterpolatedStringHandler]
    public ref struct HotHandler
    {
        private LogWriter _writer;

        public HotHandler(int literalLength, int formattedCount, LogChannel channel, out bool enabled)
            => _writer = LogWriter.Begin(channel, LogLevel.Hot, out enabled);

        public HotHandler(int literalLength, int formattedCount, in LogGate gate, out bool enabled)
            => _writer = LogWriter.Begin(gate, LogLevel.Hot, out enabled);

        public void AppendLiteral(string value) => _writer.AppendLiteral(value);
        public void AppendFormatted<T>(T value) => _writer.AppendFormatted(value);
        public void AppendFormatted<T>(T value, string? format) => _writer.AppendFormatted(value, format);
        public void AppendFormatted<T>(T value, int alignment, string? format = null) => _writer.AppendFormatted(value, alignment, format);
        public void AppendFormatted(string? value) => _writer.AppendFormatted(value);
        public void AppendFormatted(string? value, int alignment = 0, string? format = null) => _writer.AppendFormatted(value, alignment, format);
        public void AppendFormatted(ReadOnlySpan<char> value) => _writer.AppendFormatted(value);
        public void AppendFormatted(ReadOnlySpan<char> value, int alignment = 0, string? format = null) => _writer.AppendFormatted(value, alignment, format);

        internal void Emit(LogChannel channel, string file, int line) => _writer.Emit(channel, LogLevel.Hot, file, line);
    }

    [InterpolatedStringHandler]
    public ref struct TraceHandler
    {
        private LogWriter _writer;

        public TraceHandler(int literalLength, int formattedCount, LogChannel channel, out bool enabled)
            => _writer = LogWriter.Begin(channel, LogLevel.Trace, out enabled);

        public TraceHandler(int literalLength, int formattedCount, in LogGate gate, out bool enabled)
            => _writer = LogWriter.Begin(gate, LogLevel.Trace, out enabled);

        public void AppendLiteral(string value) => _writer.AppendLiteral(value);
        public void AppendFormatted<T>(T value) => _writer.AppendFormatted(value);
        public void AppendFormatted<T>(T value, string? format) => _writer.AppendFormatted(value, format);
        public void AppendFormatted<T>(T value, int alignment, string? format = null) => _writer.AppendFormatted(value, alignment, format);
        public void AppendFormatted(string? value) => _writer.AppendFormatted(value);
        public void AppendFormatted(string? value, int alignment = 0, string? format = null) => _writer.AppendFormatted(value, alignment, format);
        public void AppendFormatted(ReadOnlySpan<char> value) => _writer.AppendFormatted(value);
        public void AppendFormatted(ReadOnlySpan<char> value, int alignment = 0, string? format = null) => _writer.AppendFormatted(value, alignment, format);

        internal void Emit(LogChannel channel, string file, int line) => _writer.Emit(channel, LogLevel.Trace, file, line);
    }

    [InterpolatedStringHandler]
    public ref struct DebugHandler
    {
        private LogWriter _writer;

        public DebugHandler(int literalLength, int formattedCount, LogChannel channel, out bool enabled)
            => _writer = LogWriter.Begin(channel, LogLevel.Debug, out enabled);

        public DebugHandler(int literalLength, int formattedCount, in LogGate gate, out bool enabled)
            => _writer = LogWriter.Begin(gate, LogLevel.Debug, out enabled);

        public void AppendLiteral(string value) => _writer.AppendLiteral(value);
        public void AppendFormatted<T>(T value) => _writer.AppendFormatted(value);
        public void AppendFormatted<T>(T value, string? format) => _writer.AppendFormatted(value, format);
        public void AppendFormatted<T>(T value, int alignment, string? format = null) => _writer.AppendFormatted(value, alignment, format);
        public void AppendFormatted(string? value) => _writer.AppendFormatted(value);
        public void AppendFormatted(string? value, int alignment = 0, string? format = null) => _writer.AppendFormatted(value, alignment, format);
        public void AppendFormatted(ReadOnlySpan<char> value) => _writer.AppendFormatted(value);
        public void AppendFormatted(ReadOnlySpan<char> value, int alignment = 0, string? format = null) => _writer.AppendFormatted(value, alignment, format);

        internal void Emit(LogChannel channel, string file, int line) => _writer.Emit(channel, LogLevel.Debug, file, line);
    }

    [InterpolatedStringHandler]
    public ref struct InfoHandler
    {
        private LogWriter _writer;

        public InfoHandler(int literalLength, int formattedCount, LogChannel channel, out bool enabled)
            => _writer = LogWriter.Begin(channel, LogLevel.Info, out enabled);

        public InfoHandler(int literalLength, int formattedCount, in LogGate gate, out bool enabled)
            => _writer = LogWriter.Begin(gate, LogLevel.Info, out enabled);

        public void AppendLiteral(string value) => _writer.AppendLiteral(value);
        public void AppendFormatted<T>(T value) => _writer.AppendFormatted(value);
        public void AppendFormatted<T>(T value, string? format) => _writer.AppendFormatted(value, format);
        public void AppendFormatted<T>(T value, int alignment, string? format = null) => _writer.AppendFormatted(value, alignment, format);
        public void AppendFormatted(string? value) => _writer.AppendFormatted(value);
        public void AppendFormatted(string? value, int alignment = 0, string? format = null) => _writer.AppendFormatted(value, alignment, format);
        public void AppendFormatted(ReadOnlySpan<char> value) => _writer.AppendFormatted(value);
        public void AppendFormatted(ReadOnlySpan<char> value, int alignment = 0, string? format = null) => _writer.AppendFormatted(value, alignment, format);

        internal void Emit(LogChannel channel, string file, int line) => _writer.Emit(channel, LogLevel.Info, file, line);
    }

    [InterpolatedStringHandler]
    public ref struct WarnHandler
    {
        private LogWriter _writer;

        public WarnHandler(int literalLength, int formattedCount, LogChannel channel, out bool enabled)
            => _writer = LogWriter.Begin(channel, LogLevel.Warn, out enabled);

        public WarnHandler(int literalLength, int formattedCount, in LogGate gate, out bool enabled)
            => _writer = LogWriter.Begin(gate, LogLevel.Warn, out enabled);

        public void AppendLiteral(string value) => _writer.AppendLiteral(value);
        public void AppendFormatted<T>(T value) => _writer.AppendFormatted(value);
        public void AppendFormatted<T>(T value, string? format) => _writer.AppendFormatted(value, format);
        public void AppendFormatted<T>(T value, int alignment, string? format = null) => _writer.AppendFormatted(value, alignment, format);
        public void AppendFormatted(string? value) => _writer.AppendFormatted(value);
        public void AppendFormatted(string? value, int alignment = 0, string? format = null) => _writer.AppendFormatted(value, alignment, format);
        public void AppendFormatted(ReadOnlySpan<char> value) => _writer.AppendFormatted(value);
        public void AppendFormatted(ReadOnlySpan<char> value, int alignment = 0, string? format = null) => _writer.AppendFormatted(value, alignment, format);

        internal void Emit(LogChannel channel, string file, int line) => _writer.Emit(channel, LogLevel.Warn, file, line);
    }

    [InterpolatedStringHandler]
    public ref struct ErrorHandler
    {
        private LogWriter _writer;

        public ErrorHandler(int literalLength, int formattedCount, LogChannel channel, out bool enabled)
            => _writer = LogWriter.Begin(channel, LogLevel.Error, out enabled);

        public ErrorHandler(int literalLength, int formattedCount, in LogGate gate, out bool enabled)
            => _writer = LogWriter.Begin(gate, LogLevel.Error, out enabled);

        public void AppendLiteral(string value) => _writer.AppendLiteral(value);
        public void AppendFormatted<T>(T value) => _writer.AppendFormatted(value);
        public void AppendFormatted<T>(T value, string? format) => _writer.AppendFormatted(value, format);
        public void AppendFormatted<T>(T value, int alignment, string? format = null) => _writer.AppendFormatted(value, alignment, format);
        public void AppendFormatted(string? value) => _writer.AppendFormatted(value);
        public void AppendFormatted(string? value, int alignment = 0, string? format = null) => _writer.AppendFormatted(value, alignment, format);
        public void AppendFormatted(ReadOnlySpan<char> value) => _writer.AppendFormatted(value);
        public void AppendFormatted(ReadOnlySpan<char> value, int alignment = 0, string? format = null) => _writer.AppendFormatted(value, alignment, format);

        internal void Emit(LogChannel channel, string file, int line) => _writer.Emit(channel, LogLevel.Error, file, line);
    }

    [InterpolatedStringHandler]
    public ref struct FatalHandler
    {
        private LogWriter _writer;

        public FatalHandler(int literalLength, int formattedCount, LogChannel channel, out bool enabled)
            => _writer = LogWriter.Begin(channel, LogLevel.Fatal, out enabled);

        public FatalHandler(int literalLength, int formattedCount, in LogGate gate, out bool enabled)
            => _writer = LogWriter.Begin(gate, LogLevel.Fatal, out enabled);

        public void AppendLiteral(string value) => _writer.AppendLiteral(value);
        public void AppendFormatted<T>(T value) => _writer.AppendFormatted(value);
        public void AppendFormatted<T>(T value, string? format) => _writer.AppendFormatted(value, format);
        public void AppendFormatted<T>(T value, int alignment, string? format = null) => _writer.AppendFormatted(value, alignment, format);
        public void AppendFormatted(string? value) => _writer.AppendFormatted(value);
        public void AppendFormatted(string? value, int alignment = 0, string? format = null) => _writer.AppendFormatted(value, alignment, format);
        public void AppendFormatted(ReadOnlySpan<char> value) => _writer.AppendFormatted(value);
        public void AppendFormatted(ReadOnlySpan<char> value, int alignment = 0, string? format = null) => _writer.AppendFormatted(value, alignment, format);

        internal void Emit(LogChannel channel, string file, int line) => _writer.Emit(channel, LogLevel.Fatal, file, line);
    }
}
