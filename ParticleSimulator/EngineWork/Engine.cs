using ArctisAurora.CustomEntities;
using ArctisAurora.GameObject;
using Silk.NET.Maths;
using ArctisAurora.EngineWork.Renderer;
using ArctisAurora.EngineWork.Renderer.MeshSubComponents;
using ArctisAurora.EngineWork.ECS.RenderingComponents.Vulkan;

namespace ArctisAurora.EngineWork
{
    public class Engine
    {
        internal static Engine _engineInstance = null;
        public bool Running { get; private set; }
        internal static DateTime initTime;
        internal Frame SC;
        //internal Rasterization renderer3D;
        internal static VulkanRenderer _renderer;
        internal List<Entity> _entities = new List<Entity>();
        internal List<TestingEntity> _bandymas = new List<TestingEntity>();

        public int bbz;

        public Engine()
        {
            _engineInstance = this;
        }

        public void Init(Frame s)
        {
            Running = true;
            SC = s;

            _renderer = new VulkanRenderer();
            _renderer.InitRenderer(ERendererTypes.Pathtracer);

            ////mesh importer
            MeshImporter importer = new MeshImporter();
            //Scene scene1 = importer.ImportFBX("C:\\Users\\gmgyt\\Desktop\\plane.fbx");
            
            Running = true;
            SC = s;

            //---------------------------------------------------------------------------
            //Game logic
            //first we setup lights
            LightSourceEntity _ls = new LightSourceEntity();
            _ls.transform.SetWorldPosition(new Vector3D<float>(1, 10 ,1));
            //
            /*SimulatorEntity _e = new SimulatorEntity();
            _e.GetComponent<SPHSimComponent>().simSetup(15);
            _entities.Add(_e);*/

            //then we do meshes
            //TestingEntity _te = new TestingEntity(new Vector3D<float>(5, 5, 5), new Vector3D<float>(75, -25, 0));
            TestingEntity _te2 = new TestingEntity(new Vector3D<float>(25, 25, 25), new Vector3D<float>(75, 25, 0));
            //_entities.Add(_te);
            _entities.Add(_te2);
            /*TestingEntity _te = new TestingEntity();
            _te.transform.SetWorldScale(new Vector3D<float>(5, 5, 5));
            _te.transform.SetWorldPosition(new Vector3D<float>(75, -25, 0));
            TestingEntity _te2 = new TestingEntity();
            _te2.transform.SetWorldScale(new Vector3D<float>(25, 25, 25));
            _te2.transform.SetWorldPosition(new Vector3D<float>(75, 25, 0));*/
            //---------------------------------------------------------------------------

            //engine thread
            //Task _engineTask = Task.Run(() => EngineStart());
            new Thread(() =>
            {
                EngineStart();
            }).Start();
            /*new Thread(() =>
            {
                PathTracerTest();
            }).Start();*/
        }

        public async Task EngineStart()
        {
            double[] framerate = new double[100];
            for (int i = 0; i < 100; i++)
                framerate[i] = 0;
            int index = 0;

            initTime = DateTime.Now;
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
                        VulkanRenderer._rendererInstance.Draw();
                    }));
                TimeSpan GraphicsTime = DateTime.Now - GraphicsTimeStart;
                //Console.WriteLine("Graphics --- " + GraphicsTime.TotalMilliseconds);

                double totalTime = GraphicsTime.TotalMilliseconds + entityOnTickTime.TotalMilliseconds;
                //Console.WriteLine("TotalTime --- " + totalTime);
                framerate[index % 100] = totalTime;
                index = (index + 1) % 100;
                double fr = framerate.Sum() / index;
                //Console.WriteLine("FPS --- " + 1000 / (fr / 100));

                double TSOffset = TS - totalTime;
                if (TSOffset > 0f)
                    await Task.Delay(((int)TSOffset));
            }
        }

        public async Task PathTracerTest()
        {
            while(Running)
            {
                if (SC.InvokeRequired)
                    SC.Invoke(new Action(() =>
                    {
                        VulkanRenderer._glWindow._glfw.PollEvents();
                        VulkanRenderer._rendererInstance.Draw();
                    }));
                await Task.Delay(4);
            }
        }

        public void Stop()
        {
            Running = false;
        }
    }
}