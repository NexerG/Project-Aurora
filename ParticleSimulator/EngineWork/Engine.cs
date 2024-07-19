using ArctisAurora.CustomEntities;
using ArctisAurora.GameObject;
using ArctisAurora.EngineWork.Rendering.Renderers.Vulkan;
using Silk.NET.Maths;

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
            //MeshImporter importer = new MeshImporter();
            //Scene scene1 = importer.ImportFBX("H:\\Creative\\Blender\\AuroraTestScene.fbx");
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
            _ls.transform.SetWorldPosition(new Vector3D<float>(-1, 20, -1));

            //then we do entities
            //-------------
            TestingEntity _tent12 = new TestingEntity();
            _tent12.transform.SetWorldPosition(new Vector3D<float>(0, -5, 0));
            TestingEntity _tent13 = new TestingEntity();
            _tent13.transform.SetWorldPosition(new Vector3D<float>(1, -5, 0));
            TestingEntity _tent14 = new TestingEntity();
            _tent14.transform.SetWorldPosition(new Vector3D<float>(-1, -5, 0));
            TestingEntity _tent15 = new TestingEntity();
            _tent15.transform.SetWorldPosition(new Vector3D<float>(1, -5, 1));
            TestingEntity _tent16 = new TestingEntity();
            _tent16.transform.SetWorldPosition(new Vector3D<float>(1, -5, -1));
            TestingEntity _tent17 = new TestingEntity();
            _tent17.transform.SetWorldPosition(new Vector3D<float>(-1, -5, 1));
            TestingEntity _tent18 = new TestingEntity();
            _tent18.transform.SetWorldPosition(new Vector3D<float>(-1, -5, -1));
            TestingEntity _tent19 = new TestingEntity();
            _tent19.transform.SetWorldPosition(new Vector3D<float>(0, -5, 1));
            TestingEntity _tent20 = new TestingEntity();
            _tent20.transform.SetWorldPosition(new Vector3D<float>(0, -5, -1));
            TestingEntity _tent21 = new TestingEntity();
            _tent21.transform.SetWorldPosition(new Vector3D<float>(0, -8, 0));
            _tent21.transform.SetWorldScale(new Vector3D<float>(40, 1, 30));


            TestingEntity _tent1 = new TestingEntity();
            _tent1.transform.SetWorldPosition(new Vector3D<float>(0, 5, 0));
            TestingEntity _tent2 = new TestingEntity();
            _tent2.transform.SetWorldPosition(new Vector3D<float>(1, 5, 0));
            TestingEntity _tent3 = new TestingEntity();
            _tent3.transform.SetWorldPosition(new Vector3D<float>(-1, 5, 0));
            TestingEntity _tent4 = new TestingEntity();
            _tent4.transform.SetWorldPosition(new Vector3D<float>(1, 5, 1));
            TestingEntity _tent5 = new TestingEntity();
            _tent5.transform.SetWorldPosition(new Vector3D<float>(1, 5, -1));
            TestingEntity _tent6 = new TestingEntity();
            _tent6.transform.SetWorldPosition(new Vector3D<float>(-1, 5, 1));
            TestingEntity _tent7 = new TestingEntity();
            _tent7.transform.SetWorldPosition(new Vector3D<float>(-1, 5, -1));
            TestingEntity _tent8 = new TestingEntity();
            _tent8.transform.SetWorldPosition(new Vector3D<float>(0, 5, 1));
            TestingEntity _tent9 = new TestingEntity();
            _tent9.transform.SetWorldPosition(new Vector3D<float>(0, 5, -1));
            TestingEntity _tent11 = new TestingEntity();
            _tent11.transform.SetWorldPosition(new Vector3D<float>(0, 8, 0));
            _tent11.transform.SetWorldScale(new Vector3D<float>(40, 1, 30));


            TestingEntity _tent10 = new TestingEntity();
            _tent10.transform.SetWorldPosition(new Vector3D<float>(0, 3, 0));

            //SimulatorEntity _simEntity = new SimulatorEntity();
            //_simEntity.GetComponent<AVulkanMeshComponent>().LoadCustomMesh(kugis);
            //_simEntity.GetComponent<SPHSimComponent>().simSetup(parts);
            //_entities.Add(_simEntity);

            //TestingEntity testEnt = new TestingEntity();
            //testEnt.transform.scale = new Vector3D<float>(1, 1, 1);
            //testEnt.GetComponent<AVulkanMeshComponent>().LoadCustomMesh(scene1);
            //---------------------------------------------------------------------------

            //engine thread
            new Thread(() =>
            {
                EngineStart();
            }).Start();
            //renderer thread for testing
            /*new Thread(() =>
            {
                PathTracerTest();
            }).Start();*/
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
                //Console.SetCursorPosition(0, 0);

                DateTime entityOnTickStart = DateTime.Now;
                foreach (Entity e in _entities)
                {
                    DateTime entityTimeStart = DateTime.Now;
                    e.OnTick();
                    TimeSpan entityTime = DateTime.Now - entityTimeStart;
                    //Console.WriteLine("      " + e.name + "   " + entityTime.TotalMilliseconds);
                }
                TimeSpan entityOnTickTime = DateTime.Now - entityOnTickStart;
                //Console.WriteLine("Entity time ---" + entityOnTickTime.TotalMilliseconds);

                //renderer
                DateTime GraphicsTimeStart = DateTime.Now;
                if (SC.InvokeRequired)
                    SC.Invoke(new Action(() =>
                    {
                        VulkanRenderer._glWindow._glfw.PollEvents();
                        _rasterizer.Draw();
                    }));
                TimeSpan GraphicsTime = DateTime.Now - GraphicsTimeStart;
                //Console.WriteLine("Graphics --- " + GraphicsTime.TotalMilliseconds);

                double totalTime = GraphicsTime.TotalMilliseconds + entityOnTickTime.TotalMilliseconds;
                //Console.WriteLine("TotalTime --- " + totalTime);
                framerate[index % 100] = totalTime;
                index++;
                if (index > 100) index = 1;
                double fr = 0;
                for (int i = 0; i < 100; i++)
                {
                    fr += framerate[i];
                }
                //Console.WriteLine("FPS --- " + 1000 / (fr / 100));

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