using ArctisAurora.EngineWork.ECS.RenderingComponents.Vulkan;
using ArctisAurora.GameObject;
using Silk.NET.Maths;

namespace ArctisAurora.CustomEntities
{
    public class TestingEntity : Entity
    {
        public TestingEntity() 
        {
            CreateComponent<MeshComponent>();
        }

        public TestingEntity(Vector3D<float> Scale, Vector3D<float> position)
        {
            transform.SetWorldScale(Scale);
            transform.SetWorldPosition(position);
            CreateComponent<MeshComponent>();
        }

        public override void OnStart()
        {
            base.OnStart();
        }
        public override void OnTick()
        {
            base.OnTick();
            float r = 0.0025f;
            r = transform.GetEntityRotation().X + r;
            transform.SetRotationFromVector3(new Vector3D<float>(r,transform.GetEntityRotation().Y, transform.GetEntityRotation().Z));
        }
    }
}