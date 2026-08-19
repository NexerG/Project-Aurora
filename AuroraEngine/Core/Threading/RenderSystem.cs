using ArctisAurora.Core.Registry;
using ArctisAurora.EngineWork;
using ArctisAurora.EngineWork.Rendering;

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

        // Every Vulkan object a window owns is created and destroyed here, because this is the thread
        // that uses them. Main only ever makes and unmakes the OS window and flags this side.
        protected override void Tick()
        {
            foreach (RenderWindow window in Engine.windows.Values)
            {
                if (window.closeRequested)
                {
                    if (window.gpuDestroyed) continue;

                    // closed before it was ever built — there is nothing to free, but main still
                    // needs the go-ahead to destroy the OS window
                    if (window.gpuReady)
                    {
                        Renderer.vk.DeviceWaitIdle(Renderer.logicalDevice);
                        window.DestroyGpuResources();
                    }
                    window.gpuDestroyed = true;
                    continue;
                }

                if (!window.gpuReady)
                {
                    window.CreateGpuResources();
                    window.gpuReady = true;
                    continue;
                }

                Engine.renderer.Draw(window);
            }
        }
    }
}
