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
        internal static RendererBaseClass _testas;
        internal static VulkanRenderer _rasterizer;
        internal static Pathtracing _pathTracer;
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

            _testas = new RendererBaseClass();
            _testas.InitRenderer(true);
            //_rasterizer = new VulkanRenderer();
            //_pathTracer =  new Pathtracing();

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
                        RendererBaseClass._glWindow._glfw.PollEvents();
                        RendererBaseClass._rendererInstance.Draw();
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
                /*VulkanRenderer._lightsToRender[0].transform.SetWorldPosition(new Vector3D<float>(
                VulkanRenderer._lightsToRender[0].transform.position.X + 0.003f,
                VulkanRenderer._lightsToRender[0].transform.position.Y,
                VulkanRenderer._lightsToRender[0].transform.position.Z));
                _bandymas[0].transform.SetWorldPosition(new Vector3D<float>(
                    _bandymas[0].transform.position.X + 0.003f,
                    _bandymas[0].transform.position.Y,
                    _bandymas[0].transform.position.Z ));*/

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