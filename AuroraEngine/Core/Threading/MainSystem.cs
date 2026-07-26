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

        protected override void Tick()
        {
            // deltaTime describes the tick that just finished, which is what it meant before the
            // loop moved here — consumers read it during a tick and get the previous one's length.
            Engine.deltaTime = TimeSpan.FromMilliseconds(LastTickMs);
            Engine.totalTime += Engine.deltaTime.TotalSeconds;

            Engine.engineInstance.MainTick();
        }
    }
}
