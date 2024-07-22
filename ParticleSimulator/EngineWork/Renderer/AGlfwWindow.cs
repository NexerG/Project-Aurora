using Silk.NET.Core.Native;
using Silk.NET.GLFW;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;

namespace ArctisAurora.EngineWork.Renderer
{
    internal unsafe class AGlfwWindow
    {
        //GLFW window variables
        internal Glfw _glfw = Glfw.GetApi();
        internal WindowHandle* _windowHandle;
        internal SurfaceKHR _surface;
        internal KhrSurface _driverSurface;
        internal bool _frameBufferResized = false;
        private bool _firstMove = true;
        private double _lastX, _lastY;

        internal void CreateWindow(ref Extent2D _extent)
        {
            if (!_glfw.Init())
                Console.WriteLine("Failed to initialize GLFW");

            _glfw.WindowHint(WindowHintClientApi.ClientApi, ClientApi.NoApi);
            _windowHandle = _glfw.CreateWindow((int)_extent.Width, (int)_extent.Height, "Arctis Auora", null, null);

            if (_windowHandle == null)
            {
                Console.WriteLine("Failed to create window");
                _glfw.Terminate();
            }

            _glfw.SetWindowSizeCallback(_windowHandle, WindwoResizeCallback);
            _glfw.SetCursorPosCallback(_windowHandle, MouseMoveCallback);
            _glfw.SetKeyCallback(_windowHandle, KeyboardCallback);
        }

        internal void CreateSurface()
        {
            if (!VulkanRenderer._vulkan.TryGetInstanceExtension(VulkanRenderer._instance, out _driverSurface))
            {
                throw new NotSupportedException("KHR_surface extension not found.");
            }
            VkNonDispatchableHandle _surfaceHandle;
            _glfw.CreateWindowSurface(VulkanRenderer._instance.ToHandle(), _windowHandle, null, &_surfaceHandle);
            _surface = _surfaceHandle.ToSurface();
        }

        internal void UpdateWindowSize(ref Extent2D _extent)
        {
            int _width, _height;
            _glfw.GetFramebufferSize(_windowHandle, out _width, out _height);
            _extent.Width = (uint)_width;
            _extent.Height = (uint)_height;
        }

        private void WindwoResizeCallback(WindowHandle* window, int width, int height)
        {
            _frameBufferResized = true;
        }

        private void MouseMoveCallback(WindowHandle* window, double xPos, double yPos)
        {
            if (_firstMove)
            {
                _lastX = xPos;
                _lastY = yPos;
                _firstMove = false;
            }

            Vector2D<float> _delta = new Vector2D<float>((float)(xPos - _lastX), (float)(yPos - _lastY));
            _lastX = xPos;
            _lastY = yPos;

            VulkanRenderer._camera.ProcessMouseMovements(_delta);
        }

        private void KeyboardCallback(WindowHandle* window, Silk.NET.GLFW.Keys _key, int _scanCode, InputAction _action, KeyModifiers _mods)
        {
            if (_action == InputAction.Press)
            {
                VulkanRenderer._camera._keyStates[_key] = true;
            }
            if (_action == InputAction.Release)
            {
                VulkanRenderer._camera._keyStates[_key] = false;
            }
        }
    }
}