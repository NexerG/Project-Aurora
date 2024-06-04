using OpenTK.Mathematics;
using ArctisAurora.EngineWork;
using ArctisAurora.EngineWork.ComponentBehaviour;
using ArctisAurora.ParticleTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using ArctisAurora.EngineWork.ECS.RenderingComponents;
using System.Diagnostics;

namespace ArctisAurora.CustomEntityComponents
{
    internal class SPHSimComponent : EntityComponent
    {
        internal List<Particle3D> _particles = new List<Particle3D>();
        internal Simulator3D _simulator;
        internal List<Matrix4> _instanceMatrix = new List<Matrix4>();

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

            _simulator = new Simulator3D(_particles, new Vector3(700, 700, 700));

            for (int i = 0; i < _particles.Count; i++)
            {
                Vector3 pos = new Vector3(parent.transform.position.X, parent.transform.position.Y, parent.transform.position.Z);
                Quaternion q = Quaternion.FromEulerAngles(parent.transform.rotation);

                Matrix4 transformation = Matrix4.Identity;
                transformation *= Matrix4.CreateTranslation(pos);
                transformation *= Matrix4.CreateFromQuaternion(q);
                transformation *= Matrix4.CreateScale(parent.transform.scale);

                _instanceMatrix.Add(transformation);
            }
            parent.GetComponent<MeshComponent>().MakeInstanced(_instanceMatrix.Count, ref _instanceMatrix);
        }
        public void UpdatePositions3D(List<Particle3D> ps)
        {
            for (int i = 0; i < ps.Count; i++)
            {
                Vector3 pos = new Vector3(ps[i].point.X, ps[i].point.Y, ps[i].point.Z);
                Quaternion q = Quaternion.FromEulerAngles(parent.transform.rotation);

                Matrix4 transformation = Matrix4.Identity;
                transformation *= Matrix4.CreateTranslation(pos);
                transformation *= Matrix4.CreateFromQuaternion(q);
                transformation *= Matrix4.CreateScale(parent.transform.scale);

                _instanceMatrix[i] = transformation;
            }
        }

        public override void OnStart()
        {
            base.OnStart();
        }
    }
}