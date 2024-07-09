using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using Image = Silk.NET.Vulkan.Image;
using ImageLayout = Silk.NET.Vulkan.ImageLayout;

namespace ArctisAurora.EngineWork.Rendering.Renderers.Vulkan
{
    struct SwapChainSupportDetails
    {
        public SurfaceCapabilitiesKHR Capabilities;
        public SurfaceFormatKHR[] Formats;
        public PresentModeKHR[] PresentModes;
    }

    struct QueueFamilyIndices
    {
        public uint? GraphicsFamily { get; set; }
        public uint? PresentFamily { get; set; }

        public bool IsComplete()
        {
            return GraphicsFamily.HasValue && PresentFamily.HasValue;
        }
    }

    internal unsafe class AVulkanSwapchain
    {
        //swapchain variables
        internal SwapchainKHR _swapchainKHR;        //the virtualized swapchain
        internal KhrSwapchain _driverSwapchain;     //the driver swapchain
        internal Image[] _swapchainImages;          //swapchain images for rendering
        internal ImageView[] _imageViews;           //image views for rendering
        internal Image _depthImage;
        internal ImageView _depthView;
        internal DeviceMemory _depthMemory;
        internal SurfaceFormatKHR _surfaceFormat;   //window format
        internal RenderPass _renderPass;
        internal RenderPass _shadowmapRenderPass;

        //external references
        internal KhrSurface _driverSurface;
        internal SurfaceKHR _surface;

        internal AVulkanSwapchain(ref KhrSurface _ks, ref SurfaceKHR _sk)
        {
            _driverSurface = _ks;
            _surface = _sk;
        }

        internal void CreateSwapchain(ref Extent2D _extent)
        {
            SwapChainSupportDetails _support = GetSupportDetails(VulkanRenderer._gpu);
            _surfaceFormat = GetSwapchainSurfaceFormat(_support.Formats);
            PresentModeKHR _presentMode = GetPresentMode(_support.PresentModes);

            QueueFamilyIndices _indices = FindQueueFamilies();
            var _queueFamilyIndices = stackalloc[] { _indices.GraphicsFamily.Value, _indices.PresentFamily.Value };

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
                PreTransform = _support.Capabilities.CurrentTransform,
                QueueFamilyIndexCount = 2,
                PQueueFamilyIndices = _queueFamilyIndices
            };

            if (!VulkanRenderer._vulkan.TryGetDeviceExtension(VulkanRenderer._instance, VulkanRenderer._logicalDevice, out _driverSwapchain))
            {
                throw new Exception("VK_KHR_swapchain extension not found on the device");
            }

            Result r = _driverSwapchain!.CreateSwapchain(VulkanRenderer._logicalDevice, _swapchainCreateInfo, null, out _swapchainKHR);
            if (r != Result.Success)
            {
                throw new Exception("Failed to create swapchain " + r);
            }
            uint _swapchainImageCount = 0;
            _driverSwapchain.GetSwapchainImages(VulkanRenderer._logicalDevice, _swapchainKHR, &_swapchainImageCount, null);
            _swapchainImages = new Image[_swapchainImageCount];
            fixed (Image* _imagePtr = _swapchainImages)
            {
                _driverSwapchain.GetSwapchainImages(VulkanRenderer._logicalDevice, _swapchainKHR, &_swapchainImageCount, _imagePtr);
            }
        }

        internal void CreateImageView()
        {
            _imageViews = new ImageView[_swapchainImages.Length];
            for (int i = 0; i < _swapchainImages.Length; i++)
            {
                ImageViewCreateInfo _createInfo = new ImageViewCreateInfo
                {
                    SType = StructureType.ImageViewCreateInfo,
                    Image = _swapchainImages[i],
                    Format = _surfaceFormat.Format,
                    ViewType = ImageViewType.Type2D
                };

                /*_createInfo.Components.R = ComponentSwizzle.Identity;
                _createInfo.Components.G = ComponentSwizzle.Identity;
                _createInfo.Components.B = ComponentSwizzle.Identity;
                _createInfo.Components.A = ComponentSwizzle.Identity;*/

                _createInfo.SubresourceRange.AspectMask = ImageAspectFlags.ColorBit;
                _createInfo.SubresourceRange.BaseMipLevel = 0;
                _createInfo.SubresourceRange.LevelCount = 1;
                _createInfo.SubresourceRange.BaseArrayLayer = 0;
                _createInfo.SubresourceRange.LayerCount = 1;

                if (VulkanRenderer._vulkan!.CreateImageView(VulkanRenderer._logicalDevice, _createInfo, null, out _imageViews[i]) != Result.Success)
                {
                    throw new Exception("failed to create image views!");
                }
            }
        }

