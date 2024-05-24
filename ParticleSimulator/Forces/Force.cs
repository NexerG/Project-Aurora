using System;
using System.Collections.Generic;
using System.Linq;
using OpenTK.Mathematics;
using System.Text;
using System.Threading.Tasks;

namespace ParticleSimulator.Forces
{
    public abstract class Force
    {
        public PointF force { get; set; }
        public Vector3 force3 { get; set; }

        public Force(PointF force)
        {
            this.force = force;
        }
        public Force(Vector3 force3)
        {
            this.force3 = force3;
        }
    }
}