using ArctisAurora.Core.Data;
using ArctisAurora.Core.Diagnostics;

namespace ArctisAurora.Core.Threading
{
    // One Step of Frame.frame.xml: an action or a pool's edge, where it may run, and the columns it touches.
    public sealed class FrameStep
    {
        [ThreadStatic] private static FrameStep? _current;

        // The step running on the calling thread, or null outside one.
        public static FrameStep? Current => _current;

        // Lends a step to a thread running one of its Jobs.For chunks.
        internal static void SetCurrent(FrameStep? step) => _current = step;

        public string Name { get; }

        // the system the action belongs to, null for a frame edge
        public ThreadedSystem? System { get; }
        public bool Pinned { get; }
        public int Stage { get; internal set; }

        // column bits, indexed by pool id
        internal readonly ulong[] reads;
        internal readonly ulong[] writes;

        // the steps this one waits for, and the column that makes it wait
        internal readonly List<(FrameStep step, string column)> waits = new();

        internal bool due;
        private double _owedMs;
        private readonly Action _body;
        private readonly string _zone;

        internal FrameStep(string name, ThreadedSystem? system, Action body, bool pinned, ulong[] reads, ulong[] writes)
        {
            Name = name;
            System = system;
            _body = body;
            Pinned = pinned;
            this.reads = reads;
            this.writes = writes;
            _zone = "Step." + name;
        }

        internal static ulong AllColumns(DataPool pool) => pool.ColumnCount >= 64 ? ulong.MaxValue : (1UL << pool.ColumnCount) - 1;

        // True once the system's period has come round; a step without one runs every frame.
        internal bool Due(double dtMs)
        {
            double period = System?.Period ?? 0;
            if (period <= 0) return true;

            _owedMs += dtMs;
            if (_owedMs < period) return false;

            _owedMs = Math.Min(_owedMs - period, period);
            return true;
        }

        internal void Run()
        {
            FrameStep? previous = _current;
            _current = this;
            Profiling.Zone.Start(_zone);
            if (System != null) System.RunStep(_body);
            else _body();
            Profiling.Zone.End(_zone);
            _current = previous;
        }
    }
}
