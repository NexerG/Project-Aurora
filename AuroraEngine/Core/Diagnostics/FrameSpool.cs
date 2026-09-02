using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Xml;
using ArctisAurora.Core.Registry;

namespace ArctisAurora.Core.Diagnostics
{
    // one timed span inside a captured frame, in ticks from the frame's start
    internal struct SpanRecord
    {
        public string name;
        public long begin;
        public long end;
        public int depth;
    }

    // one counter's tally against the span index it fired inside, -1 for none
    internal struct CounterRecord
    {
        public int span;
        public string name;
        public long value;
    }

    // one frame's slice of its batch's span and counter arrays
    internal struct FrameRecord
    {
        public long index;
        public long start;
        public long duration;
        public int firstSpan;
        public int spanCount;
        public int firstCounter;
        public int counterCount;
    }

    // A run of frames handed whole from the thread that recorded them to the spool that writes
    // them. The recording thread owns it until it is submitted and never touches it again after.
    internal sealed class CaptureBatch
    {
        public CaptureLane lane = null!;
        public int dropped;
        public bool last;

        public FrameRecord[] frames;
        public int frameCount;
        public SpanRecord[] spans;
        public int spanCount;
        public CounterRecord[] counters;
        public int counterCount;

        public CaptureBatch(int framesPerBatch)
        {
            frames = new FrameRecord[framesPerBatch];
            spans = new SpanRecord[framesPerBatch * 32];
            counters = new CounterRecord[framesPerBatch * 8];
        }

        public void Reset()
        {
            frameCount = 0;
            spanCount = 0;
            counterCount = 0;
            dropped = 0;
            last = false;
        }

        public int AddSpan(string name, long begin, int depth)
        {
            if (spanCount == spans.Length) Array.Resize(ref spans, spans.Length * 2);

            ref SpanRecord span = ref spans[spanCount];
            span.name = name;
            span.begin = begin;
            span.end = -1;
            span.depth = depth;
            return spanCount++;
        }

        public void CloseSpan(int index, long end) => spans[index].end = end;

        public void AddCounter(int span, string name, long value)
        {
            if (counterCount == counters.Length) Array.Resize(ref counters, counters.Length * 2);

            ref CounterRecord counter = ref counters[counterCount++];
            counter.span = span;
            counter.name = name;
            counter.value = value;
        }
    }

    // One thread's pool of batches. The recording thread pops, the spool thread pushes back, and an
    // empty pool is what turns into a dropped frame rather than a wait.
    internal sealed class CaptureLane
    {
        private const int poolSize = 3;

        private readonly Stack<CaptureBatch> _free = new Stack<CaptureBatch>();
        private readonly Lock _gate = new Lock();

        public string Thread { get; }

        public CaptureLane(string thread, int framesPerBatch)
        {
            Thread = thread;
            for (int i = 0; i < poolSize; i++) _free.Push(new CaptureBatch(framesPerBatch));
        }

        public CaptureBatch? Take()
        {
            lock (_gate)
            {
                if (_free.Count == 0) return null;

                CaptureBatch batch = _free.Pop();
                batch.Reset();
                batch.lane = this;
                return batch;
            }
        }

        public void Return(CaptureBatch batch)
        {
            lock (_gate) _free.Push(batch);
        }
    }

    // The frame-file writer. One background thread takes finished batches, turns them into XML and
    // appends them to one file per recording thread, so nothing on a timed thread formats text.
    //
    // A session is a folder of files, one per thread, named for the moment it started. Files are
    // left unterminated if the process dies mid-capture; a reader is expected to tolerate that.
    public static class FrameSpool
    {
        private static readonly LogChannel Log = LogChannel.For("Profiling");

        private const string ns = "http://arctisaurora/AuroraProfilingTypes";

        private static readonly XmlWriterSettings xmlSettings = new XmlWriterSettings
        {
            Indent = false,
            Encoding = new UTF8Encoding(false),
            CloseOutput = false,
            ConformanceLevel = ConformanceLevel.Document,
        };

        // queue to the spool thread
        private static readonly List<CaptureBatch> _pending = new List<CaptureBatch>();
        private static readonly Lock _queue = new Lock();
        private static readonly AutoResetEvent _wake = new AutoResetEvent(false);

        // the session and its open files, held by whoever writes or replaces them
        private static readonly Lock _files = new Lock();
        private static readonly Dictionary<string, Writer> _writers = new Dictionary<string, Writer>();
        private static string? _sessionDir;
        private static DateTime _sessionStarted;
        private static string _mode = "Burst";
        private static int _requested;

