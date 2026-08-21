using ArctisAurora.Core.Rendering.Modules;
using ArctisAurora.Core.UISystem.Controls;
using ArctisAurora.EngineWork.Rendering.Modules;
using Silk.NET.Core;
using Silk.NET.GLFW;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using Image = Silk.NET.Vulkan.Image;
using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace ArctisAurora.EngineWork.Rendering
{
    // One OS window and everything sized to it: its swapchain, its frame sync, its rendering modules
    // and its compositor. Renderer keeps only what the device owns; the UI tree lives on the module.
    public unsafe class RenderWindow
    {
        // os window
        internal AGlfwWindow os;

        // swapchain
        internal SwapchainKHR swapchain;
        internal KhrSwapchain swapchainKHR = null!;
        internal Extent2D swapchainExtent;
        internal SurfaceFormatKHR surfaceFormat;
        internal Image[] swapchainImages = null!;
        internal ImageView[] swapchainImageViews = null!;
        internal uint imageCount;

        // frame sync
        internal Semaphore[] imageAvailableSemaphores = null!;
        internal Semaphore[] renderFinishedSemaphores = null!;
        internal Semaphore[] modulesFinishedSemaphores = null!;
        internal Semaphore timelineSemaphore;
        internal ulong frameCounter;
        internal int currentFrame;

        // rendering
        internal RenderingModule[] modules;
        internal CompositorModule compositor = null!;

        // the module holding this window's UI tree — the window itself owns no controls
        public UIModule ui;

        // A preview of a control being dragged: it holds no tree of its own, draws a second view of
        // a control that lives in another window, and is skipped by everything that walks trees.
        public bool isGhost;

        // lifecycle handshake — main creates and destroys the OS window, the render thread owns every
        // Vulkan object, so each side flags the other rather than reaching across
        internal volatile bool gpuReady;
        public volatile bool closeRequested;
        internal volatile bool gpuDestroyed;

        // input — the pointer is one device, but its position is reported per window, and a drag that
        // leaves a window keeps reporting to the window that owns it
        public bool isInWindow;
        public Vector2D<float> mousePos;
        public Vector2D<float> scrollDelta;
        internal Vector2D<float> scrollDeltaWrite;

        public RenderWindow(uint width, uint height)
        {
            os = new AGlfwWindow(width, height, this);
            ui = new UIModule();
            modules = new RenderingModule[]
            {
                ui,
            };

            // A module knows its window from birth. BindWindow only runs when the render thread
            // builds the GPU side, and a tree can be assigned to a window before that happens.
            for (int i = 0; i < modules.Length; i++)
                modules[i].window = this;
        }

        // Brings the window forward. Restored first when iconified — focusing a minimized window
        // leaves it minimized, so a host raising one has no way to see it.
        public void Focus()
        {
            if (AGlfwWindow._glfw.GetWindowAttrib(os.handle, WindowAttributeGetter.Iconified))
                AGlfwWindow._glfw.RestoreWindow(os.handle);

            os.Focus();
        }

        // The window a control is drawn into — its root is some window's uiRoot. Null for a subtree
        // detached from every window.
        public static RenderWindow Of(VulkanControl control)
        {
            if (control == null) return null;

            while (control.parent is VulkanControl parent)
                control = parent;

            foreach (RenderWindow window in Engine.windows.Values)
                if (ReferenceEquals(window.ui.uiRoot, control))
                    return window;
            return null;
        }

        // Everything this window needs from the device, in one call. The primary window cannot use it
        // — its GPU setup is interleaved with asset loading across five Bootstrap.xml steps — but a
        // secondary window is created after all of that already exists.
        internal void CreateGpuResources()
        {
            os.CreateSurface();

            uint presentFamily = (uint)Renderer.queueAllocator.presentFamilyIndex;
            os.driverSurface.GetPhysicalDeviceSurfaceSupport(Renderer.gpu, presentFamily, os.surface, out Bool32 supported);
            if (!supported)
                throw new Exception($"Queue family {presentFamily} cannot present to this window's surface");

            Renderer.renderer.CreateSwapchain(this);

            for (int i = 0; i < modules.Length; i++)
            {
                modules[i].BindWindow(this);
                modules[i].CreateDescriptorSetLayout();
                modules[i].PrepareObjects();
                modules[i].CreateOutputImages();
                modules[i].CreatePipeline();
            }

            compositor = new CompositorModule();
            compositor.BindWindow(this);
            compositor.Init(modules, swapchainImageViews);

            Renderer.CreateSyncObjects(this);
        }

        // The inverse. Render thread only, and only after DeviceWaitIdle — everything here is either
        // in flight or referenced by something in flight.
        internal void DestroyGpuResources()
        {
            compositor.DestroyGpuResources();
            for (int i = 0; i < modules.Length; i++)
                modules[i].DestroyGpuResources();

            Renderer.DestroySyncObjects(this);

            for (int i = 0; i < swapchainImageViews.Length; i++)
                Renderer.vk.DestroyImageView(Renderer.logicalDevice, swapchainImageViews[i], null);
            swapchainKHR.DestroySwapchain(Renderer.logicalDevice, swapchain, null);

            os.driverSurface.DestroySurface(Renderer.instance, os.surface, null);
        }

        internal void MouseCrossedBorder(WindowHandle* handle, bool entered) => isInWindow = entered;
    }
}
