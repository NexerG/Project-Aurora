using System.Diagnostics;
using System.Reflection;
using ArctisAurora.Core.Data;
using ArctisAurora.Core.Data.Commands;
using ArctisAurora.Core.Registry;

namespace ArctisAurora.Core.Threading
{

    // One engine thread and the fixed order of work it does every tick. Main, physics and render
    // each subclass this and fill in Tick(). Nothing else about the loop is theirs to change.
    //
    // The order is the point. Queued commands are drained before the system's own work runs, so a
    // tick sees one settled batch of outside changes instead of data moving under it mid-loop, and
    // outbound commands are flushed after. Threads never wait on each other, so the volatile epoch
    // read/write around the tick is the only thing left stopping the JIT from caching column reads
    // across ticks — the old AutoResetEvent handshake used to provide that for free.
    public abstract class ThreadedSystem
    {
        [ThreadStatic] private static ThreadedSystem? _current;

        // Which system is running on the calling thread, or null on a thread that is not one of
        // ours. Ownership checks use this to answer "am I allowed to write this table?" without
        // threading context through every call site.
        public static ThreadedSystem? Current => _current;

        private static readonly List<ThreadedSystem> _all = new();

        public static IReadOnlyList<ThreadedSystem> All => _all;

        // Join key for Pools.xml — a pool's System attribute names one of these.
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

        // Lanes indexed by the other system's id: _inbox[p] carries p's commands here, _outbox[o]
        // carries ours to o. Null at the own-id slot — a system writes its own tables in place.
        private CommandLane?[] _inbox = Array.Empty<CommandLane?>();
        private CommandLane?[] _outbox = Array.Empty<CommandLane?>();

        // Wire every ordered pair of systems. Call once, after all systems are constructed and
        // before any starts, alongside DataManager.ResolveOwners.
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

        // Queue a single-element write to a table this system does not own. False means the lane is
        // full — real backpressure, and the caller's problem: dropping silently would be data loss
        // and blocking here would re-couple the threads.
        public static bool Send<T>(DataHandle target, ushort columnId, in T value) where T : unmanaged
        {
            ThreadedSystem? producer = _current;
            if (producer == null) return false;

            DataPool pool = DataManager.Get(target.PoolId);
            CommandLane? lane = producer._outbox[pool.OwnerSystemId];
            if (lane == null) return false;   // this system owns the pool — write it in place

            if (!lane.HasSpace) return false;
            if (!lane.TryWritePayload(value, out int offset)) return false;

            return lane.TryEnqueue(SystemCommand.SetOne(target, columnId, offset, producer.SystemId));
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

        // Target tick period in milliseconds. Zero means do not pace — the render thread is already
        // paced by present/vsync, and sleeping on top of that would just drop frames.
        protected virtual double TargetPeriodMs => 0;

        // The name comes off the subclass's own [A_XSDType], which is also what Pools.xml's System
        // attribute refers to — so a system's name is written once, in one place.
        protected ThreadedSystem()
        {
            A_XSDTypeAttribute attr = GetType().GetCustomAttribute<A_XSDTypeAttribute>() ?? throw new Exception($"[ThreadedSystem] {GetType().Name} needs an [A_XSDType] naming it, so Pools.xml can assign pools to it.");

            Name = attr.Name;
            SystemId = (byte)(_all.Count + 1);
            _all.Add(this);
        }

        public void Start()
        {
            _running = true;
            Worker = new Thread(Loop) { Name = Name };
            Worker.Start();
        }

        // Run on the calling thread instead of spawning one, blocking until stopped. Main has to
        // use this: GLFW requires PollEvents on the thread that created the window, which is the
        // bootstrapping thread, so main cannot be handed a fresh one.
        public void Adopt()
        {
            _running = true;
            Worker = Thread.CurrentThread;
            Loop();
        }

        public void Stop() => _running = false;

        public void Join() => Worker?.Join();

        private void Loop()
        {
            _current = this;
            Console.WriteLine($"Starting {Name} system on managed thread {Environment.CurrentManagedThreadId}");
            OnStart();

            while (_running)
            {
                long tickStart = Stopwatch.GetTimestamp();

                Volatile.Read(ref _epoch);

                Drain();
                Tick();
                Publish();

                Volatile.Write(ref _epoch, _epoch + 1);
                Volatile.Write(ref _lastTickMs, ElapsedMs(tickStart));

                Pace(tickStart);
            }

            OnStop();
            _current = null;
        }

        protected abstract void Tick();
        protected virtual void OnStart() { }
        protected virtual void OnStop() { }

        // Apply everything queued for the tables this system owns. Never capped: a full drain
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
                    CommandApplier.Apply(lane.At(seq), lane.Arena);

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

        private void Pace(long tickStart)
        {
            double target = TargetPeriodMs;
            if (target <= 0) return;

            double remainingMs = target - ElapsedMs(tickStart);
            if (remainingMs >= 1.0)
                Thread.Sleep((int)remainingMs);
        }

        private static double ElapsedMs(long since) => (Stopwatch.GetTimestamp() - since) * 1000.0 / Stopwatch.Frequency;
    }
}
