using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using ArctisAurora.Core.Data;
using ArctisAurora.Core.Diagnostics;
using ArctisAurora.Core.Registry;

namespace ArctisAurora.Core.Threading
{
    // Runs Frame.frame.xml: steps placed into stages by the columns they touch, stage after stage, the
    // main thread running pinned steps and helping with the rest.
    public static class FrameScheduler
    {
        private static readonly LogChannel Log = LogChannel.For("Threading");

        private const double spinMs = 0.05;

        // the graph
        private static FrameStep[][] _stages = Array.Empty<FrameStep[]>();
        private static readonly List<ThreadedSystem> _dedicated = new();
        private static readonly List<ThreadedSystem> _scheduled = new();

        // the stage being handed out
        private static FrameStep[] _queue = Array.Empty<FrameStep>();
        private static Counters _counters;

        // workers
        private static Thread[] _workers = Array.Empty<Thread>();
        private static readonly SemaphoreSlim _wake = new SemaphoreSlim(0);
        private static volatile bool _running;
        private static long _frame;

        private static ThreadingSettings _settings = null!;

        // claim = count << 32 | next index
        [StructLayout(LayoutKind.Explicit, Size = 192)]
        private struct Counters
        {
            [FieldOffset(0)] public long claim;
            [FieldOffset(64)] public int remaining;
            [FieldOffset(128)] public int parked;
        }

        public static long Frame => Volatile.Read(ref _frame);

        // Period the frame cap asks for, 0 when uncapped.
        public static double CapPeriodMs
        {
            get
            {
                int fps = _settings.frameCap.maxFps;
                return fps > 0 ? 1000.0 / fps : 0;
            }
        }

        #region ---- LOADING ----
        // Reads the graph, resolves each step's columns and places the steps into stages.
        public static void Load(string path)
        {
            XElement root = XElement.Load(path);
            XNamespace ns = root.GetDefaultNamespace();

            foreach (XElement element in root.Elements(ns + "Dedicated"))
            {
                string name = element.Attribute("System")?.Value ?? string.Empty;
                ThreadedSystem system = ThreadedSystem.Find(name) ?? throw new Exception($"[FrameScheduler] '{name}' is not a system.");
                if (system is MainSystem)
                    throw new Exception($"[FrameScheduler] Main cannot be Dedicated — GLFW needs it on the main thread, as a Pinned step.");
                if (system.Dedicated)
                    throw new Exception($"[FrameScheduler] {name} is listed as Dedicated twice.");

                system.Dedicated = true;
                _dedicated.Add(system);
            }

            Dictionary<string, (ThreadedSystem system, Action body)> actions = FindActions();
            HashSet<string> listed = new();
            List<FrameStep> steps = new();
            foreach (XElement element in root.Elements(ns + "Step"))
            {
                FrameStep step = Build(element, actions);
                if (!listed.Add(step.Name))
                    throw new Exception($"[FrameScheduler] {step.Name} is listed twice in Frame.frame.xml.");

                steps.Add(step);
                if (step.System != null && !_scheduled.Contains(step.System))
                    _scheduled.Add(step.System);
            }

            foreach (ThreadedSystem system in ThreadedSystem.All)
                if (!system.Dedicated && !_scheduled.Contains(system))
                    Log.Warn($"{system.Name} has no Step and is not Dedicated in Frame.frame.xml, so it never runs");

            Wire(steps);
            _stages = Place(steps);

            int widest = 0;
            for (int s = 0; s < _stages.Length; s++)
            {
                widest = Math.Max(widest, _stages[s].Length);
                Log.Info($"stage {s + 1}: {string.Join(", ", _stages[s].Select(x => x.Pinned ? x.Name + " (main thread)" : x.Name))}");
            }
            _queue = new FrameStep[widest];
        }

        // Every [A_XSDActionDependency(name, "Frame")] method of a system, bound to that system.
        private static Dictionary<string, (ThreadedSystem system, Action body)> FindActions()
        {
            Dictionary<string, (ThreadedSystem, Action)> actions = new();
            foreach (ThreadedSystem system in ThreadedSystem.All)
            {
                foreach (MethodInfo method in system.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    A_XSDActionDependencyAttribute? attr = method.GetCustomAttribute<A_XSDActionDependencyAttribute>();
                    if (attr == null || attr.Category != "Frame") continue;
                    if (!actions.TryAdd(attr.Name, (system, method.CreateDelegate<Action>(system))))
                        throw new Exception($"[FrameScheduler] Frame action '{attr.Name}' is declared twice.");
                }
            }
            return actions;
        }

