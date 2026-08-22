using System.Diagnostics;
using System.Globalization;
using System.Text;
using ArctisAurora.Core.Diagnostics.Sinks;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Threading;

namespace ArctisAurora.Core.Diagnostics
{
    // The drain. One background thread gathers every lane, merges the records by timestamp, formats
    // them once as UTF-8 and feeds the sinks. It is a plain thread and not a ThreadedSystem: it owns
    // no pools, so a SystemId and a set of command lanes would be six lanes it never reads.
    //
    // Self-starting, because XSDGenerator.GenerateXSD runs before Engine.Init and logs. Until
    // Logging.Configure lands, the console prints at a fixed default and everything is also held so
    // the file and the recorder can be filled retroactively once they know their levels.
    public static class LogSpool
    {
        private const int maxLineBytes = 1024;
        private const int laneRecords = 2048;
        private const int laneArenaBytes = 256 * 1024;
        private const int pendingCap = 4096;

        [ThreadStatic] private static byte[]? _scratch;
        [ThreadStatic] private static LogLane? _lane;

        private static LogLane[] _lanes = Array.Empty<LogLane>();
        private static readonly LogLane _shared = new LogLane(1024, 64 * 1024);
        private static readonly Lock _sharedWrite = new Lock();
        private static readonly Lock _registry = new Lock();
        private static readonly Lock _drain = new Lock();

        private static Thread? _worker;
        private static readonly AutoResetEvent _wake = new AutoResetEvent(false);
        private static readonly ManualResetEventSlim _configAck = new ManualResetEventSlim(false);
        private static volatile bool _running;
        private static volatile bool _started;

        // wall-clock anchor — the per-record stamp is a raw counter read, converted only here
        private static readonly long bootTicks = Stopwatch.GetTimestamp();
        private static readonly DateTime bootTime = DateTime.Now;

        // sinks and their levels
        private static readonly ConsoleSink _console = new ConsoleSink();
        private static FileSink? _file;
        private static FlightRecorder? _recorder;
        private static LogLevel _consoleMin = LogLevel.Info;
        private static LogLevel _fileMin = LogLevel.Debug;
        private static LogLevel _recorderMin = LogLevel.Trace;

        // The floor across every sink, which is what a channel gates on — the recorder captures
        // below what anything prints, so those lines still have to be formatted.
        internal static LogLevel floor = LogLevel.Trace;

        // spill buffer
        private static byte[] _spill = new byte[64 * 1024];
        private static int _spillPos;
        private static int _flushMs = 2000;
        private static long _lastSpill;

        // pre-configure holding pen, and the composed-line scratch, both spool-thread only
        private static readonly List<(LogLevel level, byte[] line)> _pending = new List<(LogLevel, byte[])>();
        private static bool _applied;
        private static byte[] _line = new byte[8192];
        private static int _dumped;

        private static volatile Config? _request;

        private sealed class Config
        {
            public LogLevel consoleMin, fileMin, recorderMin;
            public int flushMs, bufferBytes, recorderBytes;
            public FileSink? file;
        }

        internal static Span<byte> Scratch() => _scratch ??= new byte[maxLineBytes];

        internal static void EnsureStarted()
        {
            if (_started) return;

            lock (_registry)
            {
                if (_started) return;
                _started = true;
                _running = true;
                _lanes = new LogLane[] { _shared };
                _lastSpill = Stopwatch.GetTimestamp();

                AppDomain.CurrentDomain.UnhandledException += OnUnhandled;
                AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

                _worker = new Thread(Loop) { Name = "log", IsBackground = true, Priority = ThreadPriority.BelowNormal };
                _worker.Start();
            }
        }

        // ---- producer side ----

        internal static void Write(LogChannel channel, LogLevel level, string file, int line, ReadOnlySpan<byte> text)
        {
            ThreadedSystem? system = ThreadedSystem.Current;

            LogRecord record = new LogRecord
            {
                timestamp = Stopwatch.GetTimestamp(),
                epoch = system?.Epoch ?? 0,
                systemId = system?.SystemId ?? 0,
                threadId = Environment.CurrentManagedThreadId,
                channel = channel,
                level = level,
                file = file,
                line = line,
            };

            if (system != null)
            {
                (_lane ??= Register()).TryWrite(record, text);
            }
            else
            {
                lock (_sharedWrite) _shared.TryWrite(record, text);
            }

            _wake.Set();
        }

