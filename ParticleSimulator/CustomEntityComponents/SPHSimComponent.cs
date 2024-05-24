using OpenTK.Mathematics;
using ParticleSimulator.EngineWork;
using ParticleSimulator.EngineWork.ComponentBehaviour;
using ParticleSimulator.ParticleTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using ParticleSimulator.EngineWork.ECS.RenderingComponents;
using System.Diagnostics;

namespace ParticleSimulator.CustomEntityComponents
{
    internal class SPHSimComponent : EntityComponent
    {
        internal List<Particle3D> _particles;
        internal Simulator3D _simulator;
        internal List<Matrix4> _instanceMats = new List<Matrix4>();

        public override void OnTick()
        {
            _simulator.Update(8f / 1000f);
            UpdatePositions3D(_particles);
        }

        internal void SetVariables(List<Particle3D> parts)
        {
            _particles = parts;
            _simulator = new Simulator3D(_particles, new Vector3(700, 700, 700));
            simSetup(_particles);
            parent.GetComponent<MeshComponent>().MakeInstanced(_instanceMats.Count,ref _instanceMats);
        }

        public void simSetup(List<Particle3D> ps)
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

                _instanceMats.Add(tr_s);
            }
        }
        public void UpdatePositions3D(List<Particle3D> ps)
        {
            for (int i = 0; i < ps.Count; i++)
            {
                Vector3 posTrans = new Vector3(-ps[i].point.X, -ps[i].point.Y, ps[i].point.Z);
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

                _instanceMats[i] = tr_s;
            }
        }
    }
}