using ArctisAurora.CustomEntities;
using ArctisAurora.GameObject;
using Silk.NET.Maths;
using Assimp;
using ArctisAurora.EngineWork.ECS.RenderingComponents.Vulkan;
using ArctisAurora.EngineWork.Renderer;
using Silk.NET.Vulkan;

namespace ArctisAurora.EngineWork
{
    public class Engine
    {
        internal Engine _engineInstance = null;
        public bool Running { get; private set; }
        internal Frame SC;
        //internal Rasterization renderer3D;
        internal static VulkanRenderer _renderer;
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

            _ = new VulkanRenderer();
            _renderer.InitRenderer(RendererTypes.Pathtracer);

            ////mesh importer
            MeshImporter importer = new MeshImporter();
            //Scene scene1 = importer.ImportFBX("C:\\Users\\gmgyt\\Desktop\\plane.fbx");

            Running = true;
            SC = s;

            //---------------------------------------------------------------------------
            //Game logic
            //first we setup lights
            //LightSourceEntity _ls = new LightSourceEntity();
            //
            //TestingEntity _e = new TestingEntity();
            //then we do meshes
            //---------------------------------------------------------------------------

            //engine thread
            /*new Thread(() =>
            {
                EngineStart();
            }).Start();*/
            new Thread(() =>
            {
                PathTracerTest();
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