        // For text that is already a string and may be far longer than a scratch line — a stack
        // trace, mostly.
        internal static void WriteText(LogChannel channel, LogLevel level, string file, int line, string text)
        {
            if (level < channel.min) return;
            Write(channel, level, file, line, Encoding.UTF8.GetBytes(text));
        }

        private static LogLane Register()
        {
            LogLane lane = new LogLane(laneRecords, laneArenaBytes);

            lock (_registry)
            {
                LogLane[] grown = new LogLane[_lanes.Length + 1];
                Array.Copy(_lanes, grown, _lanes.Length);
                grown[^1] = lane;
                Volatile.Write(ref _lanes, grown);
            }
            return lane;
        }

        // ---- bootstrap and shutdown ----

        [A_XSDActionDependency("Logging.Configure", "Bootstrap", "Applies LoggingSettings to the sinks and replays what booted before them")]
        public static bool Configure()
        {
            LoggingSettings settings = SettingsRegistry.Get<LoggingSettings>();

            LogLevel recorderMin = settings.recorder.enabled ? settings.recorder.minLevel : LogLevel.Off;
            Config config = new Config
            {
                consoleMin = settings.console.minLevel,
                fileMin = settings.file.minLevel,
                recorderMin = recorderMin,
                flushMs = Math.Max(100, settings.file.flushMs),
                bufferBytes = Math.Max(4096, settings.file.bufferKB * 1024),
                recorderBytes = settings.recorder.enabled ? Math.Max(4096, settings.recorder.capacityKB * 1024) : 0,
                file = settings.file.minLevel == LogLevel.Off ? null : new FileSink(
                    LogDirectory(settings.file.directory), settings.file.name,
                    (long)settings.file.maxFileMB * 1024 * 1024, Math.Max(1, settings.file.keep)),
            };

            LogLevel effective = Lowest(config.consoleMin, Lowest(config.fileMin, config.recorderMin));
            floor = effective;
            LogChannel.SetFloor(effective);

            // Applied on the spool thread, which owns the sinks and the holding pen; this one waits
            // so the steps after it in Bootstrap.xml are already writing to a live file.
            _configAck.Reset();
            _request = config;
            _wake.Set();
            _configAck.Wait(2000);
            return true;
        }

        [A_XSDActionDependency("Logging.Flush", "Shutdown", "Drains and writes everything still queued")]
        public static bool Flush()
        {
            _running = false;
            _wake.Set();
            _worker?.Join(2000);
            return true;
        }

        // Everything the recorder is holding, appended to the log under a banner. Called on the way
        // out of a crash, and safe to call by hand.
        //
        // Once per process, and only when the recorder is capturing below what the file already
        // takes — otherwise every line in the tail is on disk twice, and a crash that unwinds
        // through both the system loop and the unhandled handler would write it twice more.
        public static void DumpRecorder()
        {
            FlightRecorder? recorder = _recorder;
            if (recorder == null || _file == null) return;
            if (_recorderMin >= _fileMin) return;
            if (Interlocked.Exchange(ref _dumped, 1) != 0) return;

            byte[] tail = recorder.Snapshot();
            if (tail.Length == 0) return;

            _file.Write(Encoding.UTF8.GetBytes($"---- flight recorder, {tail.Length} bytes ----\r\n"));
            _file.Write(tail);
            _file.Write("---- end flight recorder ----\r\n"u8);
        }

        private static void OnUnhandled(object sender, UnhandledExceptionEventArgs e)
        {
            LogChannel channel = LogChannel.For("Crash");
            WriteText(channel, LogLevel.Fatal, "", 0,
                e.ExceptionObject is Exception exception ? exception.ToString() : e.ExceptionObject?.ToString() ?? "unknown");

            DrainNow();
            DumpRecorder();
        }

        private static void OnProcessExit(object? sender, EventArgs e) => DrainNow();

        // A drain on the calling thread, for the paths where the spool thread is gone or about to be.
        private static void DrainNow()
        {
            _running = false;
            try
            {
                DrainOnce();
                Spill();
            }
            catch { }
        }

