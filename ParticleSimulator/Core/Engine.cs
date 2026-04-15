using ArctisAurora.Core.Registry;
using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.Filing.Serialization;
using ArctisAurora.Core.UISystem;
using ArctisAurora.EngineWork.Registry;
using ArctisAurora.EngineWork.Rendering;
using ArctisAurora.EngineWork.Rendering.Modules;
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
        internal static EntityRegistry entityManager;

        // quick access
        internal static List<Entity> entities;
        internal static List<Entity> entitiesOnStart;
        internal static List<Entity> entitiesOnDestroy;

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

        public static TimeSpan deltaTime;
        private static DateTime lastFrameTime = DateTime.Now;
        public static double totalTime = 0;
        //private DateTime lastFrameTime = DateTime.Now;

        public Engine()
        {
            engineInstance = this;
            Console.WriteLine($"Starting main Thread at ID: {GetCurrentThreadId()}");
        }

        public void Init(bool startImmediately)
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
            Bootstrapper.Load(Paths.BOOTSTRAP);
            Bootstrapper.RunPhase("Bootstrap");

            Thread physics = new Thread(PhysicsThread);
            Thread rendering = new Thread(RenderThread);

            physics.Start();
            rendering.Start();

            if(startImmediately)
            {
                Run();
            }
        }

        #region ---- BOOTSTRAPPING ----
        [A_XSDActionDependency("Engine.SystemSetup", "Bootstrap")]
        public static void SetupSystems()
        {
            entityManager = EntityRegistry.manager;
            entities = EntityRegistry.GetGroup("Entities").As<Entity>();
            entitiesOnStart = EntityRegistry.GetGroup("EntitiesOnStart").As<Entity>();
            uiCollisionHandler = new UICollisionHandling();
        }

        [A_XSDActionDependency("Engine.InitWindowing", "Bootstrap")]
        public static void InitWindowing()
        {
            window = new AGlfwWindow(engineInstance.width, engineInstance.height);
            window.CreateWindow();
            window.SetCursorPosCallback(inputHandler.ProcessMouseMove);
            window.SetMouseButtonCallback(inputHandler.ProcessMouseClick);
            window.SetKeyCallback(inputHandler.ProcessKeyboard);
            window.SetCharCallback(inputHandler.ProcessCharInput);
            window.SetMouseOnWindowCallback(UICollisionHandling.instance.IsInWindow);
            window.SetScrollCallback(inputHandler.ProcessScrollWheel);
        }

        [A_XSDActionDependency("Renderer.InitRenderer", "Bootstrap")]
        public static void InitiateRenderer()
        {
            renderer = new Renderer();
        }

        [A_XSDActionDependency("Renderer.PreInitialize", "Bootstrap")]
        public static void SetupModules()
        {
            RenderingModule[] modules = new RenderingModule[]
            {
                new UIModule(),
            };
            renderer.PreInitialize(modules);
        }
        #endregion

        public void Run()
        {
            while (running)
            {
                DateTime tickStart = DateTime.Now;
                AGlfwWindow._glfw.PollEvents();
                InputHandler.instance.ActivateKeybinds();
                HandleUI();
                // skip this if we're no done interpolating last physics tick
                // aka one in the over, another waitting.

                t_physics_start.Set();
                t_physics_end.WaitOne();

                // here should go entity updates &/or interpolation
                Interpolate();

                t_render_end.WaitOne();
                t_render_start.Set();

                deltaTime = DateTime.Now - tickStart;
                totalTime += deltaTime.TotalSeconds;
                //Console.WriteLine($"Engine Tick Time: {deltaTime.TotalSeconds}s");
            }
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
            if (!uiCollisionHandler.isInWindow) return;

            Vector2D<float> mp = InputHandler.mousePos;
            uiCollisionHandler.delta = mp - uiCollisionHandler.lastMousePos;
            uiCollisionHandler.SolveHover(mp);
            uiCollisionHandler.SolveDrag(mp);

            KeyStateEntry lmb = inputHandler.keyTracker.GetState(Keys.MouseLeft);
            KeyStateEntry rmb = inputHandler.keyTracker.GetState(Keys.MouseRight);

            if (lmb != null)
            {
                if (lmb.justPressed)
                    uiCollisionHandler.SolveLMBPress(mp);
                if (lmb.justReleased)
                    uiCollisionHandler.SolveLMBRelease(mp);
            }

            if (rmb != null)
            {
                if (rmb.justPressed)
                    uiCollisionHandler.SolveRMBPress(mp);
                if (rmb.justReleased)
                    uiCollisionHandler.SolveRMBRelease(mp);
            }

            if (InputHandler.scrollDelta.X != 0 || InputHandler.scrollDelta.Y != 0)
                uiCollisionHandler.SolveScroll(InputHandler.scrollDelta);

            uiCollisionHandler.lastMousePos = mp;
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
            if (entitiesOnStart.Count > 0)
            {
                foreach (Entity entity in entitiesOnStart)
                {
                    entity.OnStart();
                }
                entitiesOnStart.Clear();
            }

            /*if (EntityRegistry.onDestroyEntities.Count > 0)
            {
                foreach (Entity entity in EntityRegistry.onDestroyEntities)
                {
                    entity.OnDestroy();
                }
                EntityRegistry.ClearOnDestroy();
            }*/

            foreach (Entity entity in entities)
            {
                entity.OnTick();
            }
            
            UILayout.ResolveLayout();

            /*if(EntityRegistry.entitiesToUpdate.Count > 0)
            {
                List<Entity> entitiesCopy;
                lock (EntityRegistry.entitiesToUpdate)
                {
                    entitiesCopy = new List<Entity>(EntityRegistry.entitiesToUpdate);
                    EntityRegistry.RemoveEntityUpdate(0, EntityRegistry.entitiesToUpdate.Count);
                }
                foreach (Entity e in entitiesCopy)
                {
                    //e.Invalidate();
                }
                renderer.UpdateModules();
            }*/

            // some if clause to check if we caught up
            isCaughtUp = true;
        }

        public void Stop()
        {
            running = false;
        }
    }
}