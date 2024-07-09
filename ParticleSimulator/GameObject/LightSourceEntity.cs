using ArctisAurora.EngineWork.ECS.RenderingComponents.Vulkan;

namespace ArctisAurora.GameObject
{
    internal class LightSourceEntity : Entity
    {
        public LightSourceEntity()
        {
            this.CreateComponent<AVulkanLightsourceComponent>();
            this.name = "Light Source Entity";
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
