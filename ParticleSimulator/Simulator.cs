using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ParticleSimulator
{
    public class Simulator
    {
        public void Update(TimeSpan engineTime, List<Particle> parts)
        {
            double TimeElapsed = engineTime.TotalMilliseconds / 1000;
            foreach (Particle p in parts)
            {
                p.point.Y = p.point.Y + 10;
            }
        }
    }
}