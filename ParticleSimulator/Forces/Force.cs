using Silk.NET.Maths;

namespace ArctisAurora.Forces
{
    public abstract class Force
    {
        public PointF force { get; set; }
        internal Vector3D<float> _force { get; set; }

        public Force(PointF force)
        {
            this.force = force;
        }
        public Force(Vector3D<float> force3)
        {
            this._force = force3;
        }
    }
}