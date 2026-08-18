using Silk.NET.Maths;
using System.Drawing;

namespace ArctisAurora.Forces
{
    public class Gravity : Force
    {
        public Gravity(PointF force) : base(force)
        {
        }
        public Gravity(Vector3D<float> force) : base(force)
        {
        }
    }
}
