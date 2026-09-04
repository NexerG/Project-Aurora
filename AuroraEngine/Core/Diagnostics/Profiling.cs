using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Threading;

namespace ArctisAurora.Core.Diagnostics
{
    // CPU timing, gated at compile time. Every entry point is [Conditional] on DEBUG or PROFILE, so a
    // build with neither emits neither the call nor its arguments.
    //
    //     Profiling.Zone.Start("UI.Layout");
    //         Profiling.Zone.Increment("Measure");
    //     Profiling.Zone.End("UI.Layout");
    //
    // A zone times, an increment only counts, and an increment attributes to whichever zone is
    // innermost on the calling thread.
    //
    // A capture records every span of every frame, in order and nested, and hands whole batches of
    // them to FrameSpool to be written. The per-period tables below are unaffected by it.
    public static class Profiling
    {
        private static readonly LogChannel Log = LogChannel.For("Profiling");

        public static bool enabled = true;

        private const string rootZone = "(root)";
        private const int reportMs = 1000;

        // from ProfilingSettings
        internal static bool reportEnabled;

        // A capture a thread has not joined yet is one whose number differs from its own. Frames is
        // -1 for continuous, 0 for stopped.
        private static int _session;
        private static volatile int _sessionFrames;

        // --profile on the command line, which beats ProfilingCapture.Mode
        private static bool _bootArmed;

        // one zone over the current report period, plus the span it has open
        private struct ZoneData
        {
            public long calls;
            public long totalTicks;
            public long totalBytes;
            public long minTicks;
            public long maxTicks;
            public long openStart;
            public long openBytes;
            public int open;
        }

        // One thread's tables, written and read by that thread alone.
        private sealed class Tables
        {
            public readonly Dictionary<string, ZoneData> zones = new Dictionary<string, ZoneData>();
            public readonly Dictionary<(string zone, string name), long> counters = new Dictionary<(string zone, string name), long>();
            public string[] stack = new string[32];
            public int stackDepth;
            public long periodStart;
            public long periodBytes;

            // capture: the open zones' span indices, this frame's counters, and the batch being filled
            public int[] records = new int[32];
            public readonly Dictionary<(int span, string name), long> frameCounters = new Dictionary<(int span, string name), long>();
            public CaptureLane? lane;
            public CaptureBatch? batch;
            public bool capturing;
            public int session;
            public int remaining;
            public int dropped;
            public long frameStart;
            public long frameBytesStart;
            public long frameIndex;
            public int frameFirstSpan;
        }

        [ThreadStatic] private static Tables? _tables;

        public static class Zone
        {
            // Opens a span, or deepens one already open.
            [Conditional("DEBUG"), Conditional("PROFILE")]
            public static void Start(string name)
            {
                if (!enabled) return;

                Tables tables = _tables ??= new Tables();

                if (tables.stackDepth == tables.stack.Length)
                {
                    Array.Resize(ref tables.stack, tables.stack.Length * 2);
                    Array.Resize(ref tables.records, tables.records.Length * 2);
                }
                tables.stack[tables.stackDepth] = name;

                ref ZoneData zone = ref CollectionsMarshal.GetValueRefOrAddDefault(tables.zones, name, out bool existed);
                if (!existed) zone.minTicks = long.MaxValue;

                zone.calls++;

                bool capturing = tables.capturing;
                bool outermost = zone.open++ == 0;

                long now = 0;
                long bytes = 0;
                if (outermost || capturing)
                {
                    now = Stopwatch.GetTimestamp();
                    bytes = GC.GetAllocatedBytesForCurrentThread();
                }
                if (outermost)
                {
                    zone.openStart = now;
                    zone.openBytes = bytes;
                }
                if (capturing)
                    tables.records[tables.stackDepth] = tables.batch!.AddSpan(name, now - tables.frameStart, tables.stackDepth, bytes);

                tables.stackDepth++;
            }

            // Closes a span. Time accumulates only as the outermost one closes.
            [Conditional("DEBUG"), Conditional("PROFILE")]
            public static void End(string name)
            {
                if (!enabled) return;

                Tables? tables = _tables;
                if (tables == null) return;

                CheckTop(tables, name);

                bool popped = tables.stackDepth > 0;
                if (popped) tables.stackDepth--;

                ref ZoneData zone = ref CollectionsMarshal.GetValueRefOrNullRef(tables.zones, name);
                bool outermost = !Unsafe.IsNullRef(ref zone) && zone.open != 0 && --zone.open == 0;

                bool capturing = tables.capturing && popped;
                if (!outermost && !capturing) return;

                long now = Stopwatch.GetTimestamp();
                long bytes = GC.GetAllocatedBytesForCurrentThread();

                if (capturing)
                {
                    int record = tables.records[tables.stackDepth];
                    if (record >= 0) tables.batch!.CloseSpan(record, now - tables.frameStart, bytes);
                }

                if (!outermost) return;

                long elapsed = now - zone.openStart;
                zone.totalTicks += elapsed;
                zone.totalBytes += bytes - zone.openBytes;
                if (elapsed < zone.minTicks) zone.minTicks = elapsed;
                if (elapsed > zone.maxTicks) zone.maxTicks = elapsed;
            }