        internal void CreateDepthImages()
        {
            Format _depthFormat = GetDepthFormat();
            VulkanRenderer._bufferHandlerHelper.CreateImage(VulkanRenderer._extent.Width, VulkanRenderer._extent.Height, _depthFormat, ImageTiling.Optimal, ImageUsageFlags.DepthStencilAttachmentBit, MemoryPropertyFlags.DeviceLocalBit, ref _depthImage, ref _depthMemory);
            ImageViewCreateInfo _createInfo = new ImageViewCreateInfo
            {
                SType = StructureType.ImageViewCreateInfo,
                Image = _depthImage,
                Format = _depthFormat,
                ViewType = ImageViewType.Type2D
            };

            _createInfo.SubresourceRange.AspectMask = ImageAspectFlags.DepthBit;
            _createInfo.SubresourceRange.BaseMipLevel = 0;
            _createInfo.SubresourceRange.LevelCount = 1;
            _createInfo.SubresourceRange.BaseArrayLayer = 0;
            _createInfo.SubresourceRange.LayerCount = 1;

            if (VulkanRenderer._vulkan!.CreateImageView(VulkanRenderer._logicalDevice, _createInfo, null, out _depthView) != Result.Success)
            {
                throw new Exception("failed to create image views!");
            }
        }

        internal void CreateRenderPass()
        {
            AttachmentDescription _colorAttachment = new AttachmentDescription()
            {
                Format = _surfaceFormat.Format,
                Samples = SampleCountFlags.Count1Bit,
                LoadOp = AttachmentLoadOp.Clear,
                StoreOp = AttachmentStoreOp.Store,
                StencilLoadOp = AttachmentLoadOp.DontCare,
                InitialLayout = ImageLayout.Undefined,
                FinalLayout = ImageLayout.PresentSrcKhr,
            };

            AttachmentReference _colorAttachmentRef = new AttachmentReference()
            {
                Attachment = 0,
                Layout = ImageLayout.ColorAttachmentOptimal,
            };

            AttachmentDescription _depthAttachment = new AttachmentDescription()
            {
                Format = GetDepthFormat(),
                Samples = SampleCountFlags.Count1Bit,
                LoadOp = AttachmentLoadOp.Clear,
                StoreOp = AttachmentStoreOp.DontCare,
                StencilLoadOp = AttachmentLoadOp.DontCare,
                StencilStoreOp = AttachmentStoreOp.DontCare,
                InitialLayout = ImageLayout.Undefined,
                FinalLayout = ImageLayout.DepthStencilAttachmentOptimal
            };

            AttachmentReference _depthAttachmentRef = new AttachmentReference()
            {
                Attachment = 1,
                Layout = ImageLayout.DepthStencilAttachmentOptimal
            };

            SubpassDescription _subpass = new SubpassDescription()
            {
                PipelineBindPoint = PipelineBindPoint.Graphics,
                ColorAttachmentCount = 1,
                PColorAttachments = &_colorAttachmentRef,
                PDepthStencilAttachment = &_depthAttachmentRef
            };

            SubpassDependency _subDepend = new SubpassDependency()
            {
                SrcSubpass = Vk.SubpassExternal,
                DstSubpass = 0,
                SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.EarlyFragmentTestsBit,
                SrcAccessMask = 0,
                DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.EarlyFragmentTestsBit,
                DstAccessMask = AccessFlags.ColorAttachmentWriteBit | AccessFlags.DepthStencilAttachmentWriteBit
            };

            var _attachments = new[] { _colorAttachment, _depthAttachment };
            fixed (AttachmentDescription* _attachmentPtr = _attachments)
            {
                RenderPassCreateInfo _renderPassInfo = new RenderPassCreateInfo()
                {
                    SType = StructureType.RenderPassCreateInfo,
                    AttachmentCount = (uint)_attachments.Length,
                    PAttachments = _attachmentPtr,
                    SubpassCount = 1,
                    PSubpasses = &_subpass,
                    DependencyCount = 1,
                    PDependencies = &_subDepend
                };

                if (VulkanRenderer._vulkan.CreateRenderPass(VulkanRenderer._logicalDevice, _renderPassInfo, null, out _renderPass) != Result.Success)
                {
                    throw new Exception("failed to create render pass!");
                }
            }
        }

