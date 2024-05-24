using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;
using OpenTK.WinForms;
using ParticleSimulator.CustomEntities;
using ParticleSimulator.CustomEntityComponents;
using ParticleSimulator.EngineWork.ComponentBehaviour;
using ParticleSimulator.EngineWork.ECS.RenderingComponents;
using ParticleSimulator.EngineWork.Rendering;
using ParticleSimulator.GameObject;
using ParticleSimulator.ParticleTypes;
using System.Diagnostics;
using System.Reflection;

namespace ParticleSimulator.EngineWork
{
    public class Engine
    {
        public bool Running { get; private set; }
        internal Frame SC;
        internal OpenTK_Renderer renderer3D;
        internal List<Entity> _entities = new List<Entity>();
        internal List<Particle3D> particles3DForEnts = new List<Particle3D>();
        public void Init(Frame s, bool threeDims, int parts)
        {
            //for prerequisites
            GameWindowSettings _gws = GameWindowSettings.Default;
            NativeWindowSettings _nws = new NativeWindowSettings() { ClientSize = new Vector2i(1280, 720), Title = "ProjectAurora" };
            renderer3D = new OpenTK_Renderer(SC, _gws, _nws);
            renderer3D.Prerequisites();

            Running = true;
            SC = s;
            int particleRoot = parts;
            float offsetX = (700 / 2) - (particleRoot * 7 / 2);
            float offsetY = (700 / 2) - (particleRoot * 7 / 2);
            float offsetZ = (700 / 2) - (particleRoot * 7 / 2);
            {

                //3D
                Parallel.For(0, 15, i =>
                {
                    for (int j = 0; j < 15; j++)
                    {
                        for (int k = 0; k < 15; k++)
                        {
                            particles3DForEnts.Add(new Particle3D(i * 7 + offsetX, j * 7 + offsetY, k * 7 + offsetZ));
                        }
                    }
                });

                SimulatorEntity simas = new SimulatorEntity();
                simas.GetComponent<SPHSimComponent>().SetVariables(particles3DForEnts);
                _entities.Add(simas);

                foreach(Entity e in _entities)
                {
                    e.OnStart();
                }

                EngineStart();
                renderer3D.Init();
            }
        }

        public async void EngineStart()
        {
            DateTime initTime = DateTime.Now;
            int TS = 8;
            while (Running)
            {
                //engine time
                TimeSpan SimTime = DateTime.Now - initTime;

                DateTime entityTimeStart = DateTime.Now;
                foreach(Entity e in _entities)
                {
                    e.OnTick();
                }
                TimeSpan entityTime = DateTime.Now - entityTimeStart;
                //Console.WriteLine("Entity time ---" + entityTime.TotalMilliseconds);

                //simulation
                DateTime SimTimeStart = DateTime.Now;
                /*simulator3D.Update(TS / 1000f);
                if(renderer3D!=null)
                {
                    renderer3D.UpdatePositions3D(particles3D);
                }*/
                //TimeSpan SimulationTime = DateTime.Now - SimTimeStart;
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
                //double TSOffset = TS - SimulationTime.TotalMilliseconds;
                /*if (TSOffset > 0f)
                    await Task.Delay(((int)TSOffset));*/
                await Task.Delay(TS);
            }
        }

        internal void InvokeMethod(EntityComponent e, string methodName)
        {
            MethodInfo method = e.GetType().GetMethod(methodName);
            if(method!=null)
            {
                method.Invoke(e,null);
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