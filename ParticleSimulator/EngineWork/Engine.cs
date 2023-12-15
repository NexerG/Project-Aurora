using ParticleSimulator.EngineWork.Rendering;
using ParticleSimulator.ParticleTypes;

namespace ParticleSimulator.EngineWork
{
    public class Engine
    {
        public bool Running { get; private set; }
        internal Frame SC;
        internal Simulator simulator;
        internal Renderer renderer;
        internal OpenTK_Renderer renderer3D;
        internal List<Particle> particles;
        System.Windows.Forms.Timer grapicsTimer;

        public void Init(Frame s)
        {
            Running = true;
            SC = s;
            particles = new List<Particle>();
            int particleRoot = 50;
            float offsetX = (SC.PicBox.Width / 2) - (particleRoot * 7 / 2);
            float offsetY = (SC.PicBox.Height / 2) - (particleRoot * 7 / 2);
            for (int i=0; i < particleRoot; i++)
            {
                for(int j=0; j < particleRoot; j++)
                {
                    particles.Add(new Particle(i * 7+offsetX,j*7+offsetY));
                }
            }
            //simulator = new Simulator(particles,SC);
            //renderer = new Renderer(SC.PicBox);
            renderer3D = new OpenTK_Renderer(SC);
            SC.GLControl.Paint += renderer3D.Render;

            grapicsTimer = new System.Windows.Forms.Timer();
            grapicsTimer.Interval = 1000 / 240;
            grapicsTimer.Tick += GraphicsTimer_Tick;
            grapicsTimer.Start();

            //Start();
        }

        public async void Start()
        {
            if (simulator == null)
            {
                throw new ArgumentException("Sim missing");
            }
            DateTime initTime = DateTime.Now;
            int TS = 8;
            while (Running)
            {
                TimeSpan SimTime = DateTime.Now - initTime;
                simulator.Update(SimTime, TS/1000f);
                await Task.Delay(TS);
            } 
        }

        private void GraphicsTimer_Tick(object sender, EventArgs e)
        {
            //renderer.Draw(particles);
            //SC.Invalidate();
            //renderer3D.RotatePyramid(new OpenTK.Mathematics.Vector3(0, 1f, 0), 0.5f);
            //renderer3D.camera.inputs(SC.GLControl);
            SC.GLControl.Invalidate();
        }

        public void Stop()
        {
            Running = false;
        }
        private bool FirstPress = true;
        public void MouseHandler(MouseEventArgs e, int UD)
        {
            switch (e.Button)
            {
                case MouseButtons.Left:
                    if(UD==1)
                    {
                        Cursor.Hide();
                        Cursor.Position = new Point(SC.DesktopLocation.X + SC.GLControl.Width / 2,
                            SC.DesktopLocation.Y + SC.GLControl.Height / 2);
                    }
                    else if (UD==0)
                    {
                        Cursor.Show();
                        Cursor.Position = new Point(SC.DesktopLocation.X + SC.GLControl.Width / 2,
                            SC.DesktopLocation.Y + SC.GLControl.Height / 2);
                        FirstPress = true;
                    }
                    else
                    {
                        //do cam movements
                        if (FirstPress)
                        {
                            FirstPress = false;
                        }
                        else
                        {
                            renderer3D.camera.newpos.X = Cursor.Position.X;
                            renderer3D.camera.newpos.Y = Cursor.Position.Y;

                            renderer3D.camera.rotateCamera(SC.DesktopLocation.X + SC.MainPanel.Width / 2,
                                SC.DesktopLocation.Y + SC.MainPanel.Height / 2);

                            Cursor.Position = new Point(SC.DesktopLocation.X + SC.GLControl.Width / 2,
                                SC.DesktopLocation.Y + SC.GLControl.Height / 2);
                        }
                    }
                    break;
                default:
                    break;
            }
        }
        public void KeyboardHandler(KeyPressEventArgs e)
        {
            renderer3D.camera.moveCamera(e);
            //Console.WriteLine("pisam klava");
        }
    }
}