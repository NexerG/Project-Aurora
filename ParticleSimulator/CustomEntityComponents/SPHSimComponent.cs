using ArctisAurora.EngineWork.ComponentBehaviour;
using ArctisAurora.EngineWork.ECS.RenderingComponents.Vulkan;
using ArctisAurora.ParticleTypes;
using ArctisAurora.Simulators.Vulkan;
using Silk.NET.Maths;

namespace ArctisAurora.CustomEntityComponents
{
    internal class SPHSimComponent : EntityComponent
    {
        internal List<Particle3D> _particles = new List<Particle3D>();
        internal Simulator3D _simulator;
        internal List<Matrix4X4<float>> _instanceMatrix = new List<Matrix4X4<float>>();

        public override void OnTick()
        {
            _simulator.Update(8f / 1000f);
            UpdatePositions3D(_particles);
        }

        internal void simSetup(int particleRoot)
        {
            float offsetX = (700 / 2) - (particleRoot * 7 / 2);
            float offsetY = (700 / 2) - (particleRoot * 7 / 2);
            float offsetZ = (700 / 2) - (particleRoot * 7 / 2);
            for(int i=0; i< particleRoot;i++)
            {
                for (int j = 0; j < particleRoot; j++)
                {
                    for (int k = 0; k < particleRoot; k++)
                    {
                        _particles.Add(new Particle3D((i * 7 + offsetX), (j * 7 + offsetY), k * 7 + offsetZ));
                    }
                }
            }

            _simulator = new Simulator3D(_particles, new Vector3D<float>(700, 700, 700));

            for (int i = 0; i < _particles.Count; i++)
            {
                Vector3D<float> pos = new Vector3D<float>(parent.transform.position.X, parent.transform.position.Y, parent.transform.position.Z);
                Quaternion<float> q = Quaternion<float>.CreateFromYawPitchRoll(parent.transform.rotation.X, parent.transform.rotation.Y, parent.transform.rotation.Z);

                Matrix4X4<float> transformation = Matrix4X4<float>.Identity;
                transformation *= Matrix4X4.CreateTranslation(pos);
                //transformation *= Matrix4X4.CreateFromQuaternion(q);
                //transformation *= Matrix4X4.CreateScale(parent.transform.scale);

                _instanceMatrix.Add(transformation);
            }
            parent.GetComponent<AVulkanMeshComponent>().MakeInstanced(ref _instanceMatrix);
        }
        public void UpdatePositions3D(List<Particle3D> ps)
        {
            for (int i = 0; i < ps.Count; i++)
            {
                Vector3D<float> pos = new Vector3D<float>(ps[i].point.X, ps[i].point.Y, ps[i].point.Z);
                Quaternion<float> q = Quaternion<float>.CreateFromYawPitchRoll(parent.transform.rotation.X, parent.transform.rotation.Y, parent.transform.rotation.Z);

                Matrix4X4<float> transformation = Matrix4X4<float>.Identity;
                transformation *= Matrix4X4.CreateTranslation(pos);
                transformation *= Matrix4X4.CreateFromQuaternion(q);
                transformation *= Matrix4X4.CreateScale(parent.transform.scale);

                _instanceMatrix[i] = transformation;
            }
        }

        public override void OnStart()
        {
            base.OnStart();
        }
    }
}