            // Counts one call against the innermost open zone, without reading the clock.
            [Conditional("DEBUG"), Conditional("PROFILE")]
            public static void Increment(string name)
            {
                if (!enabled) return;

                Tables tables = _tables ??= new Tables();
                string zone = tables.stackDepth > 0 ? tables.stack[tables.stackDepth - 1] : rootZone;

                ref long count = ref CollectionsMarshal.GetValueRefOrAddDefault(tables.counters, (zone, name), out _);
                count++;

                if (!tables.capturing) return;

                int span = tables.stackDepth > 0 ? tables.records[tables.stackDepth - 1] : -1;
                ref long frameCount = ref CollectionsMarshal.GetValueRefOrAddDefault(tables.frameCounters, (span, name), out _);
                frameCount++;
            }

            [Conditional("DEBUG")]
            private static void CheckTop(Tables tables, string name)
            {
                if (tables.stackDepth == 0)
                {
                    Log.Warn($"Zone.End(\"{name}\") with no zone open");
                    return;
                }

                string top = tables.stack[tables.stackDepth - 1];
                if (top != name) Log.Warn($"Zone.End(\"{name}\") closes \"{top}\"");
            }
        }

        public static class Frame
        {
            // Opens the recording window for one tick of the calling thread, on a lane named for its owner.
            [Conditional("DEBUG"), Conditional("PROFILE")]
            public static void Begin(string? owner = null)
            {
                if (!enabled) return;

                Tables tables = _tables ??= new Tables();
                Open(tables, owner);

                tables.frameStart = Stopwatch.GetTimestamp();
                tables.frameBytesStart = GC.GetAllocatedBytesForCurrentThread();
            }

            // Picks the lane and the batch this frame records into.
            private static void Open(Tables tables, string? owner)
            {
                ThreadedSystem? system = ThreadedSystem.Current;
                tables.frameIndex = system != null ? system.Epoch : tables.frameIndex + 1;

                int session = Volatile.Read(ref _session);
                if (tables.session != session)
                {
                    Hand(tables, true);
                    tables.session = session;
                    tables.remaining = _sessionFrames;
                    tables.dropped = 0;
                }

                tables.capturing = false;
                if (session == 0 || tables.remaining == 0) return;

                // a thread that gains a system identity moves to a lane of its own name
                string name = system?.Name ?? owner ?? $"t{Environment.CurrentManagedThreadId}";
                if (tables.lane != null && tables.lane.Thread != name)
                {
                    Hand(tables, true);
                    tables.lane = null;
                }

                if (tables.batch == null)
                {
                    tables.lane ??= new CaptureLane(name, FrameSpool.framesPerBatch);

                    tables.batch = tables.lane.Take();
                    if (tables.batch == null)
                    {
                        tables.dropped++;
                        return;
                    }
                    tables.batch.dropped = tables.dropped;
                    tables.dropped = 0;
                }

                // a zone left open across the frame edge has no record in this frame's batch
                for (int i = 0; i < tables.stackDepth; i++) tables.records[i] = -1;

                tables.frameCounters.Clear();
                tables.frameFirstSpan = tables.batch.spanCount;
                tables.capturing = true;
            }

            // Closes the frame, and hands the batch over once it is full or the burst has run out.
            [Conditional("DEBUG"), Conditional("PROFILE")]
            public static void End()
            {
                if (!enabled) return;

                Tables? tables = _tables;
                if (tables == null) return;

                long bytes = GC.GetAllocatedBytesForCurrentThread() - tables.frameBytesStart;
                tables.periodBytes += bytes;

                if (!tables.capturing) return;

                tables.capturing = false;

                CaptureBatch batch = tables.batch!;
                long duration = Stopwatch.GetTimestamp() - tables.frameStart;
                int firstCounter = batch.counterCount;

                foreach (KeyValuePair<(int span, string name), long> entry in tables.frameCounters)
                    batch.AddCounter(entry.Key.span, entry.Key.name, entry.Value);

                ref FrameRecord frame = ref batch.frames[batch.frameCount++];
                frame.index = tables.frameIndex;
                frame.start = tables.frameStart;
                frame.duration = duration;
                frame.bytes = bytes;
                frame.firstSpan = tables.frameFirstSpan;
                frame.spanCount = batch.spanCount - tables.frameFirstSpan;
                frame.firstCounter = firstCounter;
                frame.counterCount = batch.counterCount - firstCounter;

                bool finished = tables.remaining > 0 && --tables.remaining == 0;
                if (finished || batch.frameCount == batch.frames.Length) Hand(tables, finished);
            }