        // A Step element as an action of a system, or as the frame edge of a pool.
        private static FrameStep Build(XElement element, Dictionary<string, (ThreadedSystem system, Action body)> actions)
        {
            string action = element.Attribute("Action")?.Value ?? string.Empty;
            string edge = element.Attribute("Edge")?.Value ?? string.Empty;
            bool pinned = bool.Parse(element.Attribute("Pinned")?.Value ?? "false");
            if (action.Length > 0 == edge.Length > 0)
                throw new Exception($"[FrameScheduler] a Step names either an Action or an Edge: '{element}'.");

            if (edge.Length > 0)
            {
                if (!DataManager.TryGet(edge, out DataPool pool))
                    throw new Exception($"[FrameScheduler] Edge names pool '{edge}', which does not exist.");

                ulong[] writes = new ulong[DataManager.Pools.Count];
                writes[pool.Id] = FrameStep.AllColumns(pool);
                return new FrameStep("Edge." + edge, null, pool.FrameEdge, pinned, new ulong[writes.Length], writes);
            }

            if (!actions.TryGetValue(action, out (ThreadedSystem system, Action body) found))
                throw new Exception($"[FrameScheduler] '{action}' is not a Frame action.");
            if (found.system.Dedicated)
                throw new Exception($"[FrameScheduler] {action} belongs to {found.system.Name}, which is Dedicated and runs its own loop.");

            return new FrameStep(action, found.system, found.body, pinned, Access(element, "Reads", action), Access(element, "Writes", action));
        }

        // Column bits per pool id, from a list such as "UIElements Entities.TransformData".
        private static ulong[] Access(XElement element, string attribute, string step)
        {
            ulong[] masks = new ulong[DataManager.Pools.Count];
            string list = element.Attribute(attribute)?.Value ?? string.Empty;

            foreach (string entry in list.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                int dot = entry.IndexOf('.');
                string poolName = dot < 0 ? entry : entry.Substring(0, dot);
                if (!DataManager.TryGet(poolName, out DataPool pool))
                    throw new Exception($"[FrameScheduler] {step} {attribute} names pool '{poolName}', which does not exist.");

                if (dot < 0)
                {
                    masks[pool.Id] = FrameStep.AllColumns(pool);
                    continue;
                }

                Type? column = DataManager.ResolveComponent(entry.Substring(dot + 1));
                if (column == null || !pool.HasComponent(column))
                    throw new Exception($"[FrameScheduler] {step} {attribute} names '{entry}', which is not a column of {poolName}.");
                masks[pool.Id] |= 1UL << pool.ColumnId(column);
            }
            return masks;
        }

        // A reader waits for every writer of its column; writers of one column go in list order.
        private static void Wire(List<FrameStep> steps)
        {
            for (int i = 0; i < steps.Count; i++)
            {
                FrameStep step = steps[i];
                for (int j = 0; j < steps.Count; j++)
                {
                    if (j == i) continue;
                    FrameStep other = steps[j];

                    for (int p = 0; p < step.writes.Length; p++)
                    {
                        ulong clash = step.reads[p] & ~step.writes[p] & other.writes[p];
                        if (j < i) clash |= step.writes[p] & other.writes[p];
                        if (clash != 0)
                            step.waits.Add((other, ColumnName(DataManager.Get((ushort)p), clash)));
                    }
                }
            }

            // steps of one system never overlap: unordered pairs go in list order
            for (int i = 0; i < steps.Count; i++)
            {
                for (int j = 0; j < i; j++)
                {
                    if (steps[i].System == null || steps[i].System != steps[j].System) continue;
                    if (Reaches(steps[i], steps[j]) || Reaches(steps[j], steps[i])) continue;
                    steps[i].waits.Add((steps[j], "same system"));
                }
            }
        }

        // True when from waits for to, directly or through other steps.
        private static bool Reaches(FrameStep from, FrameStep to)
        {
            Stack<FrameStep> open = new();
            HashSet<FrameStep> seen = new();
            open.Push(from);
            while (open.Count > 0)
            {
                foreach ((FrameStep next, string _) in open.Pop().waits)
                {
                    if (next == to) return true;
                    if (seen.Add(next)) open.Push(next);
                }
            }
            return false;
        }

