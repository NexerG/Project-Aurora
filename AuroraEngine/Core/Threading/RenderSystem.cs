using ArctisAurora.Core.Registry;
using ArctisAurora.EngineWork;

namespace ArctisAurora.Core.Threading
{
    // Draws. Not paced — present and vsync already throttle it, and sleeping on top of that would
    // just drop frames, so TargetPeriodMs stays zero.
    [A_XSDType("Render", "Systems")]
    public sealed class RenderSystem : ThreadedSystem
    {
        // Startup gate only: park until main has completed one tick, so the first Draw() never runs
        // against a frame that has never been ticked. Main's epoch is published with release
        // semantics, so seeing it move guarantees everything that tick wrote is visible here.
        protected override void OnStart()
        {
            while (Running && Engine.mainSystem.Epoch == 0)
                Thread.Sleep(10);
        }

        protected override void Tick()
        {
            Engine.renderer.Draw();
        }
    }
}
