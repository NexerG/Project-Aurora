using Silk.NET.Core.Contexts;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using Image = Silk.NET.Vulkan.Image;

namespace ArctisAurora.EngineWork.Rendering.Renderers.Vulkan
{
    struct SwapChainSupportDetails
    {
        public SurfaceCapabilitiesKHR Capabilities;
        public SurfaceFormatKHR[] Formats;
        public PresentModeKHR[] PresentModes;
    }

    internal unsafe class AVulkanSwapchain
    {
        internal Vk _vulkan;

        //swapchain variables
        internal SwapchainKHR _swapchainKHR;        //the virtualized swapchain
        internal KhrSwapchain _driverSwapchain;     //the driver swapchain
        internal Image[] _swapchainImages;          //swapchain images for rendering
        internal SurfaceFormatKHR _surfaceFormat;   //window format

        //external references
        internal KhrSurface _driverSurface;
        internal SurfaceKHR _surface;
        internal Extent2D _extent;
        internal Device _logicalDevice;
        internal Instance _instance;

        internal AVulkanSwapchain(ref Vk _vk, ref KhrSurface _ks, ref SurfaceKHR _sk, ref Extent2D _ex, ref Instance _i, ref Device _ld)
        {
            _vulkan = _vk;
            _driverSurface = _ks;
            _surface = _sk;
            _extent = _ex;
            _instance = _i;
            _logicalDevice = _ld;
        }

        internal void CreateSwapchain(ref PhysicalDevice _physicalDevice)
        {
            SwapChainSupportDetails _support = GetSupportDetails(_physicalDevice);
            _surfaceFormat = GetSwapchainSurfaceFormat(_support.Formats);
            PresentModeKHR _presentMode = GetPresentMode(_support.PresentModes);

            uint _imageCount = _support.Capabilities.MinImageCount + 1;
            SwapchainCreateInfoKHR _swapchainCreateInfo = new SwapchainCreateInfoKHR()
            {
                SType = StructureType.SwapchainCreateInfoKhr,
                Surface = _surface,

                MinImageCount = _imageCount,
                ImageFormat = _surfaceFormat.Format,
                ImageColorSpace = _surfaceFormat.ColorSpace,
                ImageExtent = _extent,
                ImageArrayLayers = 1,
                ImageUsage = ImageUsageFlags.ColorAttachmentBit,
                ImageSharingMode = SharingMode.Exclusive,
                PresentMode = _presentMode,
                Clipped = true,
                OldSwapchain = default,
                CompositeAlpha = CompositeAlphaFlagsKHR.OpaqueBitKhr,
                PreTransform = _support.Capabilities.CurrentTransform
            };

            if (!_vulkan.TryGetDeviceExtension(_instance, _logicalDevice, out _driverSwapchain))
            {
                throw new Exception("VK_KHR_swapchain extension not found on the device");
            }

            Result r = _driverSwapchain!.CreateSwapchain(_logicalDevice, _swapchainCreateInfo, null, out _swapchainKHR);
            if (r != Result.Success)
            {
                throw new Exception("Failed to create swapchain " + r);
            }
            uint _swapchainImageCount = 0;
            _driverSwapchain.GetSwapchainImages(_logicalDevice, _swapchainKHR, &_swapchainImageCount, null);
            _swapchainImages = new Image[_swapchainImageCount];
            fixed (Image* _imagePtr = _swapchainImages)
            {
                _driverSwapchain.GetSwapchainImages(_logicalDevice, _swapchainKHR, &_swapchainImageCount, _imagePtr);
            }
        }

        internal void DestroySwapchain()
        {
            _driverSwapchain.DestroySwapchain(_logicalDevice, _swapchainKHR, null);
        }

        private PresentModeKHR GetPresentMode(IReadOnlyList<PresentModeKHR> _presentModes)
        {
            foreach (var _availablePresentMode in _presentModes)
            {
                if (_availablePresentMode == PresentModeKHR.MailboxKhr)
                {
                    return _availablePresentMode;
                }
            }
            return PresentModeKHR.FifoKhr;
        }

        private SurfaceFormatKHR GetSwapchainSurfaceFormat(IReadOnlyList<SurfaceFormatKHR> _formats)
        {
            foreach (var _availableFormat in _formats)
            {
                if (_availableFormat.Format == Format.B8G8R8A8Srgb && _availableFormat.ColorSpace == ColorSpaceKHR.SpaceSrgbNonlinearKhr)
                {
                    return _availableFormat;
                }
            }
            return _formats[0];
        }

        private SwapChainSupportDetails GetSupportDetails(PhysicalDevice _physicalDevice)
        {
            var _details = new SwapChainSupportDetails();

            _driverSurface!.GetPhysicalDeviceSurfaceCapabilities(_physicalDevice, _surface, out _details.Capabilities);

            //surface formats
            uint _formatCount = 0;
            _driverSurface.GetPhysicalDeviceSurfaceFormats(_physicalDevice, _surface, ref _formatCount, null);
            if (_formatCount != 0)
            {
                _details.Formats = new SurfaceFormatKHR[_formatCount];
                fixed (SurfaceFormatKHR* _fPtr = _details.Formats)
                {
                    _driverSurface.GetPhysicalDeviceSurfaceFormats(_physicalDevice, _surface, ref _formatCount, _fPtr);
                }
            }
            else _details.Formats = Array.Empty<SurfaceFormatKHR>();

            //present modes
            uint _presentModeCount = 0;
            _driverSurface.GetPhysicalDeviceSurfacePresentModes(_physicalDevice, _surface, ref _presentModeCount, null);
            if (_presentModeCount != 0)
            {
                _details.PresentModes = new PresentModeKHR[_presentModeCount];
                fixed (PresentModeKHR* _formatsPtr = _details.PresentModes)
                {
                    _driverSurface.GetPhysicalDeviceSurfacePresentModes(_physicalDevice, _surface, ref _presentModeCount, _formatsPtr);
                }
            }
            else _details.PresentModes = Array.Empty<PresentModeKHR>();

            return _details;
        }
    }
}
