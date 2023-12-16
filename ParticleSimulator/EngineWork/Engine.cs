using ParticleSimulator.EngineWork.Rendering;
using ParticleSimulator.ParticleTypes;
using System.Numerics;

namespace ParticleSimulator.EngineWork
{
    public class Engine
    {
        public bool Running { get; private set; }
        internal Frame SC;
        internal Simulator simulator2D;
        internal Simulator3D simulator3D;
        //internal Renderer renderer;
        internal OpenTK_Renderer renderer3D;
        internal List<Particle2D> particles2D;
        internal List<Particle3D> particles3D;
        System.Windows.Forms.Timer grapicsTimer;

        public void Init(Frame s, bool threeDims)
        {
            Running = true;
            SC = s;
            particles2D = new List<Particle2D>();
            particles3D = new List<Particle3D>();
            int particleRoot = 70;
            float offsetX = (700 / 2) - (particleRoot * 7 / 2);
            float offsetY = (700 / 2) - (particleRoot * 7 / 2);
            float offsetZ = (700 / 2) - (particleRoot * 7 / 2);

            if(!threeDims)
            {
                //2D
                Parallel.For(0, particleRoot, i =>
                {
                    for (int j = 0; j < particleRoot; j++)
                    {
                        particles2D.Add(new Particle2D(i * 7 + offsetX, j * 7 + offsetY));
                    }
                });
                simulator2D = new Simulator(SC, particles2D, new Vector2(700, 700));
                renderer3D = new OpenTK_Renderer(SC);
                SC.GLControl.Paint += renderer3D.Render;
                renderer3D.Init2D(particles2D);
            }
            else
            {
                //3D
                Parallel.For(0, particleRoot, i =>
                {
                    for (int j = 0; j < particleRoot; j++)
                    {
                        for (int k = 0; k < particleRoot; k++)
                        {
                            particles3D.Add(new Particle3D(i * 7 + offsetX, j * 7 + offsetY, k * 7 + offsetZ));
                        }
                    }
                });
                simulator3D = new Simulator3D(SC, particles3D, new Vector3(700, 700,700));
                renderer3D = new OpenTK_Renderer(SC);
                SC.GLControl.Paint += renderer3D.Render;
                renderer3D.Init3D(particles3D);
            }

            grapicsTimer = new System.Windows.Forms.Timer();
            grapicsTimer.Interval = 1000 / 240;
            grapicsTimer.Tick += GraphicsTimer_Tick;
            grapicsTimer.Start();

            if (!threeDims)
                Start2D();
            else
                Start3D();
        }

        public async void Start2D()
        {
            if (simulator2D == null)
            {
                throw new ArgumentException("Sim missing");
            }
            DateTime initTime = DateTime.Now;
            int TS = 8;
            while (Running)
            {
                TimeSpan SimTime = DateTime.Now - initTime;
                simulator2D.Update(SimTime, TS / 1000f);
                renderer3D.UpdatePositions2D(particles2D);
                await Task.Delay(TS);
            }
        }
        public async void Start3D()
        {
            if (simulator3D == null)
            {
                throw new ArgumentException("Sim missing");
            }
            DateTime initTime = DateTime.Now;
            int TS = 8;
            while (Running)
            {
                TimeSpan SimTime = DateTime.Now - initTime;
                simulator3D.Update(SimTime, TS / 1000f);
                renderer3D.UpdatePositions3D(particles3D);
                await Task.Delay(TS);
            }
        }

        private void GraphicsTimer_Tick(object sender, EventArgs e)
        {
            //renderer.Draw(particles2D);
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