using System;
using System.Collections.Generic;
using System.Linq;
using OpenTK.Mathematics;
using System.Text;
using System.Threading.Tasks;
using Silk.NET.Maths;

namespace ArctisAurora.Forces
{
    public abstract class Force
    {
        public PointF force { get; set; }
        public Vector3 force3 { get; set; }
        internal Vector3D<float> _force { get; set; }

        public Force(PointF force)
        {
            this.force = force;
        }
        public Force(Vector3 force3)
        {
            this.force3 = force3;
        }
        public Force(Vector3D<float> force3)
        {
            this._force = force3;
        }
    }
}