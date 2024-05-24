using OpenTK.Mathematics;
using ParticleSimulator.CustomEntityComponents;
using ParticleSimulator.EngineWork;
using ParticleSimulator.EngineWork.ECS.RenderingComponents;
using ParticleSimulator.GameObject;

namespace ParticleSimulator.CustomEntities
{
    internal class SimulatorEntity : Entity
    {
        public SimulatorEntity()
        {
            this.CreateComponent<SPHSimComponent>();
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
