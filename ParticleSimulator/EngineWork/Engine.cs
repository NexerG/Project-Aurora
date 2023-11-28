using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ParticleSimulator.ParticleTypes;

namespace ParticleSimulator.EngineWork
{
    public class Engine
    {
        public bool Running { get; private set; }
        internal Frame SC;
        internal Simulator simulator;
        internal Renderer renderer;
        internal List<Particle> particles;
        System.Windows.Forms.Timer grapicsTimer;

        public void Init(Frame s)
        {
            Running = true;
            SC = s;
            particles = new List<Particle>();
            Random rnd = new Random();
            for(int i=0; i < 40; i++)
            {
                for(int j=0; j < 40; j++)
                {
                    particles.Add(new Particle(i * 7+100,j*7+50));
                }
            }
            simulator = new Simulator(particles,SC);
            renderer = new Renderer(SC.PicBox);

            grapicsTimer = new System.Windows.Forms.Timer();
            grapicsTimer.Interval = 1000 / 240;
            grapicsTimer.Tick += GraphicsTimer_Tick;
            grapicsTimer.Start();

            Start();
        }

        public async void Start()
        {
            if (simulator == null)
            {
                throw new ArgumentException("Sim missing");
            }
            DateTime initTime = DateTime.Now;
            int TS = 4;
            while (Running)
            {
                TimeSpan SimTime = DateTime.Now - initTime;
                simulator.Update(SimTime, TS/1000f);
                await Task.Delay(TS);
            }
        }

        private void GraphicsTimer_Tick(object sender, EventArgs e)
        {
            renderer.Draw(particles);
            //SC.Invalidate();
        }

        public void Stop()
        {
            Running = false;
        }
    }
}