using ArctisAurora.EngineWork.ECS.RenderingComponents.Vulkan;
using Silk.NET.Maths;
using System.Numerics;

namespace ArctisAurora.GameObject
{
    internal class LightSourceEntity : Entity
    {
        public LightSourceEntity()
        {
            this.CreateComponent<LightsourceComponent>();
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

        public void UpdateLightPosition(Vector3D<float> newPos)
        {
            transform.position = newPos;

            //GetComponent<LightsourceComponent>().UpdatePosition();
        }
    }
}
