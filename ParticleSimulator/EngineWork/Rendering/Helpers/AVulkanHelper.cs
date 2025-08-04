using ArctisAurora.EngineWork.Rendering.RendererTypes;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace ArctisAurora.EngineWork.Rendering.Helpers
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

        internal static int FindQueueFamilyIndex(ref Vk vk, ref PhysicalDevice gpu, ref QueueFamilyProperties[] qfm, QueueFlags qType)
        {
            uint _propertyCount = 0;
            vk.GetPhysicalDeviceQueueFamilyProperties(gpu, &_propertyCount, null);
            qfm = new QueueFamilyProperties[_propertyCount];

            vk.GetPhysicalDeviceQueueFamilyProperties(gpu, &_propertyCount, qfm);
            for (int i = 0; i < _propertyCount; i++)
                if ((qfm[i].QueueFlags & qType) == qType)
                    return i;

            return int.MaxValue;
        }

        internal static Format GetDepthFormat()
        {
            return FindSupportedFormat(new[] { Format.D32Sfloat, Format.D32SfloatS8Uint, Format.D24UnormS8Uint }, ImageTiling.Optimal, FormatFeatureFlags.DepthStencilAttachmentBit);
        }

        internal static Format GetDepthFormat(ref Vk vk, ref PhysicalDevice gpu)
        {
            return FindSupportedFormat(ref vk, ref gpu, new[] { Format.D32Sfloat, Format.D32SfloatS8Uint, Format.D24UnormS8Uint }, ImageTiling.Optimal, FormatFeatureFlags.DepthStencilAttachmentBit);
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

        internal static Format FindSupportedFormat(ref Vk vk, ref PhysicalDevice gpu, IEnumerable<Format> _formats, ImageTiling _tiling, FormatFeatureFlags _features)
        {
            foreach (Format _f in _formats)
            {
                vk.GetPhysicalDeviceFormatProperties(gpu, _f, out FormatProperties _fp);
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

        internal static QueueFamilyIndices FindQueueFamilies(ref QueueFamilyProperties[] queueFamilyProperties, ref PhysicalDevice gpu, ref KhrSurface _driverSurface, ref SurfaceKHR _surface)
        {
            QueueFamilyIndices _qfi = new QueueFamilyIndices();

            uint i = 0;
            foreach (var _qf in queueFamilyProperties)
            {
                if (_qf.QueueFlags.HasFlag(QueueFlags.GraphicsBit))
                {
                    _qfi.GraphicsFamily = i;
                }
                _driverSurface.GetPhysicalDeviceSurfaceSupport(gpu, i, _surface, out var _presentSupport);

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


        internal static SwapChainSupportDetails GetSupportDetails(ref PhysicalDevice gpu, ref KhrSurface _driverSurface, ref SurfaceKHR _surface)
        {
            var _details = new SwapChainSupportDetails();

            _driverSurface!.GetPhysicalDeviceSurfaceCapabilities(gpu, _surface, out _details.Capabilities);

            //surface formats
            uint _formatCount = 0;
            _driverSurface.GetPhysicalDeviceSurfaceFormats(gpu, _surface, ref _formatCount, null);
            if (_formatCount != 0)
            {
                _details.Formats = new SurfaceFormatKHR[_formatCount];
                fixed (SurfaceFormatKHR* _fPtr = _details.Formats)
                {
                    _driverSurface.GetPhysicalDeviceSurfaceFormats(gpu, _surface, ref _formatCount, _fPtr);
                }
            }
            else _details.Formats = Array.Empty<SurfaceFormatKHR>();

            //present modes
            uint _presentModeCount = 0;
            _driverSurface.GetPhysicalDeviceSurfacePresentModes(gpu, _surface, ref _presentModeCount, null);
            if (_presentModeCount != 0)
            {
                _details.PresentModes = new PresentModeKHR[_presentModeCount];
                fixed (PresentModeKHR* _formatsPtr = _details.PresentModes)
                {
                    _driverSurface.GetPhysicalDeviceSurfacePresentModes(gpu, _surface, ref _presentModeCount, _formatsPtr);
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

        internal static uint FindPresentSupportIndex(ref PhysicalDevice gpu, ref QueueFamilyProperties[] _qfm, ref KhrSurface _driverSurface, ref SurfaceKHR _surface)
        {
            uint i = 0;
            foreach (var _qf in _qfm)
            {
                if (_qf.QueueFlags.HasFlag(QueueFlags.GraphicsBit))
                {
                    _driverSurface.GetPhysicalDeviceSurfaceSupport(gpu, i, _surface, out var _presentSupport);
                    if (_presentSupport)
                    {
                        return i;
                    }
                }
                i++;
            }
            return int.MaxValue;
        }

        internal static ulong GetBufferAdress(ref Buffer _b)
        {
            BufferDeviceAddressInfo _addressInfo = new BufferDeviceAddressInfo()
            {
                SType = StructureType.BufferDeviceAddressInfo,
                Buffer = _b,
            };
            return VulkanRenderer._vulkan.GetBufferDeviceAddress(VulkanRenderer._logicalDevice, ref _addressInfo);
        }

        internal static uint AlignedSize(uint _value, uint _alignment)
        {
            uint a = (_value + _alignment - 1) & ~(_alignment - 1);
            return a;
        }

        internal static nint StringToNint(string str)
        {
            if (str == null) throw new ArgumentNullException(nameof(str));
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(str);
            fixed (byte* ptr = bytes)
            {
                return (nint)ptr;
            }
        }

        internal static nint ArrayToNint<T>(T[] array) where T : unmanaged
        {
            if (array == null) throw new ArgumentNullException(nameof(array));
            fixed (T* ptr = array)
            {
                return (nint)ptr;
            }
        }
    }
}