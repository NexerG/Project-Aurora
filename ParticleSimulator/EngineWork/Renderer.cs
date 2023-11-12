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
            foreach (Particle particle in p)
            {
                g.FillEllipse(particle.color, particle.point.X, particle.point.Y, particle.radius, particle.radius);
            }
            PicBox.Invalidate();
        }
    }
}
