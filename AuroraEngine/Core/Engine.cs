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
using Silk.NET.GLFW;
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

        // Every open OS window, by name. Copy-on-write: a mutation builds a whole new dictionary and
        // publishes it, so the render thread reads the reference once and walks a map nobody will
        // touch again. The window the application boots into is "main" and is never removed —
        // closing it closes the application.
        private static Dictionary<string, RenderWindow> _windows = new Dictionary<string, RenderWindow>();
        public static Dictionary<string, RenderWindow> windows => Volatile.Read(ref _windows);
        public static RenderWindow primary;

        public const string mainWindow = "main";

        internal static Renderer renderer;
        internal static InputHandler inputHandler;
        internal static UICollisionHandling uiCollisionHandler;
        //internal static JobSystem jobSystem;
        internal static AssetRegistries assetRegistry = new AssetRegistries();
        internal static EntityRegistry entityManager;

        // quick access
        internal static List<Entity> entities;
        internal static List<Entity> entitiesOnDestroy;

        // threading — main, physics and render each run free at their own rate and never wait on
        // one another. Each owns its loop, its pacing and its epoch; see ThreadedSystem.
        internal static MainSystem mainSystem;
        internal static PhysicsSystem physicsSystem;
        internal static RenderSystem renderSystem;

        public bool running => mainSystem != null && mainSystem.Running;

        static bool isCaughtUp = true;

        internal static DateTime initTime;

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
            uiCollisionHandler = new UICollisionHandling();
        }

        [A_XSDActionDependency("Engine.InitWindowing", "Bootstrap")]
        public static void InitWindowing()
        {
            GraphicsSettings settings = SettingsRegistry.Get<GraphicsSettings>();
            RenderWindow window = new RenderWindow(settings.window.width, settings.window.height);
            primary = window;
            Publish(mainWindow, window);

            window.os.CreateWindow();
            WireInput(window);
            window.os.SeedIsInWindow();
            window.gpuReady = true;   // the bootstrap steps build the primary's resources in stages
        }

        // A window opened after bootstrap. Only the OS window is made here — the render thread owns
        // Vulkan, so it builds the GPU side at the top of its next tick and Draw skips until then.
        public static RenderWindow OpenWindow(string name, uint width, uint height, int x, int y)
        {
            RenderWindow window = new RenderWindow(width, height);
            window.os.CreateWindow(name, x, y);
            WireInput(window);

            Publish(name, window);
            return window;
        }

        // A menu is clicked, so unlike the preview it wires input and is an ordinary window to every
        // walk that skips ghosts.
        public static RenderWindow OpenMenuWindow(string name, uint width, uint height)
        {
            RenderWindow window = new RenderWindow(width, height);
            window.os.CreateMenuWindow();
            WireInput(window);

            Publish(name, window);
            return window;
        }

        // No input callbacks — a preview window is looked at, never clicked.
        public static RenderWindow OpenGhostWindow(string name, uint width, uint height)
        {
            RenderWindow window = new RenderWindow(width, height) { isGhost = true };
            window.os.CreateGhostWindow();

            Publish(name, window);
            return window;
        }

        // Marks a window for teardown. The render thread frees its Vulkan objects, then MainTick
        // destroys the OS window and drops it from the map.
        public static void CloseWindow(RenderWindow window)
        {
            if (window == primary)
            {
                engineInstance?.Stop();
                return;
            }

            window.ui.uiRoot?.Destroy();
            window.ui.uiRoot = null;
            window.closeRequested = true;
        }

        private static void Publish(string name, RenderWindow window)
        {
            Dictionary<string, RenderWindow> next = new Dictionary<string, RenderWindow>(_windows);
            next[name] = window;
            Volatile.Write(ref _windows, next);
        }

        private static void Unpublish(string name)
        {
            Dictionary<string, RenderWindow> next = new Dictionary<string, RenderWindow>(_windows);
            next.Remove(name);
            Volatile.Write(ref _windows, next);
        }

        // Destroys the OS window of anything the render thread has finished freeing.
        private static void ReapClosedWindows()
        {
            Dictionary<string, RenderWindow> snapshot = windows;
            foreach (KeyValuePair<string, RenderWindow> entry in snapshot)
            {
                if (!entry.Value.closeRequested || !entry.Value.gpuDestroyed) continue;

                Unpublish(entry.Key);
                entry.Value.os.DestroyWindow();
            }
        }

        // The window a GLFW callback fired for.
        public static RenderWindow WindowFor(WindowHandle* handle)
        {
            foreach (RenderWindow window in windows.Values)
                if (window.os.handle == handle)
                    return window;
            return null;
        }

        private static void WireInput(RenderWindow window)
        {
            window.os.SetCursorPosCallback(inputHandler.ProcessMouseMove);
            window.os.SetMouseButtonCallback(inputHandler.ProcessMouseClick);
            window.os.SetKeyCallback(inputHandler.ProcessKeyboard);
            window.os.SetCharCallback(inputHandler.ProcessCharInput);
            window.os.SetMouseOnWindowCallback(window.MouseCrossedBorder);
            window.os.SetScrollCallback(inputHandler.ProcessScrollWheel);
        }

        [A_XSDActionDependency("Renderer.InitRenderer", "Bootstrap")]
        public static void InitiateRenderer()
        {
            renderer = new Renderer();
        }

        [A_XSDActionDependency("Renderer.PreInitialize", "Bootstrap")]
        public static void SetupModules()
        {
            renderer.PreInitialize();
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
            ReapClosedWindows();
            InputHandler.instance.ActivateKeybinds();

            foreach (RenderWindow window in windows.Values)
                HandleUI(window);
            DragGhost.Follow();
            ContextMenuWindow.Tick();

            // here should go entity updates &/or interpolation
            Interpolate();

            // Drain queued destroys -> compact -> resequence across every data pool. This still
            // MOVES pool memory, and the render thread is no longer parked while it runs — the
            // address-stable storage rework is what makes this safe.
            DataManager.FrameEdge();

            // Dense indices have settled, so each window module can be told the range it draws.
            UILayout.RefreshWindowRanges();
        }

        private void HandleUI(RenderWindow window)
        {
            if (window.closeRequested) return;

            // A pressed button captures the pointer to the window it went down in, which keeps
            // reporting positions far outside itself and stops every other window hearing anything.
            // So the drag's own window drives the whole gesture, wherever the pointer has gone.
            bool ownsDrag = UICollisionHandling.dragging != null
                && ReferenceEquals(RenderWindow.Of(UICollisionHandling.dragging), window);
            if (!window.isInWindow && !ownsDrag) return;

            Vector2D<float> mp = window.ui.ToDesignSpace(window.mousePos);
            uiCollisionHandler.delta = mp - uiCollisionHandler.lastMousePos;
            if (window.isInWindow)
                uiCollisionHandler.SolveHover(mp, window.ui.uiRoot);

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

            // After the release, not before it: the button is already up in the key tracker by the
            // time this tick runs, so a drag solved first would take its own stale-release path and
            // the release would never see a live drag.
            uiCollisionHandler.SolveDrag(mp);

            if (window.scrollDelta.X != 0 || window.scrollDelta.Y != 0)
                uiCollisionHandler.SolveScroll(window.scrollDelta);

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

            // The tick's one lifecycle phase, in order: start, tear down, then notify enable
            // changes, so a callback fired here can create or destroy entities of its own.
            EntityRegistry.ProcessStarts();
            EntityRegistry.ProcessDestroys();
            EntityRegistry.ProcessEnableChanges();

            /*if (EntityRegistry.onDestroyEntities.Count > 0)
            {
                foreach (Entity entity in EntityRegistry.onDestroyEntities)
                {
                    entity.OnDestroy();
                }
                EntityRegistry.ClearOnDestroy();
            }*/

            // Count is captured up front so an entity created in OnTick waits for the next tick,
            // and the guard covers one destroyed later in this same loop.
            for (int i = 0, count = entities.Count; i < count; i++)
            {
                Entity entity = entities[i];
                if (!entity.tickable) continue;

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