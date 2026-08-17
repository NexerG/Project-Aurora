using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Data;
using ArctisAurora.Core.Threading;
using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.Filing.Serialization;
using ArctisAurora.Core.UISystem;
using ArctisAurora.Core.UISystem.Controls;
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

        // threading — main, physics and render each run free at their own rate and never wait on
        // one another. Each owns its loop, its pacing and its epoch; see ThreadedSystem.
        internal static MainSystem mainSystem;
        internal static PhysicsSystem physicsSystem;
        internal static RenderSystem renderSystem;

        public bool running => mainSystem != null && mainSystem.Running;

        static bool isCaughtUp = true;

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

            Bootstrapper.Load(Paths.BOOTSTRAP);
            Bootstrapper.RunPhase("Bootstrap");

            mainSystem = new MainSystem();
            physicsSystem = new PhysicsSystem();
            renderSystem = new RenderSystem();

            // Pools were parsed during bootstrap and only know their owner by name; bind them now
            // that the systems exist, then wire the lanes between them. Both have to happen before
            // anything starts — a running system drains its inbox on its first tick.
            DataManager.ResolveOwners();
            ThreadedSystem.BuildLanes();

            physicsSystem.Start();
            renderSystem.Start();

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
            GraphicsSettings settings = SettingsRegistry.Get<GraphicsSettings>();
            window = new AGlfwWindow(settings.window.width, settings.window.height);
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

        // Blocks on the calling thread — main cannot be handed a spawned thread, GLFW requires
        // PollEvents on the one that created the window.
        public void Run() => mainSystem.Adopt();

        // One main tick. MainSystem drives this; the body stays here because it touches the
        // registries, input handler and UI state that Engine owns.
        internal void MainTick()
        {
            AGlfwWindow._glfw.PollEvents();
            InputHandler.instance.ActivateKeybinds();
            HandleUI();

            // here should go entity updates &/or interpolation
            Interpolate();

            // Drain queued destroys -> compact -> resequence across every data pool. This still
            // MOVES pool memory, and the render thread is no longer parked while it runs — the
            // address-stable storage rework is what makes this safe.
            DataManager.FrameEdge();
        }

        private void HandleUI()
        {
            if (!uiCollisionHandler.isInWindow) return;

            Vector2D<float> mp = WindowControl.ToDesignSpace(InputHandler.mousePos);
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

            // drain queued destroys before ticking: removed entities won't be ticked, the live
            // lists aren't mutated mid-iteration, and their pool slots free now so the FrameEdge
            // later this tick compacts them.
            EntityRegistry.ProcessDestroys();

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
            mainSystem?.Stop();
            physicsSystem?.Stop();
            renderSystem?.Stop();
        }
    }
}