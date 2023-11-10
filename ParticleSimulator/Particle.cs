using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ParticleSimulator
{
    public class Particle
    {
        public Point point = new Point();
        public float[] velocity = new float[2];
        public float radius = 10f;
        public Brush color = new Pen(Color.FromArgb(255,255,255,255)).Brush;

        public Particle()
        {
            point.X = 0; point.Y = 0;
            velocity[0] = 0;
            velocity[1] = 0;
        }

        public Particle(Point point)
        {
            this.point = point;
            velocity[0] = 0;
            velocity[1] = 0;
        }

        public Particle(int x, int y)
        {
            this.point.X = x;
            this.point.Y = y;
            velocity[0] = 0;
            velocity[1] = 0;
        }

        public Particle(int x, int y, float HorizontalVel, float VerticalVel)
        {
            this.point.X = x;
            this.point.Y = y;
            velocity[0] = HorizontalVel;
            velocity[1] = VerticalVel;
        }

        public Particle(Point p, float HorizontalVel, float VerticalVel)
        {
            point = p;
            velocity[0] = HorizontalVel;
            velocity[1] = VerticalVel;
        }
    }
}
