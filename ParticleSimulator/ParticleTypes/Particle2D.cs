using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace ArctisAurora.ParticleTypes
{
    public class Particle2D
    {
        public Vector2 point = new Vector2();
        public Vector2 PredPoint = new Vector2();
        public Vector2 velocity = new Vector2();
        public float radius = 7;
        public Brush color = new Pen(Color.FromArgb(255, 255, 255, 255)).Brush;

        public Particle2D()
        {
            point.X = 0; point.Y = 0;
            velocity.X = 0; velocity.Y= 0;

            PredPoint = point;
        }

        public Particle2D(Vector2 point)
        {
            this.point = point;
            velocity.X = 0; velocity.Y = 0;

            PredPoint = point;
        }

        public Particle2D(float x, float y)
        {
            point.X = x;
            point.Y = y;
            velocity.X = 0; velocity.Y = 0;

            PredPoint = point;
        }

        public Particle2D(float x, float y, float HorizontalVelX, float HorizontalVelY)
        {
            point.X = x;
            point.Y = y;
            velocity.X = HorizontalVelX;
            velocity.Y = HorizontalVelY;

            PredPoint = point;
        }

        public Particle2D(Vector2 p, float HorizontalVelX, float HorizontalVelY)
        {
            point = p;
            velocity.X = HorizontalVelX;
            velocity.Y = HorizontalVelY;

            PredPoint = point;
        }

        public Particle2D(Vector2 p, Vector2 v)
        {
            point = p;
            velocity = v;

            PredPoint = point;
        }
    }
}
