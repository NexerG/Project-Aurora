using Silk.NET.Maths;

namespace ArctisAurora.ParticleTypes
{
    public class Particle3DVulkan
    {
        public Vector3D<float> point = new Vector3D<float>();
        public Vector3D<float> PredPoint = new Vector3D<float>();
        public Vector3D<float> velocity = new Vector3D<float>();
        public float radius = 7;
        public Brush color = new Pen(Color.FromArgb(255, 255, 255, 255)).Brush;

        public Particle3DVulkan()
        {
            point.X = 0; point.Y = 0; point.Z = 0;
            velocity.X = 0; velocity.Y = 0; velocity.Z= 0;

            PredPoint = point;
        }

        public Particle3DVulkan(Vector3D<float> point)
        {
            this.point = point;
            velocity.X = 0; velocity.Y = 0; velocity.Z = 0;

            PredPoint = point;
        }

        public Particle3DVulkan(float x, float y, float z)
        {
            point.X = x; point.Y = y; point.Z = z;
            velocity.X = 0; velocity.Y = 0; velocity.Z = 0;

            PredPoint = point;
        }

        public Particle3DVulkan(float x, float y, float z, float HorizontalVelX, float HorizontalVelY, float VerticalVel)
        {
            point.X = x; point.Y = y; point.Z = z;
            velocity.X = HorizontalVelX;
            velocity.Y = HorizontalVelY;
            velocity.Z = VerticalVel;

            PredPoint = point;
        }

        public Particle3DVulkan(Vector3D<float> p, float HorizontalVelX, float HorizontalVelY, float VerticalVel)
        {
            point = p;
            velocity.X = HorizontalVelX;
            velocity.Y = HorizontalVelY;
            velocity.Z = VerticalVel;

            PredPoint = point;
        }

        public Particle3DVulkan(Vector3D<float> p, Vector3D<float> v)
        {
            point = p;
            velocity = v;

            PredPoint = point;
        }
    }
}