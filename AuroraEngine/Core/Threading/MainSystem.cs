using System.Diagnostics;
using ArctisAurora.Core.Registry;
using ArctisAurora.EngineWork;

namespace ArctisAurora.Core.Threading
{
    // Input, UI and entity logic. Runs on the thread that bootstrapped the engine rather than a
    // spawned one, because GLFW requires PollEvents on the thread that created the window — so this
    // system is always started with Adopt(), never Start().
    //
    // The tick body still lives on Engine, which owns the registries and handlers it touches. This
    // class owns the loop discipline, not the work.
    [A_XSDType("Main", "Systems")]
    public sealed class MainSystem : ThreadedSystem
    {
        protected override double TargetPeriodMs => 1000.0 / 120.0;

        private long _lastTick;

        protected override void Tick()
        {
            // Tick to tick, and deliberately not LastTickMs: that is sampled before the pacing
            // sleep, so it measures the work a tick did rather than the time that passed — at 120Hz
            // with a cheap tick it runs an order of magnitude short. Key repeat, hold durations and
            // the tap window all count real seconds.
            long now = Stopwatch.GetTimestamp();

            Engine.deltaTime = _lastTick == 0
                ? TimeSpan.Zero
                : TimeSpan.FromSeconds((now - _lastTick) / (double)Stopwatch.Frequency);
            _lastTick = now;

            Engine.totalTime += Engine.deltaTime.TotalSeconds;

            Engine.engineInstance.MainTick();
        }
    }
}
