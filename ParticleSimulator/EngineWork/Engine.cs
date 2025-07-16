using ArctisAurora.CustomEntities;
using ArctisAurora.GameObject;
using Silk.NET.Maths;
using ArctisAurora.EngineWork.Renderer;
using ArctisAurora.EngineWork.ECS.RenderingComponents.Vulkan;
using Assimp;
using ArctisAurora.EngineWork.Renderer.Helpers;
using Silk.NET.Vulkan;

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

        //private DateTime lastFrameTime = DateTime.Now;

        public Engine()
        {
            _engineInstance = this;
        }

        public void Init(Frame s)
        {
            Running = true;
            SC = s;

            _renderer = new VulkanRenderer();
            _renderer.InitRenderer(ERendererTypes.UITemp);
            //VulkanRenderer._rendererInstance.Draw();

            ////mesh importer
            MeshImporter importer = new MeshImporter();
            Scene scene1 = importer.ImportFBX("C:\\Users\\gmgyt\\Desktop\\planeAlongX_ActualPlane.fbx");
            
            Running = true;
            SC = s;
            //---------------------------------------------------------------------------
            //PHOTO EDITING ENTITY CREATION

            //---------------------------------------------------------------------------

            //---------------------------------------------------------------------------
            //Game logic
            //first we setup lights
            //LightSourceEntity _ls = new LightSourceEntity();
            //_ls.transform.SetWorldPosition(new Vector3D<float>(1, 10 ,1));
            //
            /*SimulatorEntity _e = new SimulatorEntity();
            _e.GetComponent<SPHSimComponent>().simSetup(15);
            _entities.Add(_e);*/

            //then we do meshes
            TestingEntity _te = new TestingEntity(new Vector3D<float>(200, 200, 200), new Vector3D<float>(2, 0, 0));
            //_te.ChangeColor(new Vector3D<float>(0.5f, 0.5f, 0.5f));
            _te.GetComponent<MeshComponent>().LoadCustomMesh(scene1);
            _entities.Add(_te);
            //TestingEntity _te2 = new TestingEntity(new Vector3D<float>(20, 20, 20), new Vector3D<float>(0, 0, 0));
            //_te2.ChangeColor(new Vector3D<float>(0.05f, 0.5f, 0.247f));
            //_te2.GetComponent<MeshComponent>().LoadCustomMesh(scene1);
            //_entities.Add(_te2);
            //TestingEntity _te3 = new TestingEntity(new Vector3D<float>(20, 20, 20), new Vector3D<float>(0, 10, 10));
            //_te2.ChangeColor(new Vector3D<float>(0f, 0f, 1f));
            //_te3.transform.SetRotationFromVector3(new Vector3D<float>(0.0f,2f,0.0f));
            //_entities.Add(_te3);
            //TestingEntity _te4 = new TestingEntity(new Vector3D<float>(5, 5, 5), new Vector3D<float>(75, 29, 20));
            //_te4.ChangeColor(new Vector3D<float>(1f, 0f, 0f));
            //_te4.transform.SetRotationFromVector3(new Vector3D<float>(0.0f,0.0f,5.0f));
            //_entities.Add(_te4);
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
            //Console.Clear();
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

                //DateTime now = DateTime.Now;
                //pd.deltaTime = (float)(now - lastFrameTime).TotalSeconds;
                //AVulkanBufferHandler.UpdateBuffer(ref pd, ref phosphorusDataBuffer, ref phosphorusDM, BufferUsageFlags.UniformBufferBit);
                //lastFrameTime = now;


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