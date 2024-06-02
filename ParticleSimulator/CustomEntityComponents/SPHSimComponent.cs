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
                Vector3 posTrans = new Vector3(_particles[i].point.X, _particles[i].point.Y, _particles[i].point.Z);
                Quaternion q = new Quaternion(0.0f, 1.0f, 0.0f, 1.0f);
                Vector3 sc = new Vector3(5.0f, 5.0f, 5.0f);

                Matrix4 translation = Matrix4.Identity;
                Matrix4 rotation = Matrix4.Identity;
                Matrix4 scale = Matrix4.Identity;

                Matrix4.CreateTranslation(posTrans, out translation);
                Matrix4.CreateFromQuaternion(q, out rotation);
                Matrix4.CreateScale(sc, out scale);

                Matrix4 tr = Matrix4.Mult(translation, rotation);
                Matrix4 tr_s = Matrix4.Mult(scale, tr);

                _instanceMatrix.Add(tr_s);
            }
            parent.GetComponent<MeshComponent>().MakeInstanced(_instanceMatrix.Count, ref _instanceMatrix);
        }
        public void UpdatePositions3D(List<Particle3D> ps)
        {
            for (int i = 0; i < ps.Count; i++)
            {
                Vector3 posTrans = new Vector3(ps[i].point.X, ps[i].point.Y, ps[i].point.Z);
                Quaternion q = new Quaternion(0.0f, 1.0f, 0.0f, 1.0f);
                Vector3 sc = new Vector3(5.0f, 5.0f, 5.0f);

                Matrix4 translation = Matrix4.Identity;
                Matrix4 rotation = Matrix4.Identity;
                Matrix4 scale = Matrix4.Identity;

                Matrix4.CreateTranslation(posTrans, out translation);
                Matrix4.CreateFromQuaternion(q, out rotation);
                Matrix4.CreateScale(sc, out scale);

                Matrix4 tr = Matrix4.Mult(translation, rotation);
                Matrix4 tr_s = Matrix4.Mult(scale, tr);

                _instanceMatrix[i] = tr_s;
            }
        }

        public override void OnStart()
        {
            base.OnStart();
        }
    }
}