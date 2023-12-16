using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace ParticleSimulator.ParticleTypes
{
    public class Particle3D
    {
        public Vector3 point = new Vector3();
        public Vector3 PredPoint = new Vector3();
        public Vector3 velocity = new Vector3();
        public float radius = 7;
        public Brush color = new Pen(Color.FromArgb(255, 255, 255, 255)).Brush;

        public Particle3D()
        {
            point.X = 0; point.Y = 0; point.Z = 0;
            velocity.X = 0; velocity.Y = 0; velocity.Z= 0;

            PredPoint = point;
        }

        public Particle3D(Vector3 point)
        {
            this.point = point;
            velocity.X = 0; velocity.Y = 0; velocity.Z = 0;

            PredPoint = point;
        }

        public Particle3D(float x, float y, float z)
        {
            point.X = x; point.Y = y; point.Z = z;
            velocity.X = 0; velocity.Y = 0; velocity.Z = 0;

            PredPoint = point;
        }

        public Particle3D(float x, float y, float z, float HorizontalVelX, float HorizontalVelY, float VerticalVel)
        {
            point.X = x; point.Y = y; point.Z = z;
            velocity.X = HorizontalVelX;
            velocity.Y = HorizontalVelY;
            velocity.Z = VerticalVel;

            PredPoint = point;
        }

        public Particle3D(Vector3 p, float HorizontalVelX, float HorizontalVelY, float VerticalVel)
        {
            point = p;
            velocity.X = HorizontalVelX;
            velocity.Y = HorizontalVelY;
            velocity.Z = VerticalVel;

            PredPoint = point;
        }

        public Particle3D(Vector3 p, Vector3 v)
        {
            point = p;
            velocity = v;

            PredPoint = point;
        }
    }
}