using ArctisAurora.CustomEntityComponents;
using ArctisAurora.EngineWork.ECS.RenderingComponents.Vulkan;
using ArctisAurora.GameObject;

namespace ArctisAurora.CustomEntities
{
    internal class TestingEntity : Entity
    {
        public TestingEntity() 
        {
            this.CreateComponent<MeshComponent>();
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