        // Each step lands one stage after the latest step it waits for.
        private static FrameStep[][] Place(List<FrameStep> steps)
        {
            int[] pending = new int[steps.Count];
            List<int>[] dependents = new List<int>[steps.Count];
            for (int i = 0; i < steps.Count; i++) dependents[i] = new List<int>();
            for (int i = 0; i < steps.Count; i++)
            {
                pending[i] = steps[i].waits.Count;
                foreach ((FrameStep waitFor, string _) in steps[i].waits)
                    dependents[steps.IndexOf(waitFor)].Add(i);
            }

            Queue<int> ready = new();
            for (int i = 0; i < steps.Count; i++)
                if (pending[i] == 0) ready.Enqueue(i);

            int placed = 0, stageCount = 0;
            while (ready.Count > 0)
            {
                int i = ready.Dequeue();
                FrameStep step = steps[i];
                foreach ((FrameStep waitFor, string _) in step.waits)
                    step.Stage = Math.Max(step.Stage, waitFor.Stage + 1);

                stageCount = Math.Max(stageCount, step.Stage + 1);
                placed++;
                foreach (int d in dependents[i])
                    if (--pending[d] == 0) ready.Enqueue(d);
            }

            if (placed < steps.Count)
                throw new Exception($"[FrameScheduler] Frame.frame.xml steps wait on each other in a loop: {Loop(steps, pending)}");

            FrameStep[][] stages = new FrameStep[stageCount][];
            for (int s = 0; s < stageCount; s++)
                stages[s] = steps.Where(x => x.Stage == s).ToArray();
            return stages;
        }

        // Follows waits among the unplaced steps until one repeats, and names that loop.
        private static string Loop(List<FrameStep> steps, int[] pending)
        {
            FrameStep step = steps[Array.FindIndex(pending, p => p > 0)];
            List<FrameStep> path = new();
            while (!path.Contains(step))
            {
                path.Add(step);
                step = step.waits.First(w => pending[steps.IndexOf(w.step)] > 0).step;
            }

            List<string> links = new();
            for (int i = path.IndexOf(step); i < path.Count; i++)
            {
                FrameStep from = path[i];
                FrameStep to = i + 1 < path.Count ? path[i + 1] : step;
                links.Add($"{from.Name} waits for {to.Name} ({from.waits.First(w => w.step == to).column})");
            }
            return string.Join(" → ", links);
        }

        private static string ColumnName(DataPool pool, ulong bits)
        {
            int column = System.Numerics.BitOperations.TrailingZeroCount(bits);
            return $"{pool.Name}.{pool.ColumnAt((ushort)column).ElementType.Name}";
        }
        #endregion

        #region ---- RUNNING ----
        // Starts the dedicated threads and the workers.
        public static void Start()
        {
            _settings = SettingsRegistry.Get<ThreadingSettings>();

            int workers = _settings.threads.count > 0
                ? _settings.threads.count - 1
                : Environment.ProcessorCount - 1 - _dedicated.Count;
            workers = Math.Max(0, workers);

            _running = true;
            foreach (ThreadedSystem system in _dedicated)
                system.Start();

            _workers = new Thread[workers];
            for (int i = 0; i < workers; i++)
            {
                string lane = $"Worker {i + 1}";
                _workers[i] = new Thread(() => Work(lane)) { Name = lane, IsBackground = true };
                _workers[i].Start();
            }

            Log.Info($"{workers} worker(s) beside the main thread, {_dedicated.Count} dedicated, {Environment.ProcessorCount} logical cores");
        }

