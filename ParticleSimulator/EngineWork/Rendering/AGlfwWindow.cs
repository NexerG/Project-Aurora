using Silk.NET.Core.Native;
using Silk.NET.GLFW;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;

namespace ArctisAurora.EngineWork.Rendering
{
    internal unsafe class AGlfwWindow
    {
        //GLFW window variables
        internal Glfw _glfw = Glfw.GetApi();
        internal WindowHandle* windowHandle;
        internal SurfaceKHR surface;
        internal KhrSurface driverSurface;
        internal bool frameBufferResized = false;

        internal void CreateWindow(ref Extent2D _extent)
        {
            if (!_glfw.Init())
                Console.WriteLine("Failed to initialize GLFW");

            _glfw.WindowHint(WindowHintClientApi.ClientApi, ClientApi.NoApi);
            windowHandle = _glfw.CreateWindow((int)_extent.Width, (int)_extent.Height, "Arctis Auora", null, null);

            if (windowHandle == null)
            {
                Console.WriteLine("Failed to create window");
                _glfw.Terminate();
            }

            _glfw.SetWindowSizeCallback(windowHandle, WindwoResizeCallback);

            _glfw.SetCursorPosCallback(windowHandle, MouseMoveCallback);
            _glfw.SetKeyCallback(windowHandle, KeyboardCallback);
            _glfw.SetMouseButtonCallback(windowHandle, MouseClickCallback);
        }


        internal void CreateSurface(ref Vk vk, ref Instance instance)
        {
            if (!vk.TryGetInstanceExtension(instance, out driverSurface))
            {
                throw new NotSupportedException("KHR_surface extension not found.");
            }
            VkNonDispatchableHandle _surfaceHandle;
            _glfw.CreateWindowSurface(instance.ToHandle(), windowHandle, null, &_surfaceHandle);
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

        private void MouseMoveCallback(WindowHandle* window, double xPos, double yPos)
        {
            VulkanRenderer._rendererInstance.MouseUpdate(xPos, yPos);
        }

        private void MouseClickCallback(WindowHandle* window, MouseButton button, InputAction action, KeyModifiers mods)
        {
            VulkanRenderer._rendererInstance.MouseClick(button, action);
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