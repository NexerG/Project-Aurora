using System.Diagnostics;
using ArctisAurora.Core.Animation;
using ArctisAurora.Core.Data;
using ArctisAurora.Core.Diagnostics;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.UI;
using ArctisAurora.EngineWork;
using ArctisAurora.EngineWork.Rendering;

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

        private DataPool? _done;
        private DataPool? _dirty;

        // Whether the next frame must run now. Drops wakeAt once a frame has reached it.
        internal bool Pending(ref double wakeAt)
        {
            if (wakeAt <= Engine.totalTime) wakeAt = double.PositiveInfinity;

            _done ??= DataManager.Get("AnimationDone");
            _dirty ??= DataManager.Get("LayoutDirty");
            return Animations.Awake.Length > 0 || _done.Count > 0 || _dirty.Count > 0 || Engine.HasPosted
                || InputHandler.instance.keyTracker.AnyDown || wakeAt <= Now();
        }

        // Blocks on OS events until one arrives or Engine.totalTime reaches wakeAt.
        internal void IdleWait(double wakeAt)
        {
            if (double.IsPositiveInfinity(wakeAt))
                AGlfwWindow._glfw.WaitEvents();
            else
                AGlfwWindow._glfw.WaitEventsTimeout(Math.Max(0.0, wakeAt - Now()));
        }

        // Engine.totalTime as of this instant rather than the frame's start.
        private double Now() => _lastTick == 0
            ? Engine.totalTime
            : Engine.totalTime + (Stopwatch.GetTimestamp() - _lastTick) / (double)Stopwatch.Frequency;

        [A_XSDActionDependency("Main.Input", "Frame")]
        private void Input()
        {
            // Tick to tick, and deliberately not LastTickMs: that is sampled before the pacing
            // sleep, so it measures the work a tick did rather than the time that passed — at 120Hz
            // with a cheap tick it runs an order of magnitude short. Key repeat, hold durations and
            // the tap window all count real seconds.
            long now = Stopwatch.GetTimestamp();

            Engine.deltaTime = Engine.fixedStep > 0 ? TimeSpan.FromSeconds(Engine.clockHeld ? 0 : Engine.fixedStep)
                : _lastTick == 0 ? TimeSpan.Zero
                : TimeSpan.FromSeconds((now - _lastTick) / (double)Stopwatch.Frequency);
            _lastTick = now;

            Engine.totalTime += Engine.deltaTime.TotalSeconds;

            Engine.engineInstance.Input();
        }

        [A_XSDActionDependency("Main.Logic", "Frame")]
        private void Logic()
        {
            Animations.DrainDone();
            Engine.engineInstance.Interpolate();
        }

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