        // The frame loop, on the main thread, until Stop.
        public static void Run()
        {
            foreach (ThreadedSystem system in _scheduled)
                system.StartScheduled();

            try
            {
                long last = Stopwatch.GetTimestamp();
                while (_running)
                {
                    long frameStart = Stopwatch.GetTimestamp();
                    double dtMs = (frameStart - last) * 1000.0 / Stopwatch.Frequency;
                    last = frameStart;

                    Profiling.Frame.Begin("Main", Frame);
                    for (int s = 0; s < _stages.Length; s++)
                        RunStage(_stages[s], dtMs);
                    for (int i = 0; i < _scheduled.Count; i++)
                        _scheduled[i].EndFrame();

                    IReadOnlyList<DataPool> pools = DataManager.Pools;
                    for (int i = 0; i < pools.Count; i++)
                        Profiling.Frame.Pool(pools[i].Name, pools[i].Count, pools[i].Capacity, pools[i].ReservedBytes);
                    Profiling.Frame.End();
                    Profiling.Report();

                    Volatile.Write(ref _frame, _frame + 1);
                    ThreadedSystem.WaitOut(frameStart, CapPeriodMs);
                }
            }
            catch (Exception exception)
            {
                Log.Exception(LogLevel.Fatal, exception, $"{FrameStep.Current?.Name ?? "the frame"} died on frame {Frame}");
                LogSpool.DumpRecorder();
                throw;
            }

            foreach (ThreadedSystem system in _scheduled)
                system.StopScheduled();
        }

        public static void Stop()
        {
            _running = false;
            _wake.Release(Math.Max(1, _workers.Length));
        }

        private static void RunStage(FrameStep[] stage, double dtMs)
        {
            int count = 0;
            for (int i = 0; i < stage.Length; i++)
            {
                FrameStep step = stage[i];
                step.due = step.Due(dtMs);
                if (step.due && !step.Pinned)
                    _queue[count++] = step;
            }

            Volatile.Write(ref _counters.remaining, count);
            Interlocked.Exchange(ref _counters.claim, (long)count << 32);
            int parked = Volatile.Read(ref _counters.parked);
            if (count > 0 && parked > 0)
                _wake.Release(Math.Min(count, parked));

            for (int i = 0; i < stage.Length; i++)
                if (stage[i].due && stage[i].Pinned)
                    stage[i].Run();

            Profiling.Zone.Start("Scheduler.Barrier");
            while (Volatile.Read(ref _counters.remaining) > 0)
            {
                FrameStep? step = Claim();
                if (step == null)
                {
                    Thread.SpinWait(8);
                    continue;
                }
                step.Run();
                Interlocked.Decrement(ref _counters.remaining);
            }
            Profiling.Zone.End("Scheduler.Barrier");
        }

        // Takes one unclaimed step of the current stage, or null when none is left.
        private static FrameStep? Claim()
        {
            while (true)
            {
                long claim = Volatile.Read(ref _counters.claim);
                int count = (int)(claim >> 32), next = (int)claim;
                if (next >= count) return null;
                if (Interlocked.CompareExchange(ref _counters.claim, claim + 1, claim) == claim)
                    return _queue[next];
            }
        }

        private static bool Claimable()
        {
            long claim = Volatile.Read(ref _counters.claim);
            return (int)claim < (int)(claim >> 32);
        }

        // A worker: claims steps, spins briefly when there are none, then parks until woken.
        private static void Work(string lane)
        {
            long spinTicks = (long)(spinMs * Stopwatch.Frequency / 1000.0);
            long idleSince = Stopwatch.GetTimestamp();
            long openFrame = -1;

            try
            {
                while (_running)
                {
                    FrameStep? step = Claim();
                    if (step != null)
                    {
                        long frame = Frame;
                        if (frame != openFrame)
                        {
                            if (openFrame >= 0)
                            {
                                Profiling.Frame.End();
                                Profiling.Report();
                            }
                            Profiling.Frame.Begin(lane, frame);
                            openFrame = frame;
                        }

                        step.Run();
                        Interlocked.Decrement(ref _counters.remaining);
                        idleSince = Stopwatch.GetTimestamp();
                        continue;
                    }

                    if (Stopwatch.GetTimestamp() - idleSince < spinTicks)
                    {
                        Thread.SpinWait(32);
                        continue;
                    }

                    Interlocked.Increment(ref _counters.parked);
                    if (!Claimable())
                        _wake.Wait();
                    Interlocked.Decrement(ref _counters.parked);
                    idleSince = Stopwatch.GetTimestamp();
                }
            }
            catch (Exception exception)
            {
                Log.Exception(LogLevel.Fatal, exception, $"{FrameStep.Current?.Name ?? lane} died on frame {Frame}");
                LogSpool.DumpRecorder();
                throw;
            }
        }
        #endregion
    }
}
