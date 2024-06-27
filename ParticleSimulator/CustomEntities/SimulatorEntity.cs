using OpenTK.Mathematics;
using ArctisAurora.CustomEntityComponents;
using ArctisAurora.EngineWork;
using ArctisAurora.GameObject;
using ArctisAurora.EngineWork.ECS.RenderingComponents.OpenTK;

namespace ArctisAurora.CustomEntities
{
    internal class SimulatorEntity : Entity
    {
        public SimulatorEntity()
        {
            this.transform.scale *= 2;
            this.CreateComponent<SPHSimComponent_OpenTK>();
            this.CreateComponent<MeshComponent_OpenTK>();
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
