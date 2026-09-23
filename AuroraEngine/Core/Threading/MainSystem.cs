using System.Diagnostics;
using ArctisAurora.Core.Animation;
using ArctisAurora.Core.Diagnostics;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.UI;
using ArctisAurora.EngineWork;

namespace ArctisAurora.Core.Threading
{
    // Input, UI and entity logic. Pinned steps, because GLFW requires PollEvents on the thread that
    // created the window.
    //
    // The tick body still lives on Engine, which owns the registries and handlers it touches. This
    // class owns the loop discipline, not the work.
    [A_XSDType("Main", "Systems")]
    public sealed class MainSystem : ThreadedSystem
    {
        private long _lastTick;

        // Drops the baseline, so the tick after something that parked main for seconds — a native
        // window drag — starts from a zero delta instead of charging the whole stall to it.
        internal void ResyncClock() => _lastTick = 0;

        [A_XSDActionDependency("Main.Input", "Frame")]
        private void Input()
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

            Engine.engineInstance.Input();
        }

        [A_XSDActionDependency("Main.Logic", "Frame")]
        private void Logic() => Engine.engineInstance.Interpolate();

        [A_XSDActionDependency("Main.Apply", "Frame")]
        private void Apply() => Animations.ApplyValues();

        [A_XSDActionDependency("Main.Layout", "Frame")]
        private void Layout()
        {
            Profiling.Zone.Start("ResolveLayout");
            UIEngine.ResolveLayout();
            Profiling.Zone.End("ResolveLayout");
        }

        [A_XSDActionDependency("Main.DrawLists", "Frame")]
        private void DrawLists() => UIEngine.BuildDrawLists();
    }
}