        private static Thread? _worker;
        private static volatile bool _running;
        private static bool _started;

        // settings
        internal static int framesPerBatch = 64;
        internal static int burstFrames = 300;
        private static string _directory = "Profiling";
        private static long _maxBytes = 64L * 1024 * 1024;
        private static int _keep = 5;

        // One thread's file, plus the name ids it has already declared in it.
        private sealed class Writer
        {
            public FileStream stream = null!;
            public XmlWriter xml = null!;
            public readonly Dictionary<string, int> names = new Dictionary<string, int>();
            public readonly List<string> undeclared = new List<string>();
            public long written;
            public int seq;
        }

        internal static void Configure(ProfilingCaptureSetting settings)
        {
            framesPerBatch = Math.Max(1, settings.framesPerBatch);
            burstFrames = Math.Max(1, settings.burstFrames);
            _directory = settings.directory;
            _maxBytes = Math.Max(1, settings.maxFileMB) * 1024L * 1024L;
            _keep = Math.Max(1, settings.keep);
        }

        // Closes whatever the previous session left open and arms a new folder, created lazily by
        // the first batch that arrives.
        internal static void BeginSession(string mode, int requested)
        {
            EnsureStarted();

            lock (_files)
            {
                CloseWriters();
                _sessionDir = null;
                _sessionStarted = DateTime.Now;
                _mode = mode;
                _requested = requested;
            }
        }

        internal static void Submit(CaptureBatch batch)
        {
            lock (_queue) _pending.Add(batch);
            _wake.Set();
        }

        internal static void Flush()
        {
            if (_worker == null) return;

            _running = false;
            _wake.Set();
            _worker.Join(2000);
        }

        private static void EnsureStarted()
        {
            lock (_queue)
            {
                if (_started) return;

                _started = true;
                _running = true;
                _worker = new Thread(Loop) { Name = "frames", IsBackground = true, Priority = ThreadPriority.BelowNormal };
                _worker.Start();
            }
        }

        // ---- the spool thread ----

        private static void Loop()
        {
            while (_running)
            {
                _wake.WaitOne(200);
                DrainOnce();
            }

            DrainOnce();
            lock (_files) CloseWriters();
        }

        private static void DrainOnce()
        {
            while (true)
            {
                CaptureBatch batch;
                lock (_queue)
                {
                    if (_pending.Count == 0) return;
                    batch = _pending[0];
                    _pending.RemoveAt(0);
                }

                lock (_files)
                {
                    try
                    {
                        WriteBatch(batch);
                    }
                    catch (Exception exception)
                    {
                        Log.Every(5000).Error($"frame capture write failed — {exception.Message}");
                        CloseWriters();
                        _sessionDir = null;
                    }
                }

                batch.lane.Return(batch);
            }
        }

        private static void WriteBatch(CaptureBatch batch)
        {
            Writer writer = WriterFor(batch.lane.Thread);

            Declare(writer, batch);
            if (writer.undeclared.Count > 0)
            {
                writer.xml.WriteStartElement("Names", ns);
                foreach (string name in writer.undeclared)
                {
                    writer.xml.WriteStartElement("N", ns);
                    Attribute(writer, "I", writer.names[name]);
                    writer.xml.WriteAttributeString("V", name);
                    writer.xml.WriteEndElement();
                }
                writer.xml.WriteEndElement();
                writer.undeclared.Clear();
            }

            writer.xml.WriteStartElement("Batch", ns);
            Attribute(writer, "Seq", writer.seq++);
            Attribute(writer, "Dropped", batch.dropped);

            for (int i = 0; i < batch.frameCount; i++)
                WriteFrame(writer, batch, ref batch.frames[i]);

            writer.xml.WriteEndElement();

            if (batch.last)
            {
                Close(writer);
                _writers.Remove(batch.lane.Thread);
                return;
            }

            writer.xml.Flush();
            writer.written = writer.stream.Position;

            // Rolling ends the session rather than renaming files, so every folder stays a
            // self-contained capture and the next batch opens a fresh one.
            if (writer.written >= _maxBytes)
            {
                CloseWriters();
                _sessionDir = null;
            }
        }

