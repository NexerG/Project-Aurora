using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ParticleSimulator.ParticleTypes;

namespace ParticleSimulator.EngineWork
{
    public class Renderer
    {
        Bitmap bmp;
        public Graphics g;
        PictureBox PicBox;

        public Renderer(PictureBox PB)
        {
            PicBox = PB;
            //picture stuff
            bmp = new Bitmap(PicBox.Width, PicBox.Height);
            g = Graphics.FromImage(bmp);
        }

        public void Draw(List<Particle> p)
        {
            if (bmp.Width != PicBox.Width || bmp.Height != PicBox.Height)
            {
                bmp = new Bitmap(PicBox.Width, PicBox.Height);
                g = Graphics.FromImage(bmp);
            }
            //clear the canvas before drawing
            g.Clear(Color.FromArgb(255, 30, 30, 30));
            PicBox.Image = bmp;

            //draw
            float MaxSpeed = p.Max(r => r.velocity.LengthSquared()) + 0.1f;
            float MinSpeed = p.Min(r => r.velocity.LengthSquared()) - 0.1f;
            foreach (Particle particle in p)
            {
                g.FillEllipse(color(particle.velocity.LengthSquared(),MinSpeed,MaxSpeed), particle.point.X, particle.point.Y, particle.radius, particle.radius);
            }
            PicBox.Invalidate();
        }

        public Brush color(float velocity, float MinSpeed, float MaxSpeed)
        {
            Color minColor = Color.Blue;
            Color MaxColor = Color.Red;
            double remaped = (velocity - MinSpeed) / (MaxSpeed - MinSpeed);
            int r = (int)(minColor.R + remaped * (MaxColor.R - minColor.R));
            int g = (int)(minColor.G + remaped * (MaxColor.G - minColor.G));
            int b = (int)(minColor.B + remaped * (MaxColor.B - minColor.B));

            return new Pen(Color.FromArgb(255, r, g, b)).Brush;
        }
    }
}
