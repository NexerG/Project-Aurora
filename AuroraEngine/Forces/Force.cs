using System.Numerics;
using System.Drawing;

namespace ArctisAurora.Forces
{
    public abstract class Force
    {
        public PointF force { get; set; }
        internal Vector3 _force { get; set; }

        public Force(PointF force)
        {
            this.force = force;
        }
        public Force(Vector3 force3)
        {
            this._force = force3;
        }
    }
}