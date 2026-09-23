using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using ArctisAurora.Core.Data.Commands;
using ArctisAurora.Core.Diagnostics;
using ArctisAurora.Core.Registry;

namespace ArctisAurora.Core.Threading
{

    // One engine system and the fixed order of work it does every tick: drain, Tick, publish. It runs
    // as a Step of Frame.frame.xml's graph or, listed as Dedicated, on a thread of its own.
    public abstract class ThreadedSystem
    {
        private static readonly LogChannel Log = LogChannel.For("Threading");

        [ThreadStatic] private static ThreadedSystem? _current;

        // Which system is running on the calling thread, or null outside one.
        public static ThreadedSystem? Current => _current;

        private static readonly List<ThreadedSystem> _all = new();

        public static IReadOnlyList<ThreadedSystem> All => _all;

        // Join key for Frame.frame.xml — a Step's or Dedicated's System attribute names one of these.
        public static ThreadedSystem? Find(string name)
        {
            for (int i = 0; i < _all.Count; i++)
                if (_all[i].Name == name) return _all[i];
            return null;
        }

        public string Name { get; }

        // Stamped into SystemCommand.Producer, so a command that faults on apply names its sender.
        // Assigned from construction order the same way DataManager assigns pool ids from parse
        // order — nothing in C# declares the mapping. Zero is left free to mean "no system".
        public byte SystemId { get; }

        public Thread? Worker { get; private set; }

        // Set by FrameScheduler for a system listed as Dedicated.
        public bool Dedicated { get; internal set; }

        // Lanes indexed by the other system's id: _inbox[p] carries p's commands here, _outbox[o]
        // carries ours to o. Null at the own-id slot — a system writes its own tables in place.
        private CommandLane?[] _inbox = Array.Empty<CommandLane?>();
        private CommandLane?[] _outbox = Array.Empty<CommandLane?>();

        // Wire every ordered pair of systems. Call once, after all systems are constructed and
        // before any starts.
        public static void BuildLanes(int commandCapacity = 1024, int arenaBytes = 64 * 1024)
        {
            int n = _all.Count + 1;   // ids are 1-based; index 0 is the "no system" hole

            for (int i = 0; i < _all.Count; i++)
            {
                _all[i]._inbox = new CommandLane?[n];
                _all[i]._outbox = new CommandLane?[n];
            }

            foreach (ThreadedSystem producer in _all)
            {
                foreach (ThreadedSystem owner in _all)
                {
                    if (ReferenceEquals(producer, owner)) continue;

                    CommandLane lane = new(producer.SystemId, owner.SystemId, commandCapacity, arenaBytes);
                    producer._outbox[owner.SystemId] = lane;
                    owner._inbox[producer.SystemId] = lane;
                }
            }
        }

        // Queues a message for target's OnPost. False on backpressure, or when not called from another system.
        public static bool Post<T>(ThreadedSystem target, ushort kind, in T message) where T : unmanaged
        {
            ThreadedSystem? producer = _current;
            if (producer == null) return false;

            CommandLane? lane = producer._outbox[target.SystemId];
            if (lane == null || !lane.HasSpace) return false;
            if (!lane.TryWritePayload(message, out int offset)) return false;

            return lane.TryEnqueue(SystemCommand.Post(kind, offset, Unsafe.SizeOf<T>(), producer.SystemId));
        }

        private volatile bool _running;
        private int _epoch;
        private double _lastTickMs;

        // Wall time the last completed tick took, including drain and publish but excluding the
        // pacing sleep. This is the per-thread tick rate counter, and main feeds it to deltaTime.
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

        // Runs as a step of the frame graph; FrameScheduler ticks it through Step().
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

        // One tick: queued commands in, the system's own work, its commands out.
        internal void Step()
        {
            ThreadedSystem? previous = _current;
            _current = this;
            long tickStart = Stopwatch.GetTimestamp();

            Volatile.Read(ref _epoch);

            Drain();
            Tick();
            Publish();

            Volatile.Write(ref _epoch, _epoch + 1);
            Volatile.Write(ref _lastTickMs, ElapsedMs(tickStart));
            _current = previous;
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

        protected abstract void Tick();
        protected virtual void OnStart() { }
        protected virtual void OnStop() { }

        // A message another system posted here, applied during Drain.
        protected virtual void OnPost(ushort kind, ReadOnlySpan<byte> payload) { }

        // Apply everything queued for this system. Never capped: a full drain
        // settles at the producer/consumer rate ratio, whereas a per-tick cap turns that into a
        // queue that grows forever once the producer outpaces it.
        private void Drain()
        {
            for (int i = 0; i < _inbox.Length; i++)
            {
                CommandLane? lane = _inbox[i];
                if (lane == null) continue;

                lane.BeginDrain(out long from, out long to, out long arenaTo);
                if (from == to) continue;

                for (long seq = from; seq < to; seq++)
                {
                    ref readonly SystemCommand cmd = ref lane.At(seq);
                    if (cmd.Op == CommandOp.Post)
                        OnPost(cmd.ColumnId, lane.Arena.ReadBytes(cmd.ArenaOffset, cmd.Count));
                    else
                        CommandApplier.Apply(cmd, lane.Arena);
                }

                lane.EndDrain(to, arenaTo);
            }
        }

        // Flush outbound lanes, so a tick's commands become visible as one batch. Publishing per
        // enqueue would let an owner drain half a tick's writes.
        private void Publish()
        {
            for (int i = 0; i < _outbox.Length; i++)
                _outbox[i]?.Commit();
        }

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
