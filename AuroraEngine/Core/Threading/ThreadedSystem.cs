using System.Diagnostics;
using System.Reflection;
using ArctisAurora.Core.Diagnostics;
using ArctisAurora.Core.Registry;

namespace ArctisAurora.Core.Threading
{

    // One engine system. Its tagged methods run as Steps of Frame.frame.xml's graph or, listed as
    // Dedicated, its Tick runs on a thread of its own.
    public abstract class ThreadedSystem
    {
        private static readonly LogChannel Log = LogChannel.For("Threading");

        [ThreadStatic] private static ThreadedSystem? _current;

        // Which system is running on the calling thread, or null outside one.
        public static ThreadedSystem? Current => _current;

        private static readonly List<ThreadedSystem> _all = new();

        public static IReadOnlyList<ThreadedSystem> All => _all;

        // Join key for Frame.frame.xml — a Dedicated's System attribute names one of these.
        public static ThreadedSystem? Find(string name)
        {
            for (int i = 0; i < _all.Count; i++)
                if (_all[i].Name == name) return _all[i];
            return null;
        }

        public string Name { get; }

        // Stamped on log records. Assigned from construction order; zero means "no system".
        public byte SystemId { get; }

        public Thread? Worker { get; private set; }

        // Set by FrameScheduler for a system listed as Dedicated.
        public bool Dedicated { get; internal set; }

        private volatile bool _running;
        private int _epoch;
        private double _lastTickMs;

        // this frame's step time, and whether any step ran
        private double _frameMs;
        private bool _ranThisFrame;

        // Wall time of the last tick, or of a graph system's steps in the last frame it ran, excluding
        // the pacing sleep. This is the per-thread tick rate counter, and main feeds it to deltaTime.
        public double LastTickMs => Volatile.Read(ref _lastTickMs);

        // Bumped once per completed tick, published with release semantics: a reader that has seen
        // the new epoch is guaranteed to see everything this system wrote during that tick.
        public int Epoch => Volatile.Read(ref _epoch);

        public bool Running => _running;

        // Shortest time between ticks in milliseconds. Zero ticks every frame.
        protected virtual double TargetPeriodMs => 0;

        internal double Period => TargetPeriodMs;

        // The name comes off the subclass's own [A_XSDType], which is also what Frame.frame.xml's System
        // attribute refers to — so a system's name is written once, in one place.
        protected ThreadedSystem()
        {
            A_XSDTypeAttribute attr = GetType().GetCustomAttribute<A_XSDTypeAttribute>() ?? throw new Exception($"[ThreadedSystem] {GetType().Name} needs an [A_XSDType] naming it, so Frame.frame.xml can name it.");

            Name = attr.Name;
            SystemId = (byte)(_all.Count + 1);
            _all.Add(this);
        }

        // Runs on a thread of its own, outside the frame graph.
        public void Start()
        {
            _running = true;
            Worker = new Thread(Loop) { Name = Name };
            Worker.Start();
        }

        // Runs as steps of the frame graph; FrameScheduler runs them through RunStep.
        internal void StartScheduled()
        {
            _running = true;
            _current = this;
            Log.Info($"starting {Name} in the frame graph");
            OnStart();
            _current = null;
        }

        internal void StopScheduled()
        {
            _current = this;
            OnStop();
            _current = null;
        }

        public void Stop() => _running = false;

        public void Join() => Worker?.Join();

        // One tick of a dedicated system.
        private void Step()
        {
            ThreadedSystem? previous = _current;
            _current = this;
            long tickStart = Stopwatch.GetTimestamp();

            Volatile.Read(ref _epoch);

            Tick();

            Volatile.Write(ref _epoch, _epoch + 1);
            Volatile.Write(ref _lastTickMs, ElapsedMs(tickStart));
            _current = previous;
        }

        // Runs one of this system's frame steps on the calling thread.
        internal void RunStep(Action body)
        {
            ThreadedSystem? previous = _current;
            _current = this;
            long start = Stopwatch.GetTimestamp();

            body();

            _frameMs += ElapsedMs(start);
            _ranThisFrame = true;
            _current = previous;
        }

        // Publishes the frame's step time and epoch, when any step ran.
        internal void EndFrame()
        {
            if (!_ranThisFrame) return;

            Volatile.Write(ref _lastTickMs, _frameMs);
            Volatile.Write(ref _epoch, _epoch + 1);
            _frameMs = 0;
            _ranThisFrame = false;
        }

        private void Loop()
        {
            _current = this;
            Log.Info($"starting {Name} on managed thread {Environment.CurrentManagedThreadId}");

            try
            {
                OnStart();

                while (_running)
                {
                    long tickStart = Stopwatch.GetTimestamp();
                    Profiling.Frame.Begin();
                    Step();
                    Profiling.Frame.End();
                    Profiling.Report();

                    WaitOut(tickStart, Math.Max(TargetPeriodMs, FrameScheduler.CapPeriodMs));
                }

                OnStop();
            }
            catch (Exception exception)
            {
                Log.Exception(LogLevel.Fatal, exception, $"{Name} died on tick {Epoch}");
                LogSpool.DumpRecorder();
                throw;
            }
            finally
            {
                _current = null;
            }
        }

        protected virtual void Tick() { }
        protected virtual void OnStart() { }
        protected virtual void OnStop() { }

        // Sleeps out the rest of a period, spinning the last two milliseconds.
        internal static void WaitOut(long since, double periodMs)
        {
            if (periodMs <= 0) return;

            long end = since + (long)(periodMs * Stopwatch.Frequency / 1000.0);
            while (end - Stopwatch.GetTimestamp() > Stopwatch.Frequency / 500)
                Thread.Sleep(1);
            while (Stopwatch.GetTimestamp() < end)
                Thread.SpinWait(64);
        }

        private static double ElapsedMs(long since) => (Stopwatch.GetTimestamp() - since) * 1000.0 / Stopwatch.Frequency;
    }
}
