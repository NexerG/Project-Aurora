using System;
using System.Collections.Generic;
using System.Linq;
using OpenTK.Mathematics;
using System.Text;
using System.Threading.Tasks;

namespace ArctisAurora.Forces
{
    public class Gravity : Force
    {
        public Gravity(PointF force) : base(force)
        {
        }
        public Gravity(Vector3 force) : base(force)
        {
        }
    }
}
