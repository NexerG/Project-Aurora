using ArctisAurora.CustomEntities;
using ArctisAurora.GameObject;
using Silk.NET.Maths;
using Assimp;
using ArctisAurora.EngineWork.ECS.RenderingComponents.Vulkan;
using ArctisAurora.EngineWork.Renderer;
using ArctisAurora.CustomEntityComponents;

namespace ArctisAurora.EngineWork
{
    public class Engine
    {
        internal Engine _engineInstance = null;
        public bool Running { get; private set; }
        internal Frame SC;
        //internal Rasterization renderer3D;
        internal static VulkanRenderer _rasterizer;
        internal List<Entity> _entities = new List<Entity>();
        internal List<TestingEntity> _bandymas = new List<TestingEntity>();

        public Engine()
        {
            _engineInstance = this;
        }

        public void Init(Frame s, bool threeDims, int parts)
        {
            Running = true;
            SC = s;

            _rasterizer = new VulkanRenderer();

            ////mesh importer
            MeshImporter importer = new MeshImporter();
            Scene scene1 = importer.ImportFBX("C:\\Users\\gmgyt\\Desktop\\plane.fbx");
            //Scene kugis = importer.ImportFBX("H:\\Creative\\Blender\\AuroraTestScene_kugis.fbx");

            Running = true;
            SC = s;

            //---------------------------------------------------------------------------
            //Game logic
            //first we setup lights
            //LightSourceEntity lightEntity = new LightSourceEntity();
            //lightEntity.transform.position = new Vector3D<float>(-0, 15, -0);
            //LightSourceEntity lightEntity2 = new LightSourceEntity();
            //lightEntity2.transform.position = new Vector3(700, 700, 700);
            //_entities.Add(lightEntity);
            //_entities.Add(lightEntity);
            LightSourceEntity _ls = new LightSourceEntity();
            _ls.transform.SetWorldPosition(new Vector3D<float>(-1, 40, -1));

            //then we do entities
            //-------------+

            SimulatorEntity _simEntity = new SimulatorEntity();
            //_simEntity.GetComponent<Mesh>().LoadCustomMesh(kugis);
            _simEntity.GetComponent<SPHSimComponent>().simSetup(parts);
            _entities.Add(_simEntity);

            //TestingEntity testEnt = new TestingEntity();
            //testEnt.transform.scale = new Vector3D<float>(1, 1, 1);
            //testEnt.GetComponent<AVulkanMeshComponent>().LoadCustomMesh(scene1);
            //---------------------------------------------------------------------------

            //engine thread
            new Thread(() =>
            {
                EngineStart();
            }).Start();
        }

        public async void EngineStart()
        {
            double[] framerate = new double[100];
            for (int i = 0; i < 100; i++)
                framerate[i] = 0;
            int index = 0;

            DateTime initTime = DateTime.Now;
            int TS = 8;
            while (Running)
            {
                //engine time
                TimeSpan SimTime = DateTime.Now - initTime;
                Console.SetCursorPosition(0, 0);

                DateTime entityOnTickStart = DateTime.Now;
                foreach (Entity e in _entities)
                {
                    DateTime entityTimeStart = DateTime.Now;
                    e.OnTick();
                    TimeSpan entityTime = DateTime.Now - entityTimeStart;
                    Console.WriteLine("      " + e.name + "   " + entityTime.TotalMilliseconds);
                }
                TimeSpan entityOnTickTime = DateTime.Now - entityOnTickStart;
                Console.WriteLine("Entity time ---" + entityOnTickTime.TotalMilliseconds);

                //renderer
                double GraphicsTime = 999;
                if (SC.InvokeRequired)
                    SC.Invoke(new Action(() =>
                    {
                        VulkanRenderer._glWindow._glfw.PollEvents();
                        _rasterizer.Draw();
                        GraphicsTime = _rasterizer._timesFloat[1] - _rasterizer._timesFloat[0];
                    }));
                Console.WriteLine("Graphics time --- " + GraphicsTime);

                TimeSpan CPUTime = DateTime.Now - entityOnTickStart;
                Console.WriteLine("CPU time --- " + CPUTime.TotalMilliseconds);

                double totalTime = GraphicsTime + CPUTime.TotalMilliseconds;
                Console.WriteLine("TotalTime --- " + totalTime);
                framerate[index % 100] = totalTime;
                index++;
                if (index > 100) index = 1;
                double fr = 0;
                for (int i = 0; i < 100; i++)
                {
                    fr += framerate[i];
                }
                Console.WriteLine("FPS --- " + 1000 / (fr / 100));
                double TSOffset = TS - totalTime;
                if (TSOffset > 0f)
                    await Task.Delay(((int)TSOffset));
            }
        }

        public async void PathTracerTest()
        {
            while(Running)
            {
                if (SC.InvokeRequired)
                    SC.Invoke(new Action(() =>
                    {
                        VulkanRenderer._glWindow._glfw.PollEvents();
                        _rasterizer.Draw();
                    }));
                await Task.Delay(8);
            }
        }

        public void Stop()
        {
            Running = false;
        }
    }
}