using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ParticleSimulator.Forces
{
    public abstract class Force
    {
        public PointF force { get; set; }

        public Force(PointF force)
        {
            this.force = force;
        }
    }
}