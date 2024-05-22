using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;
using OpenTK.WinForms;
using ParticleSimulator.EngineWork.Rendering;
using ParticleSimulator.ParticleTypes;
using System.Diagnostics;
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
        internal List<Particle3D> particles3D = new List<Particle3D>();
        System.Windows.Forms.Timer grapicsTimer;

        public void Init(Frame s, bool threeDims, int parts)
        {
            Running = true;
            SC = s;
            //particles2D = new List<Particle2D>();
            //particles3D = new List<Particle3D>();
            int particleRoot = parts;
            float offsetX = (700 / 2) - (particleRoot * 7 / 2);
            float offsetY = (700 / 2) - (particleRoot * 7 / 2);
            float offsetZ = (700 / 2) - (particleRoot * 7 / 2);
            #region Old2D
            //if(!threeDims)
            //{
            //    //2D
            //    Parallel.For(0, particleRoot, i =>
            //    {
            //        for (int j = 0; j < particleRoot; j++)
            //        {
            //            particles2D.Add(new Particle2D(i * 7 + offsetX, j * 7 + offsetY));
            //        }
            //    });
            //    simulator2D = new Simulator(SC, particles2D, new Vector2(700, 700));
            //    renderer3D = new OpenTK_Renderer(SC);
            //    SC.GLControl.Paint += renderer3D.Render;
            //    renderer3D.Init2D(particles2D);
            //}
            //else
            #endregion
            {
                //3D
                Parallel.For(0, 15, i =>
                {
                    for (int j = 0; j < 15; j++)
                    {
                        for (int k = 0; k < 15; k++)
                        {
                            particles3D.Add(new Particle3D(i * 7 + offsetX, j * 7 + offsetY, k * 7 + offsetZ));
                        }
                    }
                });
                simulator3D = new Simulator3D(SC, particles3D, new System.Numerics.Vector3(700, 700, 700));
                GameWindowSettings _gws = GameWindowSettings.Default;
                NativeWindowSettings _nws = new NativeWindowSettings() { Size = new Vector2i(1280,720), Title = "ProjectAurora"};
                Start3D();

                renderer3D = new OpenTK_Renderer(SC, _gws, _nws);
                renderer3D.Init();
                Console.WriteLine("atejom");
                //renderer3D = new OpenTK_Renderer(SC);
                //SC.GLControl.Paint += renderer3D.Render;
                //renderer3D.Init3D(particles3D);
            }

            //grapicsTimer = new System.Windows.Forms.Timer();
            //grapicsTimer.Interval = 1000 / 240;
            //grapicsTimer.Tick += GraphicsTimer_Tick;
            //grapicsTimer.Start();

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
                //engine time
                TimeSpan SimTime = DateTime.Now - initTime;

                //simulation
                DateTime SimTimeStart = DateTime.Now;
                simulator3D.Update(SimTime, TS / 1000f);
                if(renderer3D!=null)
                {
                    renderer3D.UpdatePositions3D(particles3D);
                }
                TimeSpan SimulationTime = DateTime.Now - SimTimeStart;
                //Console.WriteLine("Particles ---" + SimulationTime.TotalMilliseconds);
                //Console.WriteLine("whatever" + (TS-SimulationTime.TotalMilliseconds));

                //renderer
                /*DateTime GraphicsTimeStart = DateTime.Now;
                SC.GLControl.Invalidate();
                TimeSpan GraphicsTime = DateTime.Now - GraphicsTimeStart;
                Console.WriteLine("Graphics --- "+GraphicsTime.TotalMilliseconds);*/

                //double totalTime = GraphicsTime.TotalMilliseconds + SimulationTime.TotalMilliseconds;
                //Console.Clear();
                //Console.WriteLine("TotalTime --- "+ totalTime);
                double TSOffset = TS - SimulationTime.TotalMilliseconds;
                /*if (TSOffset > 0f)
                    await Task.Delay(((int)TSOffset));*/
                await Task.Delay(TS);
            }
        }

        private void GraphicsTimer_Tick(object sender, EventArgs e)
        {
            //renderer3D.camera.inputs(SC.GLControl);

            DateTime GraphicsTimeStart = DateTime.Now;
            //SC.GLControl.Invalidate();
            TimeSpan GraphicsTime = DateTime.Now - GraphicsTimeStart;
            //Console.WriteLine(GraphicsTime.TotalMilliseconds);
        }

        public void Stop()
        {
            Running = false;
        }
        private bool FirstPress = true;
        /*public void MouseHandler(MouseEventArgs e, int UD)
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
        }*/
        public void KeyboardHandler(KeyPressEventArgs e)
        {
            renderer3D.camera.moveCamera(e);
            //Console.WriteLine("pisam klava");
        }
    }
}