        // ---- the spool thread ----

        private static void Loop()
        {
            int poll = Math.Max(50, Math.Min(_flushMs, 250));

            while (_running)
            {
                _wake.WaitOne(poll);
                try { DrainOnce(); } catch { }
                poll = Math.Max(50, Math.Min(_flushMs, 250));
            }

            try
            {
                DrainOnce();
                Spill();
                _console.Flush();
                _file?.Dispose();
            }
            catch { }
        }

        private static void DrainOnce()
        {
            lock (_drain)
            {
                Config? request = _request;
                if (request != null)
                {
                    Apply(request);
                    _request = null;
                    _configAck.Set();
                }

                LogLane[] lanes = Volatile.Read(ref _lanes);
                int count = lanes.Length;
                if (count == 0) return;

                Span<long> pos = count <= 16 ? stackalloc long[count] : new long[count];
                Span<long> end = count <= 16 ? stackalloc long[count] : new long[count];
                Span<long> arenaTo = count <= 16 ? stackalloc long[count] : new long[count];

                for (int i = 0; i < count; i++)
                    lanes[i].BeginDrain(out pos[i], out end[i], out arenaTo[i]);

                bool urgent = false;

                // k-way merge across the lanes, so three threads interleave in the order they
                // actually logged rather than in lane order.
                while (true)
                {
                    int next = -1;
                    long earliest = long.MaxValue;
                    for (int i = 0; i < count; i++)
                    {
                        if (pos[i] >= end[i]) continue;
                        long at = lanes[i].At(pos[i]).timestamp;
                        if (at >= earliest) continue;
                        earliest = at;
                        next = i;
                    }
                    if (next < 0) break;

                    ref readonly LogRecord record = ref lanes[next].At(pos[next]);
                    int length = Compose(record, lanes[next].TextOf(record));
                    Publish(record.level, _line.AsSpan(0, length));
                    if (record.level >= LogLevel.Error) urgent = true;
                    pos[next]++;
                }

                for (int i = 0; i < count; i++)
                {
                    lanes[i].EndDrain(pos[i], arenaTo[i]);

                    long dropped = lanes[i].TakeDropped();
                    if (dropped > 0) Synthetic($"{dropped} log records dropped — lane full");
                }

                if (urgent || _spillPos >= _spill.Length / 2 || ElapsedMs(_lastSpill) >= _flushMs) Spill();
                _console.Flush();
            }
        }

        private static void Apply(Config config)
        {
            Spill();

            _consoleMin = config.consoleMin;
            _fileMin = config.fileMin;
            _recorderMin = config.recorderMin;
            _flushMs = config.flushMs;
            _file = config.file;

            if (config.bufferBytes != _spill.Length)
            {
                _spill = new byte[config.bufferBytes];
                _spillPos = 0;
            }

            _recorder = config.recorderBytes > 0 ? new FlightRecorder(config.recorderBytes) : null;
            _applied = true;

            foreach ((LogLevel level, byte[] line) in _pending)
            {
                if (_recorder != null && level >= _recorderMin) _recorder.Append(line);
                if (level >= _fileMin) AppendSpill(line);
            }
            _pending.Clear();
            Spill();
        }

        private static void Publish(LogLevel level, ReadOnlySpan<byte> line)
        {
            if (level >= _consoleMin) _console.Write(level, line);

            if (!_applied)
            {
                // Nothing knows its level yet — hold the line so the file and the recorder can be
                // filled in retroactively the moment they do.
                if (_pending.Count < pendingCap) _pending.Add((level, line.ToArray()));
                return;
            }

            if (_recorder != null && level >= _recorderMin) _recorder.Append(line);
            if (level >= _fileMin) AppendSpill(line);
        }

        private static void AppendSpill(ReadOnlySpan<byte> line)
        {
            if (line.Length > _spill.Length - _spillPos) Spill();
            if (line.Length > _spill.Length)
            {
                _file?.Write(line);
                return;
            }

            line.CopyTo(_spill.AsSpan(_spillPos));
            _spillPos += line.Length;
        }

        private static void Spill()
        {
            _lastSpill = Stopwatch.GetTimestamp();
            if (_spillPos == 0) return;

            _file?.Write(_spill.AsSpan(0, _spillPos));
            _spillPos = 0;
        }

