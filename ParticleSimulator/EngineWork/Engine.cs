using ArctisAurora.CustomEntities;
using ArctisAurora.EngineWork.Rendering;
using Assimp;
using ArctisAurora.EngineWork.Serialization;
using ArctisAurora.EngineWork.AssetRegistry;
using ArctisAurora.EngineWork.EngineEntity;
using ArctisAurora.EngineWork.Rendering.Modules;
using Windows.Devices.HumanInterfaceDevice;
using ArctisAurora.EngineWork.Physics.UICollision;

namespace ArctisAurora.EngineWork
{
    public class Engine
    {
        public static bool isDebug
        {
            get
            {
#if DEBUG
                return true;
#else
                return false;
#endif
            }
        }

        internal static Engine engineInstance = null;
        internal static Renderer renderer;
        internal static UICollisionHandling collisionHandling2D;
        //internal static JobSystem jobSystem;
        internal static AssetRegistries assetRegistry = new AssetRegistries();

        public bool running { get; private set; }
        static AutoResetEvent t_physics_start = new AutoResetEvent(false);
        static AutoResetEvent t_physics_end = new AutoResetEvent(false);
        
        static AutoResetEvent t_ECS_start = new AutoResetEvent(false);
        static AutoResetEvent t_ECS_end = new AutoResetEvent(false);

        static AutoResetEvent t_render_start = new AutoResetEvent(false);
        static AutoResetEvent t_render_end = new AutoResetEvent(true);

        static bool isCaughtUp = true;
        static bool isPhysicsReady = false;

        internal static DateTime initTime;
        internal Frame SC;


        //private DateTime lastFrameTime = DateTime.Now;

        public Engine()
        {
            engineInstance = this;
        }

        public void Init(Frame s)
        {
            running = true;
            SC = s;

            EntityManager.manager = new EntityManager();
            collisionHandling2D = new UICollisionHandling();

            renderer = new Renderer();
            RenderingModule[] modules = new RenderingModule[]
            {
                new UIModule(),
            };
            renderer.PreInitialize(modules);
            renderer.Initialize();
            // asset and renderable objects can be loaded from here

            Bootstrapper.PreprareDefaultAssets();
            renderer.SetupObjects();

            renderer.PrepareDescriptors();
            renderer.SetupPipelines();
            renderer.CreateCommandBuffers();
            renderer.CreateSyncObjects();


            TextEntity _te = new TextEntity("A", 70);

            Thread physics = new Thread(PhysicsThread);
            Thread rendering = new Thread(RenderThread);

            physics.Start();
            rendering.Start();

            while (running)
            {
                Renderer.window._glfw.PollEvents();

                // skip this if we're no done interpolating last physics tick
                // aka one in the over, another waitting.

                t_physics_start.Set();
                t_physics_end.WaitOne();

                // here should go entity updates &/or interpolation
                Interpolate();

                t_render_end.WaitOne();
                t_render_start.Set();
            }

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
            //TestingEntity _te = new TestingEntity(new Vector3D<float>(1, 70, 70), new Vector3D<float>(2, 0, 0));
            //_te.ChangeColor(new Vector3D<float>(0.5f, 0.5f, 0.5f));
            //_te.GetComponent<MeshComponent>().LoadCustomMesh(scene1);
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
        }

        private void PhysicsThread()
        {
            while (running)
            {
                t_physics_start.WaitOne();
                //Console.WriteLine("running physics thread");
                if (!isPhysicsReady) // we're lacking ?
                {
                    //Console.WriteLine("Doing phyics");
                    isPhysicsReady = true;
                }
                Thread.Sleep(32);
                t_physics_end.Set();
            }
        }

        private void RenderThread()
        {
            while (running)
            {
                t_render_start.WaitOne();
                renderer.Draw();
                t_render_end.Set();
            }
        }

        private void Interpolate()
        {
            // have we caught up?
            if (isCaughtUp)
            {
                isCaughtUp = false;
                // switch active buffers
                isPhysicsReady = false;
            }

            // do interpolation

            if (EntityManager.onStartEntities.Count > 0)
            {
                foreach (Entity entity in EntityManager.onStartEntities)
                {
                    entity.OnStart();
                }
                EntityManager.ClearOnStart();
            }

            if (EntityManager.onDestroyEntities.Count > 0)
            {
                foreach (Entity entity in EntityManager.onDestroyEntities)
                {
                    entity.OnDestroy();
                }
                EntityManager.ClearOnDestroy();
            }

            foreach (Entity entity in EntityManager.entities)
            {
                entity.OnTick();
            }

            // paralelization of this is a good idea, just the systems that are to be paralelized must be paralel safe as the current ones arent
            //if (EntityManager.onStartEntities.Count > 0)
            //{
            //    Parallel.ForEach(EntityManager.onStartEntities, e =>
            //    {
            //        e.OnStart();
            //    });
            //    EntityManager.ClearOnStart();
            //}

            //if (EntityManager.onDestroyEntities.Count > 0)
            //{
            //    Parallel.ForEach(EntityManager.onDestroyEntities, e =>
            //    {
            //        e.OnDestroy();
            //    });
            //    EntityManager.ClearOnDestroy();
            //}

            //Parallel.ForEach(EntityManager.entities, e =>
            //{
            //    e.OnTick();
            //});

            // some if clause to check if we caught up
            isCaughtUp = true;
        }

        public void Stop()
        {
            running = false;
        }
    }
}