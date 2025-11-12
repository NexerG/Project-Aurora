using Silk.NET.Core.Native;
using Silk.NET.GLFW;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using static Silk.NET.GLFW.GlfwCallbacks;
using Cursor = Silk.NET.GLFW.Cursor;

namespace ArctisAurora.EngineWork.Rendering
{
    internal unsafe class AGlfwWindow
    {
        //GLFW window variables
        internal static Glfw _glfw;
        internal static WindowHandle* windowHandle;
        internal SurfaceKHR surface;
        internal KhrSurface driverSurface;
        internal bool frameBufferResized = false;
        internal Extent2D windowSize;
        internal static Cursor* cursor;

        internal AGlfwWindow(uint width, uint height)
        {
            _glfw = Glfw.GetApi();
            windowSize = new Extent2D(width, height);
        }

        internal void CreateWindow()
        {
            if (!_glfw.Init())
                throw new Exception("Failed to initialize GLFW");

            _glfw.WindowHint(WindowHintClientApi.ClientApi, ClientApi.NoApi);
            _glfw.WindowHint(WindowHintBool.Resizable, true);
            _glfw.WindowHint(WindowHintBool.Decorated, false);
            _glfw.WindowHint(WindowHintBool.DoubleBuffer, true);
            windowHandle = _glfw.CreateWindow((int)windowSize.Width, (int)windowSize.Height, "Arctis Aurora", null, null);

            if (windowHandle == null)
            {
                _glfw.Terminate();
                throw new Exception("Failed to create window");
            }

            SetResizeCallback(WindwoResizeCallback);
        }

        internal static void ChangeCursor(CursorShape shape)
        {
            cursor = Glfw.GetApi().CreateStandardCursor(shape);
            _glfw.SetCursor(windowHandle, cursor);
        }

        internal void SetResizeCallback(WindowSizeCallback callback)
        {
            _glfw.SetWindowSizeCallback(windowHandle, callback);
        }

        internal void SetCursorPosCallback(CursorPosCallback callback)
        {
            _glfw.SetCursorPosCallback(windowHandle, callback);
        }

        internal void SetKeyCallback(KeyCallback callback)
        {
            _glfw.SetKeyCallback(windowHandle, callback);
        }

        internal void SetMouseButtonCallback(MouseButtonCallback callback)
        {
            _glfw.SetMouseButtonCallback(windowHandle, callback);
        }

        internal void SetMouseOnWindowCallback(CursorEnterCallback callback)
        {
            _glfw.SetCursorEnterCallback(windowHandle, callback);
        }

        internal void CreateSurface()
        {
            if (!Renderer.vk.TryGetInstanceExtension(Renderer.instance, out driverSurface))
            {
                throw new NotSupportedException("KHR_surface extension not found.");
            }
            VkNonDispatchableHandle _surfaceHandle;
            _glfw.CreateWindowSurface(Renderer.instance.ToHandle(), windowHandle, null, &_surfaceHandle);
            surface = _surfaceHandle.ToSurface();
        }

        internal void UpdateWindowSize(ref Extent2D _extent)
        {
            int _width, _height;
            _glfw.GetFramebufferSize(windowHandle, out _width, out _height);
            _extent.Width = (uint)_width;
            _extent.Height = (uint)_height;
        }

        private void WindwoResizeCallback(WindowHandle* window, int width, int height)
        {
            frameBufferResized = true;
        }
    }
}