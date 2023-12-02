using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace ParticleSimulator.ParticleTypes
{
    public class Particle
    {
        public Vector2 point = new Vector2();
        public Vector2 PredPoint = new Vector2();
        public Vector2 velocity = new Vector2();
        public float radius = 7;
        public Brush color = new Pen(Color.FromArgb(255, 255, 255, 255)).Brush;

        public Particle()
        {
            point.X = 0; point.Y = 0;
            velocity.X = 0; velocity.Y= 0;

            PredPoint = point;
        }

        public Particle(Vector2 point)
        {
            this.point = point;
            velocity.X = 0; velocity.Y = 0;

            PredPoint = point;
        }

        public Particle(float x, float y)
        {
            point.X = x;
            point.Y = y;
            velocity.X = 0; velocity.Y = 0;

            PredPoint = point;
        }

        public Particle(float x, float y, float HorizontalVel, float VerticalVel)
        {
            point.X = x;
            point.Y = y;
            velocity.X = HorizontalVel;
            velocity.Y = VerticalVel;

            PredPoint = point;
        }

        public Particle(Vector2 p, float HorizontalVel, float VerticalVel)
        {
            point = p;
            velocity.X = HorizontalVel;
            velocity.Y = VerticalVel;

            PredPoint = point;
        }

        public Particle(Vector2 p, Vector2 v)
        {
            point = p;
            velocity = v;

            PredPoint = point;
        }
    }
}