        internal void CreateShadowmapRenderPass()
        {
            AttachmentDescription _depthAttachment = new AttachmentDescription()
            {
                Format = GetDepthFormat(),
                Samples = SampleCountFlags.Count1Bit,
                LoadOp = AttachmentLoadOp.Clear,
                StoreOp = AttachmentStoreOp.DontCare,
                StencilLoadOp = AttachmentLoadOp.DontCare,
                StencilStoreOp = AttachmentStoreOp.DontCare,
                InitialLayout = ImageLayout.Undefined,
                FinalLayout = ImageLayout.DepthStencilAttachmentOptimal
            };

            AttachmentReference _depthAttachmentRef = new AttachmentReference()
            {
                Attachment = 0,
                Layout = ImageLayout.DepthStencilAttachmentOptimal
            };

            SubpassDescription _subpass = new SubpassDescription()
            {
                PipelineBindPoint = PipelineBindPoint.Graphics,
                ColorAttachmentCount = 0,
                PColorAttachments = null,
                PDepthStencilAttachment = &_depthAttachmentRef
            };

            SubpassDependency _subDepend = new SubpassDependency()
            {
                SrcSubpass = Vk.SubpassExternal,
                DstSubpass = 0,
                SrcStageMask = PipelineStageFlags.EarlyFragmentTestsBit,
                SrcAccessMask = 0,
                DstStageMask = PipelineStageFlags.EarlyFragmentTestsBit,
                DstAccessMask = AccessFlags.DepthStencilAttachmentWriteBit
            };

            var _attachments = new[] { _depthAttachment };
            fixed (AttachmentDescription* _attachmentPtr = _attachments)
            {
                RenderPassCreateInfo _renderPassInfo = new RenderPassCreateInfo()
                {
                    SType = StructureType.RenderPassCreateInfo,
                    AttachmentCount = (uint)_attachments.Length,
                    PAttachments = _attachmentPtr,
                    SubpassCount = 1,
                    PSubpasses = &_subpass,
                    DependencyCount = 1,
                    PDependencies = &_subDepend
                };

                if (VulkanRenderer._vulkan.CreateRenderPass(VulkanRenderer._logicalDevice, _renderPassInfo, null, out _shadowmapRenderPass) != Result.Success)
                {
                    throw new Exception("failed to create render pass!");
                }
            }
        }

        internal void DestroySwapchain()
        {
            foreach (var iv in _imageViews)
            {
                VulkanRenderer._vulkan.DestroyImageView(VulkanRenderer._logicalDevice, iv, null);
            }
            _driverSwapchain.DestroySwapchain(VulkanRenderer._logicalDevice, _swapchainKHR, null);
        }

        internal void DoSwapchainMethodSequence(ref Extent2D _extent)
        {
            CreateSwapchain(ref _extent);
            CreateImageView();
            CreateRenderPass();
            CreateShadowmapRenderPass();
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

        private QueueFamilyIndices FindQueueFamilies()
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

        private Format GetDepthFormat()
        {
            return FindSupportedFormat(new[] { Format.D32Sfloat, Format.D32SfloatS8Uint, Format.D24UnormS8Uint }, ImageTiling.Optimal, FormatFeatureFlags.DepthStencilAttachmentBit);
        }

        private Format FindSupportedFormat(IEnumerable<Format> _formats, ImageTiling _tiling, FormatFeatureFlags _features)
        {
            foreach (Format _f in _formats)
            {
                VulkanRenderer._vulkan.GetPhysicalDeviceFormatProperties(VulkanRenderer._gpu, _f, out FormatProperties _fp);
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
    }
}