        private static void WriteFrame(Writer writer, CaptureBatch batch, ref FrameRecord frame)
        {
            writer.xml.WriteStartElement("F", ns);
            Attribute(writer, "I", frame.index);
            Attribute(writer, "T", frame.start);
            Attribute(writer, "D", frame.duration);

            int open = 0;
            for (int i = 0; i < frame.spanCount; i++)
            {
                int index = frame.firstSpan + i;
                ref SpanRecord span = ref batch.spans[index];

                while (open > span.depth)
                {
                    writer.xml.WriteEndElement();
                    open--;
                }

                writer.xml.WriteStartElement("Z", ns);
                Attribute(writer, "N", writer.names[span.name]);
                Attribute(writer, "B", span.begin);
                Attribute(writer, "E", span.end < 0 ? frame.duration : span.end);
                WriteCounters(writer, batch, ref frame, index);
                open = span.depth + 1;
            }

            while (open > 0)
            {
                writer.xml.WriteEndElement();
                open--;
            }

            WriteCounters(writer, batch, ref frame, -1);
            writer.xml.WriteEndElement();
        }

        private static void WriteCounters(Writer writer, CaptureBatch batch, ref FrameRecord frame, int span)
        {
            for (int i = 0; i < frame.counterCount; i++)
            {
                ref CounterRecord counter = ref batch.counters[frame.firstCounter + i];
                if (counter.span != span) continue;

                writer.xml.WriteStartElement("C", ns);
                Attribute(writer, "N", writer.names[counter.name]);
                Attribute(writer, "V", counter.value);
                writer.xml.WriteEndElement();
            }
        }

        private static void Declare(Writer writer, CaptureBatch batch)
        {
            for (int i = 0; i < batch.spanCount; i++) Declare(writer, batch.spans[i].name);
            for (int i = 0; i < batch.counterCount; i++) Declare(writer, batch.counters[i].name);
        }

        private static void Declare(Writer writer, string name)
        {
            if (writer.names.ContainsKey(name)) return;

            writer.names[name] = writer.names.Count;
            writer.undeclared.Add(name);
        }

        private static void Attribute(Writer writer, string name, long value) =>
            writer.xml.WriteAttributeString(name, value.ToString(CultureInfo.InvariantCulture));

        // ---- files ----

        private static Writer WriterFor(string thread)
        {
            if (_writers.TryGetValue(thread, out Writer? existing)) return existing;

            string directory = SessionDirectory();
            Writer writer = new Writer();
            writer.stream = new FileStream(Path.Combine(directory, thread + ".frames.xml"),
                FileMode.Create, FileAccess.Write, FileShare.Read);
            writer.xml = XmlWriter.Create(writer.stream, xmlSettings);

            writer.xml.WriteStartDocument();
            writer.xml.WriteStartElement("FrameCapture", ns);
            writer.xml.WriteAttributeString("Thread", thread);
            Attribute(writer, "Frequency", Stopwatch.Frequency);
            writer.xml.WriteAttributeString("Started", _sessionStarted.ToString("o", CultureInfo.InvariantCulture));
            writer.xml.WriteAttributeString("Mode", _mode);
            if (_requested > 0) Attribute(writer, "Requested", _requested);

            _writers[thread] = writer;
            return writer;
        }

        private static string SessionDirectory()
        {
            if (_sessionDir != null) return _sessionDir;

            string root = CaptureRoot();
            Directory.CreateDirectory(root);
            Prune(root);

            string stamp = _sessionStarted.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string candidate = Path.Combine(root, stamp);
            for (int i = 2; Directory.Exists(candidate); i++)
                candidate = Path.Combine(root, stamp + "-" + i.ToString(CultureInfo.InvariantCulture));

            _sessionDir = candidate;
            Directory.CreateDirectory(_sessionDir);
            return _sessionDir;
        }

        private static string CaptureRoot()
        {
            if (Path.IsPathRooted(_directory)) return _directory;

            string? writeRoot = SettingsRegistry.WriteRoot;
            string? parent = writeRoot == null ? null : Directory.GetParent(writeRoot)?.FullName;

            return Path.Combine(parent ?? AppContext.BaseDirectory, _directory);
        }

        private static void Prune(string root)
        {
            string[] sessions = Directory.GetDirectories(root);
            if (sessions.Length < _keep) return;

            Array.Sort(sessions, StringComparer.Ordinal);
            for (int i = 0; i <= sessions.Length - _keep; i++)
                try { Directory.Delete(sessions[i], true); } catch { }
        }

        private static void CloseWriters()
        {
            foreach (Writer writer in _writers.Values) Close(writer);
            _writers.Clear();
        }

        private static void Close(Writer writer)
        {
            try
            {
                writer.xml.WriteEndElement();
                writer.xml.WriteEndDocument();
                writer.xml.Flush();
                writer.xml.Dispose();
                writer.stream.Dispose();
            }
            catch { }
        }
    }
}
