using ArctisAurora.EngineWork.Renderer.Helpers;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using static ArctisAurora.EngineWork.Renderer.Helpers.AVulkanHelper;
using Image = Silk.NET.Vulkan.Image;
using ImageLayout = Silk.NET.Vulkan.ImageLayout;

namespace ArctisAurora.EngineWork.Renderer
{
    struct SwapChainSupportDetails
    {
        public SurfaceCapabilitiesKHR Capabilities;
        public SurfaceFormatKHR[] Formats;
        public PresentModeKHR[] PresentModes;
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
            SwapChainSupportDetails _support = GetSupportDetails(ref _driverSurface, ref _surface);
            _surfaceFormat = GetSwapchainSurfaceFormat(_support.Formats);
            PresentModeKHR _presentMode = GetPresentMode(_support.PresentModes);

            QueueFamilyIndices _indices = FindQueueFamilies(ref _driverSurface, ref _surface);
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

        internal void DoSwapchainMethodSequence(ref Extent2D _extent)
        {
            CreateSwapchain(ref _extent);            //swapchain imageviews, and what we will be outputting to the screen
            _imageViews = new ImageView[_swapchainImages.Length];
            for (int i = 0; i < _swapchainImages.Length; i++)
            {
                CreateImageView(ref _imageViews[i], ref _swapchainImages[i], ImageAspectFlags.ColorBit, _surfaceFormat.Format);
            }
            Format _depthFormat = GetDepthFormat();
            AVulkanBufferHandler.CreateImage(VulkanRenderer._extent.Width, VulkanRenderer._extent.Height, _depthFormat, ImageTiling.Optimal, ImageUsageFlags.DepthStencilAttachmentBit, MemoryPropertyFlags.DeviceLocalBit, ref _depthImage, ref _depthMemory);
            CreateImageView(ref _depthView, ref _depthImage, ImageAspectFlags.DepthBit, _depthFormat); //depth map
            CreateRenderPass();
            //CreateShadowmapRenderPass();
        }

        internal void CreateImageView(ref ImageView _iv, ref Image _im, ImageAspectFlags _aspect, Format _f)
        {
            ImageViewCreateInfo _createInfo = new ImageViewCreateInfo
            {
                SType = StructureType.ImageViewCreateInfo,
                Image = _im,
                Format = _f,
                ViewType = ImageViewType.Type2D
            };

            _createInfo.SubresourceRange.AspectMask = _aspect;
            _createInfo.SubresourceRange.BaseMipLevel = 0;
            _createInfo.SubresourceRange.LevelCount = 1;
            _createInfo.SubresourceRange.BaseArrayLayer = 0;
            _createInfo.SubresourceRange.LayerCount = 1;

            if (VulkanRenderer._vulkan!.CreateImageView(VulkanRenderer._logicalDevice, _createInfo, null, out _iv) != Result.Success)
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
                StoreOp = AttachmentStoreOp.Store,
                StencilLoadOp = AttachmentLoadOp.DontCare,
                StencilStoreOp = AttachmentStoreOp.DontCare,
                InitialLayout = ImageLayout.Undefined,
                FinalLayout = ImageLayout.DepthStencilReadOnlyOptimal
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
    }
}