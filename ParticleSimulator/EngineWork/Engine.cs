using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;
using OpenTK.WinForms;
using ArctisAurora.CustomEntities;
using ArctisAurora.CustomEntityComponents;
using ArctisAurora.EngineWork.ComponentBehaviour;
using ArctisAurora.EngineWork.ECS.RenderingComponents;
using ArctisAurora.EngineWork.Rendering;
using ArctisAurora.GameObject;
using ArctisAurora.ParticleTypes;
using System.Diagnostics;
using System.Reflection;
using OpenTK.Windowing.Common;

namespace ArctisAurora.EngineWork
{
    public class Engine
    {
        internal static Engine _engineInstance = null;
        public bool Running { get; private set; }
        internal Frame SC;
        internal OpenTK_Renderer renderer3D;
        internal List<Entity> _entities = new List<Entity>();

        public Engine()
        {
            _engineInstance = this;
        }

        public void Init(Frame s, bool threeDims, int parts)
        {
            //Renderer prerequisites refueling
            GameWindowSettings _gws = GameWindowSettings.Default;
            NativeWindowSettings _nws = new NativeWindowSettings() { ClientSize = new Vector2i(1280, 720), Title = "ProjectAurora" };
            renderer3D = new OpenTK_Renderer(_gws, _nws);
            renderer3D.Prerequisites();

            Running = true;
            SC = s;

            //---------------------------------------------------------------------------
            //Game logic
            //first we setup lights
            LightSourceEntity lightEntity = new LightSourceEntity();
            LightSourceEntity lightEntity2 = new LightSourceEntity();
            lightEntity2.transform.position = new Vector3(700, 700, 700);
            _entities.Add(lightEntity2);
            _entities.Add(lightEntity);

            //then we do entities
            SimulatorEntity _simEntity = new SimulatorEntity();
            _simEntity.GetComponent<SPHSimComponent>().simSetup(parts);
            _entities.Add(_simEntity);
            //---------------------------------------------------------------------------


            new Thread(() =>
            {
                EngineStart();
            }).Start();
            renderer3D.Init();
        }

        public async void EngineStart()
        {
            double[] framerate = new double[10];
            for(int i=0;i<10;i++)
                framerate[i] = 0;
            int index = 0;

            DateTime initTime = DateTime.Now;
            int TS = 8;
            while (Running)
            {
                //engine time
                //TimeSpan SimTime = DateTime.Now - initTime;
                Console.SetCursorPosition(0, 0);

                DateTime entityOnTickStart = DateTime.Now;
                foreach (Entity e in _entities)
                {
                    DateTime entityTimeStart = DateTime.Now;
                    e.OnTick();
                    TimeSpan entityTime = DateTime.Now - entityTimeStart;
                    Console.WriteLine("      " + e.name + "   "+entityTime.TotalMilliseconds);
                }
                TimeSpan entityOnTickTime = DateTime.Now - entityOnTickStart;
                Console.WriteLine("Entity time ---" + entityOnTickTime.TotalMilliseconds);

                //renderer
                DateTime GraphicsTimeStart = DateTime.Now;
                if (SC.InvokeRequired)
                    SC.Invoke(new Action(() =>
                    {
                        OpenTK_Renderer._rendererInstance.Render(this, null);
                    }));
                TimeSpan GraphicsTime = DateTime.Now - GraphicsTimeStart;
                Console.WriteLine("Graphics --- " + GraphicsTime.TotalMilliseconds);

                double totalTime = GraphicsTime.TotalMilliseconds + entityOnTickTime.TotalMilliseconds;
                Console.WriteLine("TotalTime --- " + totalTime);
                framerate[index % 10] = totalTime;
                index++;
                if (index > 100) index = 1;
                double fr=0;
                for(int i=0;i<10;i++)
                {
                    fr += framerate[i];
                }
                Console.WriteLine("FPS --- " + 1000/(fr/10));

                double TSOffset = TS - totalTime;
                if (TSOffset > 0f)
                    await Task.Delay(((int)TSOffset));
            }
        }

        public void Stop()
        {
            Running = false;
        }
    }
}