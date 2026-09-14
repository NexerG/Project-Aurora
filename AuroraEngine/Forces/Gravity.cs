using System.Numerics;
using System.Drawing;

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