            private static void Hand(Tables tables, bool last)
            {
                if (tables.batch == null) return;

                tables.batch.last = last;
                FrameSpool.Submit(tables.batch);
                tables.batch = null;
            }
        }

        // Records the next N frames of every thread that ticks, then closes the files.
        public static void Capture(int frames)
        {
            if (frames <= 0) return;

            FrameSpool.BeginSession("Burst", frames);
            _sessionFrames = frames;
            Interlocked.Increment(ref _session);
        }

        [A_XSDActionDependency("Profiling.Capture", "Input", "Records the next BurstFrames frames of every thread to a frame file")]
        public static void Capture() => Capture(FrameSpool.burstFrames);

        // Opens a boot capture when --profile or --profile=N is on the command line. Runs before the
        // bootstrap phase, which is the only way the phase itself lands in one.
        public static bool ArmBoot()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 1; i < args.Length; i++)
            {
                string arg = args[i];
                bool valued = arg.StartsWith("--profile=", StringComparison.Ordinal);
                if (!valued && arg != "--profile") continue;

                int frames = FrameSpool.burstFrames;
                if (valued && (!int.TryParse(arg.AsSpan("--profile=".Length), out frames) || frames <= 0))
                {
                    Log.Warn($"{arg} — not a frame count, capturing {FrameSpool.burstFrames} instead");
                    frames = FrameSpool.burstFrames;
                }

                FrameSpool.BeginSession("Boot", frames);
                _sessionFrames = frames;
                Interlocked.Increment(ref _session);
                _bootArmed = true;

                Log.Info($"profiling from boot — {frames} frames");
                return true;
            }
            return false;
        }

        [A_XSDActionDependency("Profiling.Configure", "Bootstrap", "Applies ProfilingSettings and opens the capture its Mode asks for")]
        public static bool Configure()
        {
            ProfilingSettings settings = SettingsRegistry.Get<ProfilingSettings>();

            reportEnabled = settings.report.enabled;
            FrameSpool.Configure(settings.capture);

            if (_bootArmed) return true;

            if (settings.capture.mode == CaptureMode.Continuous)
            {
                FrameSpool.BeginSession("Continuous", 0);
                _sessionFrames = -1;
                Interlocked.Increment(ref _session);
            }
            else if (settings.capture.mode == CaptureMode.Boot)
            {
                FrameSpool.BeginSession("Boot", FrameSpool.burstFrames);
                _sessionFrames = FrameSpool.burstFrames;
                Interlocked.Increment(ref _session);
            }
            return true;
        }

        [A_XSDActionDependency("Profiling.Flush", "Shutdown", "Ends any capture and writes what the frame spool still holds")]
        public static bool Flush()
        {
            _sessionFrames = 0;
            Interlocked.Increment(ref _session);
            FrameSpool.Flush();
            return true;
        }

        // Prints the calling thread's zones once a period, then clears them.
        [Conditional("DEBUG"), Conditional("PROFILE")]
        public static void Report()
        {
            if (!enabled) return;

            Tables? tables = _tables;
            if (tables == null) return;

            long now = Stopwatch.GetTimestamp();
            if (tables.periodStart == 0)
            {
                tables.periodStart = now;
                return;
            }

            double periodMs = Ms(now - tables.periodStart);
            if (periodMs < reportMs) return;

            if (tables.stackDepth != 0)
            {
                Log.Warn($"report skipped, {tables.stackDepth} zone(s) open — innermost \"{tables.stack[tables.stackDepth - 1]}\"");
                return;
            }

            if (reportEnabled)
            {
                string owner = ThreadedSystem.Current?.Name ?? $"t{Environment.CurrentManagedThreadId}";

                Log.Info($"{owner} {periodMs:F0}ms — allocated {Bytes(tables.periodBytes)}");

                foreach (KeyValuePair<string, ZoneData> entry in tables.zones)
                {
                    ZoneData zone = entry.Value;
                    long min = zone.minTicks == long.MaxValue ? 0 : zone.minTicks;
                    Log.Info($"{owner} {periodMs:F0}ms — {entry.Key} {Ms(zone.totalTicks):F2}ms x{zone.calls} (min {Ms(min):F3} max {Ms(zone.maxTicks):F3}) {Bytes(zone.totalBytes)}{CountersOf(tables, entry.Key)}");
                }
            }

            tables.zones.Clear();
            tables.counters.Clear();
            tables.periodBytes = 0;
            tables.periodStart = now;
        }

        private static string CountersOf(Tables tables, string zone)
        {
            StringBuilder? line = null;
            foreach (KeyValuePair<(string zone, string name), long> entry in tables.counters)
            {
                if (entry.Key.zone != zone) continue;
                line ??= new StringBuilder(" —");
                line.Append(' ').Append(entry.Key.name).Append(' ').Append(entry.Value);
            }
            return line?.ToString() ?? string.Empty;
        }

        private static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

        private static string Bytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes}B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1}KB";
            if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F2}MB";
            return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2}GB";
        }
    }
}
