using Silk.NET.Core.Native;
using Silk.NET.GLFW;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;

namespace ArctisAurora.EngineWork.Rendering.Renderers.Vulkan
{
    internal unsafe class AGlfwWindow
    {
        //GLFW window variables
        internal Glfw _glfw = Glfw.GetApi();
        internal WindowHandle* _windowHandle;
        internal SurfaceKHR _surface;
        internal KhrSurface _driverSurface;
        internal bool _frameBufferResized = false;

        internal void CreateWindow(ref Extent2D _extent)
        {
            if (!_glfw.Init())
                Console.WriteLine("Failed to initialize GLFW");

            _glfw.WindowHint(WindowHintClientApi.ClientApi, ClientApi.NoApi);
            _windowHandle = _glfw.CreateWindow((int)_extent.Width, (int)_extent.Height, "Arctis Auora", null, null);
            _glfw.SetWindowSizeCallback(_windowHandle, WindwoResizeCallback);

            if (_windowHandle == null)
            {
                Console.WriteLine("Failed to create window");
                _glfw.Terminate();
            }
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

        internal uint FindPresentSupportIndex(ref QueueFamilyProperties[] _qfm)
        {
            uint i = 0;
            foreach (var _qf in _qfm)
            {
                if (_qf.QueueFlags.HasFlag(QueueFlags.GraphicsBit))
                {
                    _driverSurface.GetPhysicalDeviceSurfaceSupport(VulkanRenderer._gpu, i, _surface, out var _presentSupport);
                    if (_presentSupport)
                    {
                        return i;
                    }
                }
                i++;
            }
            return int.MaxValue;

        }

        private void WindwoResizeCallback(WindowHandle* window, int width, int height)
        {
            _frameBufferResized = true;
        }
    }
}