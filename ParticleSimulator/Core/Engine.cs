using ArctisAurora.EngineWork.AssetRegistry;
using ArctisAurora.EngineWork.EngineEntity;
using ArctisAurora.EngineWork.Physics.UICollision;
using ArctisAurora.EngineWork.Rendering;
using ArctisAurora.EngineWork.Rendering.Modules;
using ArctisAurora.EngineWork.Rendering.UI;
using ArctisAurora.EngineWork.Rendering.UI.Controls;
using Silk.NET.Maths;
using System.Runtime.InteropServices;

namespace ArctisAurora.EngineWork
{
    public unsafe class Engine
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

        [DllImport("kernel32.dll")]
        static extern int GetCurrentThreadId();

        // pre vars
        uint width = 1280;
        uint height = 720;
        public static int doubleClickTime = 250;

        internal static Engine engineInstance = null;
        internal static AGlfwWindow window;
        internal static Renderer renderer;
        internal static InputHandler inputHandler;
        internal static UICollisionHandling uiCollisionHandler;
        //internal static JobSystem jobSystem;
        internal static AssetRegistries assetRegistry = new AssetRegistries();
        internal static EntityManager entityManager;

        // threading
        public bool running { get; private set; }
        static AutoResetEvent t_physics_start = new AutoResetEvent(false);
        static AutoResetEvent t_physics_end = new AutoResetEvent(false);

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
            Console.WriteLine($"Starting main Thread at ID: {GetCurrentThreadId()}");
        }

        public void Init(RenderingModule[] modules, bool startImmediately)
        {
            //Image<Rgba32> im = new Image<Rgba32>(16, 16);
            //for (int i = 0; i < 16; i++)
            //{
            //    for (int j = 0; j < 16; j++)
            //    {
            //        im[i, j] = new Rgba32(255, 255, 255, 255);
            //    }
            //}
            //im.Save(Paths.UIMASKS + "\\defaultMask.png");

            running = true;
            Bootstrapper.PrepareRegistries();
            Bootstrapper.RegisterTypes();

            entityManager = new EntityManager();
            uiCollisionHandler = new UICollisionHandling();

            window = new AGlfwWindow(width, height);
            window.CreateWindow();

            renderer = new Renderer();
            renderer.PreInitialize(modules);
            renderer.Initialize();

            // assets and renderable objects can be loaded from here alongside inputs

            inputHandler = new InputHandler();
            window.SetCursorPosCallback(inputHandler.ProcessMouseMove);
            window.SetMouseButtonCallback(inputHandler.ProcessMouseClick);
            window.SetKeyCallback(inputHandler.ProcessKeyboard);
            window.SetMouseOnWindowCallback(UICollisionHandling.instance.IsInWindow);

            // optionals
            Bootstrapper.PreprareDefaultAssets();

            renderer.SetupObjects();
            renderer.PrepareDescriptors();
            renderer.SetupPipelines();
            
            // optional but has to go after objects / renderer
            //renderer.CreateCommandBuffers();

            renderer.CreateSyncObjects();

            Thread physics = new Thread(PhysicsThread);
            Thread rendering = new Thread(RenderThread);

            physics.Start();
            rendering.Start();

            if(startImmediately)
            {
                Run();
            }
        }

        public void Run()
        {
            while (running)
            {
                AGlfwWindow._glfw.PollEvents();
                HandleUI();
                // skip this if we're no done interpolating last physics tick
                // aka one in the over, another waitting.

                t_physics_start.Set();
                t_physics_end.WaitOne();

                // here should go entity updates &/or interpolation
                Interpolate();

                t_render_end.WaitOne();
                t_render_start.Set();
            }
        }

        private void TestEnter()
        {
            Console.WriteLine("entered");
        }
        private void TestExit()
        {
            Console.WriteLine("exited");
        }
        private void TestClick()
        {
            Console.WriteLine("clicked");
        }
        private void TestRelease()
        {
            Console.WriteLine("released");
        }
        private void TestAltClick()
        {
            Console.WriteLine("alt clicked");
        }
        private void TestAltRelease()
        {
            Console.WriteLine("alt released");
        }
        private void TestDoubleClick()
        {
            Console.WriteLine("double clicked");
        }

        private void PhysicsThread()
        {
            Console.WriteLine($"Starting physics thread at ID: {GetCurrentThreadId()}");
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
            Console.WriteLine($"Starting rendering thread at ID: {GetCurrentThreadId()}");
            while (running)
            {
                t_render_start.WaitOne();
                renderer.Draw();
                t_render_end.Set();
            }
        }

        private void HandleUI()
        {
            if (uiCollisionHandler.isInWindow)
            {
                Vector2D<float> mp = InputHandler.mousePos;
                uiCollisionHandler.delta = mp - uiCollisionHandler.lastMousePos;
                uiCollisionHandler.SolveHover(mp);
                uiCollisionHandler.SolveLMB(mp);
                uiCollisionHandler.SolveRMB(mp);
                uiCollisionHandler.SolveDrag(mp);
                uiCollisionHandler.lastMousePos = mp;
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
            

            if(EntityManager.entitiesToUpdate.Count > 0)
            {
                List<Entity> entitiesCopy;
                lock (EntityManager.entitiesToUpdate)
                {
                    entitiesCopy = new List<Entity>(EntityManager.entitiesToUpdate);
                    EntityManager.RemoveEntityUpdate(0, EntityManager.entitiesToUpdate.Count);
                }
                foreach (Entity e in entitiesCopy)
                {
                    //e.Invalidate();
                }
                renderer.UpdateModules();
            }

            // some if clause to check if we caught up
            isCaughtUp = true;
        }

        public void Stop()
        {
            running = false;
        }
    }
}