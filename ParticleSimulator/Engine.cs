using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ParticleSimulator
{
    public class Engine
    {
        public bool Running { get; private set; }
        private Frame SC;
        private Simulator simulator;
        private Renderer renderer;
        private List<Particle> particles;
        System.Windows.Forms.Timer grapicsTimer;

        public void Init(Frame s)
        {
            Running = true;
            SC = s;
            particles= new List<Particle>();
            particles.Add(new Particle(10,10));
            simulator = new Simulator();
            renderer = new Renderer(ref SC.PicBox);

            grapicsTimer=new System.Windows.Forms.Timer();
            grapicsTimer.Interval = 1000 / 120;
            grapicsTimer.Tick += GraphicsTimer_Tick;
            grapicsTimer.Start();

            Start();
        }

        public async void Start()
        {
            if(simulator==null)
            {
                throw new ArgumentException("Sim missing");
            }
            DateTime initTime = DateTime.Now;
            while(Running)
            {
                TimeSpan SimTime = DateTime.Now - initTime;
                simulator.Update(SimTime,particles);
                await Task.Delay(64);
            }
        }

        private void GraphicsTimer_Tick(object sender, EventArgs e)
        {
            Console.WriteLine("Draw");
            renderer.Draw(particles);
            //SC.Invalidate();
        }

        public void Stop()
        {
            Running = false;
        }
    }
}