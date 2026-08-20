using ArctisAurora.Core.Registry;
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
        internal WindowHandle* handle;
        internal SurfaceKHR surface;
        internal KhrSurface driverSurface;
        internal bool frameBufferResized = false;
        internal Extent2D windowSize;

        // One cursor per shape for the whole process — GLFW creates cursors against the library and
        // only applies them per window, and the hover paths ask for a shape far more often than the
        // shape changes.
        private static readonly Dictionary<CursorShape, IntPtr> cursors = new Dictionary<CursorShape, IntPtr>();

        internal readonly RenderWindow owner;

        internal AGlfwWindow(uint width, uint height, RenderWindow owner)
        {
            _glfw = Glfw.GetApi();
            windowSize = new Extent2D(width, height);
            this.owner = owner;
        }

        internal void CreateWindow()
        {
            if (!_glfw.Init())
                throw new Exception("Failed to initialize GLFW");

            // hints are sticky until reset, and the ghost window sets several this one must not keep
            _glfw.DefaultWindowHints();
            _glfw.WindowHint(WindowHintClientApi.ClientApi, ClientApi.NoApi);
            _glfw.WindowHint(WindowHintBool.Resizable, true);
            _glfw.WindowHint(WindowHintBool.Decorated, false);
            _glfw.WindowHint(WindowHintBool.DoubleBuffer, true);
            handle = CreateForMode(SettingsRegistry.Get<GraphicsSettings>());

            if (handle == null)
            {
                _glfw.Terminate();
                throw new Exception("Failed to create window");
            }

            UpdateWindowSize(ref windowSize);
            SetResizeCallback(WindwoResizeCallback);
        }

        // A window opened after boot: plain windowed at the size it was constructed with, wherever it
        // is asked to go, rather than on the GraphicsSettings monitor in the GraphicsSettings mode.
        internal void CreateWindow(string title, int x, int y)
        {
            _glfw.DefaultWindowHints();
            _glfw.WindowHint(WindowHintClientApi.ClientApi, ClientApi.NoApi);
            _glfw.WindowHint(WindowHintBool.Resizable, true);
            _glfw.WindowHint(WindowHintBool.Decorated, false);
            _glfw.WindowHint(WindowHintBool.DoubleBuffer, true);
            handle = _glfw.CreateWindow((int)windowSize.Width, (int)windowSize.Height, title, null, null);

            if (handle == null)
                throw new Exception("Failed to create window");

            _glfw.SetWindowPos(handle, x, y);
            UpdateWindowSize(ref windowSize);
            SetResizeCallback(WindwoResizeCallback);
            SeedIsInWindow();
        }

        // Floats above everything, never takes focus and starts hidden — it exists only to show what
        // is being dragged. Not resizable, so it needs no resize callback.
        internal void CreateGhostWindow()
        {
            _glfw.DefaultWindowHints();
            _glfw.WindowHint(WindowHintClientApi.ClientApi, ClientApi.NoApi);
            _glfw.WindowHint(WindowHintBool.Resizable, false);
            _glfw.WindowHint(WindowHintBool.Decorated, false);
            _glfw.WindowHint(WindowHintBool.DoubleBuffer, true);
            _glfw.WindowHint(WindowHintBool.Floating, true);
            _glfw.WindowHint(WindowHintBool.FocusOnShow, false);
            _glfw.WindowHint(WindowHintBool.Visible, false);
            handle = _glfw.CreateWindow((int)windowSize.Width, (int)windowSize.Height, "", null, null);

            if (handle == null)
                throw new Exception("Failed to create the drag preview window");

            UpdateWindowSize(ref windowSize);
        }

        // Floats over its parent and takes focus, so a dismissal has something to leave. Starts
        // hidden like the ghost, so the first open is filled and placed before it is ever seen.
        internal void CreateMenuWindow()
        {
            _glfw.DefaultWindowHints();
            _glfw.WindowHint(WindowHintClientApi.ClientApi, ClientApi.NoApi);
            _glfw.WindowHint(WindowHintBool.Resizable, false);
            _glfw.WindowHint(WindowHintBool.Decorated, false);
            _glfw.WindowHint(WindowHintBool.DoubleBuffer, true);
            _glfw.WindowHint(WindowHintBool.Floating, true);
            _glfw.WindowHint(WindowHintBool.Visible, false);
            handle = _glfw.CreateWindow((int)windowSize.Width, (int)windowSize.Height, "", null, null);

            if (handle == null)
                throw new Exception("Failed to create the context menu window");

            UpdateWindowSize(ref windowSize);
        }

        internal void Focus() => _glfw.FocusWindow(handle);

        internal void SetOpacity(float opacity) => _glfw.SetWindowOpacity(handle, opacity);

        internal void Show() => _glfw.ShowWindow(handle);

        internal void Hide() => _glfw.HideWindow(handle);

        internal void DestroyWindow()
        {
            _glfw.DestroyWindow(handle);
            handle = null;
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

        internal void SetPosition(int x, int y)
        {
            _glfw.SetWindowPos(handle, x, y);
        }

        // Publishes the new size itself, for windows with no resize callback to do it.
        internal void Resize(uint width, uint height)
        {
            _glfw.SetWindowSize(handle, (int)width, (int)height);
            UpdateWindowSize(ref windowSize);
            frameBufferResized = true;
        }

        // The cursor-enter callback only fires on a crossing, so a window created under the pointer
        // would never learn it has it.
        internal void SeedIsInWindow()
        {
            owner.isInWindow = _glfw.GetWindowAttrib(handle, WindowAttributeGetter.Hovered);
        }

        internal void ChangeCursor(CursorShape shape)
        {
            if (!cursors.TryGetValue(shape, out IntPtr cursor))
            {
                cursor = (IntPtr)_glfw.CreateStandardCursor(shape);
                cursors[shape] = cursor;
            }
            _glfw.SetCursor(handle, (Cursor*)cursor);
        }

        internal void SetResizeCallback(WindowSizeCallback callback)
        {
            _glfw.SetWindowSizeCallback(handle, callback);
        }

        internal void SetCursorPosCallback(CursorPosCallback callback)
        {
            _glfw.SetCursorPosCallback(handle, callback);
        }

        internal void SetKeyCallback(KeyCallback callback)
        {
            _glfw.SetKeyCallback(handle, callback);
        }

        internal void SetScrollCallback(ScrollCallback callback)
        {
            _glfw.SetScrollCallback(handle, callback);
        }

        internal void SetCharCallback(CharCallback callback)
        {
            _glfw.SetCharCallback(handle, callback);
        }

        internal void SetMouseButtonCallback(MouseButtonCallback callback)
        {
            _glfw.SetMouseButtonCallback(handle, callback);
        }

        internal void SetMouseOnWindowCallback(CursorEnterCallback callback)
        {
            _glfw.SetCursorEnterCallback(handle, callback);
        }

        internal void CreateSurface()
        {
            if (!Renderer.vk.TryGetInstanceExtension(Renderer.instance, out driverSurface))
            {
                throw new NotSupportedException("KHR_surface extension not found.");
            }
            VkNonDispatchableHandle _surfaceHandle;
            _glfw.CreateWindowSurface(Renderer.instance.ToHandle(), handle, null, &_surfaceHandle);
            surface = _surfaceHandle.ToSurface();
        }

        internal void UpdateWindowSize(ref Extent2D _extent)
        {
            int _width, _height;
            _glfw.GetFramebufferSize(handle, out _width, out _height);
            _extent.Width = (uint)_width;
            _extent.Height = (uint)_height;
        }

        private void WindwoResizeCallback(WindowHandle* window, int width, int height)
        {
            frameBufferResized = true;
            // width/height here are in screen coordinates; Vulkan needs framebuffer (pixel)
            // size, so query it directly. On minimize this reports 0x0, which Draw() guards on.
            _glfw.GetFramebufferSize(handle, out int fbWidth, out int fbHeight);
            windowSize = new Extent2D((uint)fbWidth, (uint)fbHeight);

            owner.ui.uiRoot?.FitTo(windowSize);
        }
    }
}