        private static void Synthetic(string text)
        {
            DateTime now = DateTime.Now;
            Publish(LogLevel.Warn, Encoding.UTF8.GetBytes(
                $"{now:HH:mm:ss.fff} WARN  spool [Log] {text}\r\n"));
        }

        // ---- formatting ----

        private static int Compose(in LogRecord record, ReadOnlySpan<byte> text)
        {
            int need = text.Length + 256 + record.channel.Name.Length + record.file.Length;
            if (_line.Length < need) _line = new byte[RoundUpPow2(need)];

            Span<byte> dest = _line;
            int pos = 0;

            DateTime when = bootTime.AddTicks((long)((record.timestamp - bootTicks) * (10_000_000.0 / Stopwatch.Frequency)));
            when.TryFormat(dest, out int written, "HH:mm:ss.fff", CultureInfo.InvariantCulture);
            pos += written;

            pos += Put(dest.Slice(pos), " ");
            pos += Put(dest.Slice(pos), Tag(record.level));
            pos += Put(dest.Slice(pos), " ");
            pos += PutOrigin(dest.Slice(pos), record);
            pos += Put(dest.Slice(pos), " [");
            pos += Put(dest.Slice(pos), record.channel.Name);
            pos += Put(dest.Slice(pos), "] ");

            text.CopyTo(dest.Slice(pos));
            pos += text.Length;

            // Only the levels somebody will go looking for carry their call site; on Info it is noise.
            if (record.level >= LogLevel.Warn && record.file.Length != 0)
            {
                pos += Put(dest.Slice(pos), "  @");
                pos += Put(dest.Slice(pos), FileNameOf(record.file));
                pos += Put(dest.Slice(pos), ":");
                record.line.TryFormat(dest.Slice(pos), out written);
                pos += written;
            }

            dest[pos++] = (byte)'\r';
            dest[pos++] = (byte)'\n';
            return pos;
        }

        private static int PutOrigin(Span<byte> dest, in LogRecord record)
        {
            if (record.systemId == 0)
            {
                int at = Put(dest, "t");
                record.threadId.TryFormat(dest.Slice(at), out int idWritten);
                return at + idWritten;
            }

            IReadOnlyList<ThreadedSystem> systems = ThreadedSystem.All;
            int index = record.systemId - 1;

            int pos = Put(dest, index >= 0 && index < systems.Count ? systems[index].Name : "system");
            pos += Put(dest.Slice(pos), ":");
            record.epoch.TryFormat(dest.Slice(pos), out int written);
            return pos + written;
        }

        private static int Put(Span<byte> dest, ReadOnlySpan<char> value)
        {
            System.Text.Unicode.Utf8.FromUtf16(value, dest, out _, out int written,
                replaceInvalidSequences: true, isFinalBlock: true);
            return written;
        }

        private static ReadOnlySpan<char> FileNameOf(string path)
        {
            int slash = path.LastIndexOfAny(new[] { '\\', '/' });
            return slash < 0 ? path.AsSpan() : path.AsSpan(slash + 1);
        }

        private static string Tag(LogLevel level) => level switch
        {
            LogLevel.Hot => "HOT  ",
            LogLevel.Trace => "TRACE",
            LogLevel.Debug => "DEBUG",
            LogLevel.Info => "INFO ",
            LogLevel.Warn => "WARN ",
            LogLevel.Error => "ERROR",
            LogLevel.Fatal => "FATAL",
            _ => "?????",
        };

        private static string LogDirectory(string configured)
        {
            if (Path.IsPathRooted(configured)) return configured;

            string? writeRoot = SettingsRegistry.WriteRoot;
            string? parent = writeRoot == null ? null : Directory.GetParent(writeRoot)?.FullName;

            return Path.Combine(parent ?? AppContext.BaseDirectory, configured);
        }

        private static LogLevel Lowest(LogLevel a, LogLevel b) => a < b ? a : b;

        private static double ElapsedMs(long since) => (Stopwatch.GetTimestamp() - since) * 1000.0 / Stopwatch.Frequency;

        private static int RoundUpPow2(int value)
        {
            int result = 1;
            while (result < value) result <<= 1;
            return result;
        }
    }
}
