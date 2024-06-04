using OpenTK.Mathematics;
using ArctisAurora.CustomEntityComponents;
using ArctisAurora.EngineWork;
using ArctisAurora.EngineWork.ECS.RenderingComponents;
using ArctisAurora.GameObject;

namespace ArctisAurora.CustomEntities
{
    internal class SimulatorEntity : Entity
    {
        public SimulatorEntity()
        {
            this.transform.scale *= 2;
            this.CreateComponent<SPHSimComponent>();
            this.CreateComponent<MeshComponent>();
            this.name= "SPH Simulation Entity";
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
