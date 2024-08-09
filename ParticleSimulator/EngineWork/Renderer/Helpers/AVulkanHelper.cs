using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;


namespace ArctisAurora.EngineWork.Renderer.Helpers
{
    internal unsafe static class AVulkanHelper
    {
        internal struct QueueFamilyIndices
        {
            public uint? GraphicsFamily { get; set; }
            public uint? PresentFamily { get; set; }

            public bool IsComplete()
            {
                return GraphicsFamily.HasValue && PresentFamily.HasValue;
            }
        }

        internal static uint Version(uint major, uint minor, uint patch)
        {
            return major << 22 | minor << 12 | patch;
        }

        internal static int FindQueueFamilyIndex(ref PhysicalDevice _gpu, ref QueueFamilyProperties[] _qfm, QueueFlags _qType)
        {
            uint _propertyCount = 0;
            VulkanRenderer._vulkan.GetPhysicalDeviceQueueFamilyProperties(_gpu, &_propertyCount, null);
            _qfm = new QueueFamilyProperties[_propertyCount];

            Rasterizer._vulkan.GetPhysicalDeviceQueueFamilyProperties(_gpu, &_propertyCount, _qfm);
            for (int i = 0; i < _propertyCount; i++)
                if ((_qfm[i].QueueFlags & _qType) == _qType)
                    return i;

            return int.MaxValue;
        }

        internal static Format GetDepthFormat()
        {
            return FindSupportedFormat(new[] { Format.D32Sfloat, Format.D32SfloatS8Uint, Format.D24UnormS8Uint }, ImageTiling.Optimal, FormatFeatureFlags.DepthStencilAttachmentBit);
        }

        internal static Format FindSupportedFormat(IEnumerable<Format> _formats, ImageTiling _tiling, FormatFeatureFlags _features)
        {
            foreach (Format _f in _formats)
            {
                Rasterizer._vulkan.GetPhysicalDeviceFormatProperties(Rasterizer._gpu, _f, out FormatProperties _fp);
                if (_tiling == ImageTiling.Linear && (_fp.LinearTilingFeatures & _features) == _features)
                {
                    return _f;
                }
                else if (_tiling == ImageTiling.Optimal && (_fp.OptimalTilingFeatures & _features) == _features)
                {
                    return _f;
                }
            }
            throw new Exception("Failed to find requested format");
        }

        internal static QueueFamilyIndices FindQueueFamilies(ref KhrSurface _driverSurface, ref SurfaceKHR _surface)
        {
            QueueFamilyIndices _qfi = new QueueFamilyIndices();

            uint _qfc = 0;
            VulkanRenderer._vulkan.GetPhysicalDeviceQueueFamilyProperties(VulkanRenderer._gpu, ref _qfc, null);

            var _qfp = new QueueFamilyProperties[_qfc];
            fixed (QueueFamilyProperties* _qfpPtr = _qfp)
            {
                VulkanRenderer._vulkan.GetPhysicalDeviceQueueFamilyProperties(VulkanRenderer._gpu, ref _qfc, _qfpPtr);
            }

            uint i = 0;
            foreach (var _qf in _qfp)
            {
                if (_qf.QueueFlags.HasFlag(QueueFlags.GraphicsBit))
                {
                    _qfi.GraphicsFamily = i;
                }
                _driverSurface.GetPhysicalDeviceSurfaceSupport(VulkanRenderer._gpu, i, _surface, out var _presentSupport);

                if (_presentSupport)
                {
                    _qfi.PresentFamily = i;
                }
                if (_qfi.IsComplete())
                {
                    break;
                }
                i++;
            }
            return _qfi;
        }

        internal static SwapChainSupportDetails GetSupportDetails(ref KhrSurface _driverSurface, ref SurfaceKHR _surface)
        {
            var _details = new SwapChainSupportDetails();

            _driverSurface!.GetPhysicalDeviceSurfaceCapabilities(Rasterizer._gpu, _surface, out _details.Capabilities);

            //surface formats
            uint _formatCount = 0;
            _driverSurface.GetPhysicalDeviceSurfaceFormats(Rasterizer._gpu, _surface, ref _formatCount, null);
            if (_formatCount != 0)
            {
                _details.Formats = new SurfaceFormatKHR[_formatCount];
                fixed (SurfaceFormatKHR* _fPtr = _details.Formats)
                {
                    _driverSurface.GetPhysicalDeviceSurfaceFormats(Rasterizer._gpu, _surface, ref _formatCount, _fPtr);
                }
            }
            else _details.Formats = Array.Empty<SurfaceFormatKHR>();

            //present modes
            uint _presentModeCount = 0;
            _driverSurface.GetPhysicalDeviceSurfacePresentModes(Rasterizer._gpu, _surface, ref _presentModeCount, null);
            if (_presentModeCount != 0)
            {
                _details.PresentModes = new PresentModeKHR[_presentModeCount];
                fixed (PresentModeKHR* _formatsPtr = _details.PresentModes)
                {
                    _driverSurface.GetPhysicalDeviceSurfacePresentModes(Rasterizer._gpu, _surface, ref _presentModeCount, _formatsPtr);
                }
            }
            else _details.PresentModes = Array.Empty<PresentModeKHR>();

            return _details;
        }

        internal static PresentModeKHR GetPresentMode(IReadOnlyList<PresentModeKHR> _presentModes)
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

        internal static SurfaceFormatKHR GetSwapchainSurfaceFormat(IReadOnlyList<SurfaceFormatKHR> _formats)
        {
            foreach (var _availableFormat in _formats)
            {
                if (_availableFormat.Format == Format.R8G8B8A8Unorm && _availableFormat.ColorSpace == ColorSpaceKHR.SpaceSrgbNonlinearKhr)
                {
                    return _availableFormat;
                }
            }
            return _formats[0];
        }

        internal static uint FindPresentSupportIndex(ref QueueFamilyProperties[] _qfm, ref KhrSurface _driverSurface, ref SurfaceKHR _surface)
        {
            uint i = 0;
            foreach (var _qf in _qfm)
            {
                if (_qf.QueueFlags.HasFlag(QueueFlags.GraphicsBit))
                {
                    _driverSurface.GetPhysicalDeviceSurfaceSupport(Rasterizer._gpu, i, _surface, out var _presentSupport);
                    if (_presentSupport)
                    {
                        return i;
                    }
                }
                i++;
            }
            return int.MaxValue;
        }
    }
}