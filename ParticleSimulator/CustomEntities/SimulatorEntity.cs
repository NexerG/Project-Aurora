using ArctisAurora.CustomEntityComponents;
using ArctisAurora.EngineWork.ECS.RenderingComponents.Vulkan;
using ArctisAurora.EngineWork.Rendering.Renderers.Renderer_Vulkan;
using ArctisAurora.GameObject;

namespace ArctisAurora.CustomEntities
{
    internal class SimulatorEntity : Entity
    {
        public SimulatorEntity()
        {
            this.transform.scale *= 2;
            this.CreateComponent<SPHSimComponent>();
            this.CreateComponent<AVulkanMeshComponent>();
            this.name = "SPH Simulation Entity";
        }

        public override void OnStart()
        {
            base.OnStart();
        }

        public override void OnTick()
        {
            base.OnTick();
        }
    }
}
