using ArctisAurora.Core.Registry;
using ArctisAurora.Core.UISystem.Controls;
using Silk.NET.Core.Native;
using Silk.NET.GLFW;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using static Silk.NET.GLFW.GlfwCallbacks;
using Cursor = Silk.NET.GLFW.Cursor;
using Monitor = Silk.NET.GLFW.Monitor;

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
            windowHandle = CreateForMode(SettingsRegistry.Get<GraphicsSettings>());

            if (windowHandle == null)
            {
                _glfw.Terminate();
                throw new Exception("Failed to create window");
            }

            UpdateWindowSize(ref windowSize);
            SetResizeCallback(WindwoResizeCallback);
        }

        // Borderless is a plain window at the monitor's size and position — the window is created
        // undecorated in every mode, so it needs no hint of its own.
        private WindowHandle* CreateForMode(GraphicsSettings settings)
        {
            if (settings.window.mode == WindowSetting.WindowMode.Windowed)
            {
                WindowHandle* windowed = _glfw.CreateWindow((int)windowSize.Width, (int)windowSize.Height, "Arctis Aurora", null, null);
                if (!string.IsNullOrWhiteSpace(settings.monitor.name))
                    CenterOn(windowed, PickMonitor(settings.monitor.name));
                return windowed;
            }

            Monitor* monitor = PickMonitor(settings.monitor.name);
            VideoMode* video = _glfw.GetVideoMode(monitor);

            if (settings.window.mode == WindowSetting.WindowMode.Fullscreen)
                return _glfw.CreateWindow(video->Width, video->Height, "Arctis Aurora", monitor, null);

            _glfw.GetMonitorPos(monitor, out int x, out int y);
            WindowHandle* handle = _glfw.CreateWindow(video->Width, video->Height, "Arctis Aurora", null, null);
            _glfw.SetWindowPos(handle, x, y);
            return handle;
        }

        private void CenterOn(WindowHandle* handle, Monitor* monitor)
        {
            VideoMode* video = _glfw.GetVideoMode(monitor);
            _glfw.GetMonitorPos(monitor, out int x, out int y);
            _glfw.SetWindowPos(handle,
                x + (video->Width - (int)windowSize.Width) / 2,
                y + (video->Height - (int)windowSize.Height) / 2);
        }

        private Monitor* PickMonitor(string preferred)
        {
            Monitor* primary = _glfw.GetPrimaryMonitor();
            if (string.IsNullOrWhiteSpace(preferred)) return primary;

            Monitor** monitors = _glfw.GetMonitors(out int count);
            for (int i = 0; i < count; i++)
            {
                string name = MonitorName(monitors[i]);
                if (name == null || !name.Contains(preferred, StringComparison.OrdinalIgnoreCase)) continue;
                return monitors[i];
            }

            Console.WriteLine($"[Renderer] no monitor matching '{preferred}' — using {MonitorName(primary)}.");
            return primary;
        }

        // GLFW's own name is the driver description, which is the same string for every panel on a
        // machine, so the name comes from Windows and is joined to the monitor by its position.
        private string MonitorName(Monitor* monitor)
        {
            _glfw.GetMonitorPos(monitor, out int x, out int y);
            return DisplayNames.At(x, y) ?? _glfw.GetMonitorName(monitor);
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

        internal void SetScrollCallback(ScrollCallback callback)
        {
            _glfw.SetScrollCallback(windowHandle, callback);
        }

        internal void SetCharCallback(CharCallback callback)
        {
            _glfw.SetCharCallback(windowHandle, callback);
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
            // width/height here are in screen coordinates; Vulkan needs framebuffer (pixel)
            // size, so query it directly. On minimize this reports 0x0, which Draw() guards on.
            _glfw.GetFramebufferSize(windowHandle, out int fbWidth, out int fbHeight);
            windowSize = new Extent2D((uint)fbWidth, (uint)fbHeight);

            (EntityRegistry.uiTree as WindowControl)?.FitTo(windowSize);
        }
    }
}