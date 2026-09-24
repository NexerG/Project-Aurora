using ArctisAurora.Core.Diagnostics;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ArctisAurora.Core.Threading
{
    // A loop body over rows [start, end).
    public interface IJobFor
    {
        void Execute(int start, int end);
    }

    // Splits one step's loop into chunks that the calling step and idle workers claim.
    public static class Jobs
    {
        private const int chunkBytes = 16 * 1024;
        private const int lineBytes = 64;

        // claim = chunk count << 32 | next chunk
        [StructLayout(LayoutKind.Explicit, Size = 192)]
        private struct Counters
        {
            [FieldOffset(0)] public long claim;
            [FieldOffset(64)] public int remaining;
            [FieldOffset(128)] public int joined;
        }

        private static Counters _counters;

        // the running For
        private static IJobFor? _kernel;
        private static FrameStep? _step;
        private static int _count;
        private static int _chunk;
        private static int _helpers;

        [ThreadStatic] private static bool _inChunk;

        // True on a thread while it runs a chunk.
        public static bool InChunk => _inChunk;

        internal static bool Claimable
        {
            get
            {
                long claim = Volatile.Read(ref _counters.claim);
                return (int)claim < (int)(claim >> 32);
            }
        }

        // Rows per chunk for rows of rowBytes: about 16 KB, in whole cache lines.
        public static int Chunk(int rowBytes)
        {
            int rows = Math.Max(1, chunkBytes / rowBytes);
            int line = lineBytes / Gcd(rowBytes, lineBytes);
            return (rows + line - 1) / line * line;
        }

        // Runs kernel over [0, count) chunk by chunk, on as many threads as there are chunks, and returns once every chunk is done.
        public static void For(int count, int rowBytes, IJobFor kernel)
        {
            AssertNotNested();
            int chunk = Chunk(rowBytes);
            int chunks = (count + chunk - 1) / chunk;
            int helpers = Math.Min(chunks, FrameScheduler.WorkerCount + 1) - 1;
            if (helpers <= 0 || Interlocked.CompareExchange(ref _kernel, kernel, null) != null)
            {
                RunInline(count, chunk, kernel);
                return;
            }

            _step = FrameStep.Current;
            _count = count;
            _chunk = chunk;
            Volatile.Write(ref _helpers, helpers);
            Volatile.Write(ref _counters.remaining, chunks);
            Interlocked.Exchange(ref _counters.claim, (long)chunks << 32);
            FrameScheduler.Wake(helpers);

            RunChunks();
            while (Volatile.Read(ref _counters.remaining) > 0)
                Thread.SpinWait(8);
            Volatile.Write(ref _kernel, null);
        }

        // A worker takes a place in the running For while it has chunks left and room for another thread.
        internal static bool Join()
        {
            if (!Claimable) return false;
            if (Interlocked.Increment(ref _counters.joined) <= Volatile.Read(ref _helpers)) return true;
            Interlocked.Decrement(ref _counters.joined);
            return false;
        }

        // A joined worker runs chunks until none are left.
        internal static void Help()
        {
            FrameStep? previous = FrameStep.Current;
            Profiling.Zone.Start("Jobs.Chunk");
            RunChunks();
            Profiling.Zone.End("Jobs.Chunk");
            FrameStep.SetCurrent(previous);
            Interlocked.Decrement(ref _counters.joined);
        }

        private static void RunChunks()
        {
            while (true)
            {
                long claim = Volatile.Read(ref _counters.claim);
                int chunks = (int)(claim >> 32), next = (int)claim;
                if (next >= chunks) return;
                if (Interlocked.CompareExchange(ref _counters.claim, claim + 1, claim) != claim) continue;

                FrameStep.SetCurrent(_step);
                int start = next * _chunk;
                _inChunk = true;
                _kernel!.Execute(start, Math.Min(start + _chunk, _count));
                _inChunk = false;
                Interlocked.Decrement(ref _counters.remaining);
            }
        }

        private static void RunInline(int count, int chunk, IJobFor kernel)
        {
            _inChunk = true;
            for (int start = 0; start < count; start += chunk)
                kernel.Execute(start, Math.Min(start + chunk, count));
            _inChunk = false;
        }

        [Conditional("DEBUG")]
        private static void AssertNotNested()
        {
            if (_inChunk) throw new InvalidOperationException("Jobs.For called from inside a Jobs.For chunk.");
        }

        private static int Gcd(int a, int b)
        {
            while (b != 0) (a, b) = (b, a % b);
            return a;
        }
    }
}
