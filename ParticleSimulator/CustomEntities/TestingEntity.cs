using ArctisAurora.EngineWork.ECS.RenderingComponents.Vulkan;
using ArctisAurora.EngineWork.Renderer.MeshSubComponents;
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
            //if(transform.position.Y < 200.0f)
            //transform.SetWorldPosition(new Vector3D<float>(transform.position.X, transform.position.Y, transform.position.Z));
        }

        public void ChangeColor(Vector3D<float> color)
        {
            ((MCRaytracing)GetComponent<MeshComponent>()).UpdateColor(color);
        }
    }
}