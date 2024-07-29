using ArctisAurora.EngineWork.ComponentBehaviour;
using ArctisAurora.EngineWork.Renderer;
using ArctisAurora.EngineWork.Renderer.Helpers;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using static ArctisAurora.EngineWork.Renderer.Helpers.AVulkanHelper;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace ArctisAurora.EngineWork.ECS.RenderingComponents.Vulkan
{
    internal unsafe class LightsourceComponent: EntityComponent
    {
        bool _render = true;

        //Descriptor set
        internal DescriptorSet[] _descriptorSets;

        //buffer objects
        internal Buffer _trasnformsBuffer;
        internal DeviceMemory _trasnformsBufferMemory;

        internal Silk.NET.Vulkan.Image _depthImage;
        internal ImageView _depthImageView;
        internal DeviceMemory _depthBufferMemory;
        internal Framebuffer _shadowFramebuffer;

        //internal Matrix4X4<float> _transformMatrix;
        internal Matrix4X4<float> _lightView;
        internal Matrix4X4<float> _lightProjection;
        internal Matrix4X4<float> _lpv;
        internal Vector4D<float> _lightColor = new Vector4D<float>(1, 1, 1, 1);

        public LightsourceComponent()
        {
            //CreateDescriptorSet();
            CreateShadowFramebuffer(new Extent2D(2000,2000));
        }

        public override void OnStart()
        {
            RendererBaseClass._rendererInstance.AddLightToRenderQueue(parent);
            ((IRecreateCommandBuffer)RendererBaseClass._rendererInstance).RecreateCommandBuffers();
        }

        internal void CreateShadowFramebuffer(Extent2D _resolution)
        {
            CreateDepthImage(_resolution);
            var _attachment = new[] { _depthImageView };

            fixed (ImageView* _imAttachmentPtr = _attachment)
            {
                FramebufferCreateInfo _framebufferInfo = new FramebufferCreateInfo()
                {
                    SType = StructureType.FramebufferCreateInfo,
                    RenderPass = VulkanRenderer._swapchain._shadowmapRenderPass,
                    AttachmentCount = (uint)_attachment.Length,
                    PAttachments = _imAttachmentPtr,
                    Width = _resolution.Width,
                    Height = _resolution.Height,
                    Layers = 1
                };
                if (VulkanRenderer._vulkan.CreateFramebuffer(VulkanRenderer._logicalDevice, _framebufferInfo, null, out _shadowFramebuffer) != Result.Success)
                {
                    throw new Exception("Failed to create frame buffer");
                }
            }
        }

        internal void UpdateVPMatrices(uint _currentImage)
        {
            _lightProjection = Matrix4X4.CreateOrthographicOffCenter(-35f, 35f, -35f, 35f, 5, 300f);
            _lightProjection.M22 *= -1;
            _lightView = Matrix4X4.CreateLookAt(parent.transform.position, Vector3D<float>.Zero, Vector3D<float>.UnitY);
            _lpv = _lightProjection * _lightView;
        }

        private void CreateDepthImage(Extent2D _resolution)
        {
            Format _depthFormat = GetDepthFormat();
            AVulkanBufferHandler.CreateImage(_resolution.Width, _resolution.Height, _depthFormat, ImageTiling.Optimal, ImageUsageFlags.DepthStencilAttachmentBit | ImageUsageFlags.SampledBit, MemoryPropertyFlags.DeviceLocalBit, ref _depthImage, ref _depthBufferMemory);
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

            if (VulkanRenderer._vulkan!.CreateImageView(VulkanRenderer._logicalDevice, _createInfo, null, out _depthImageView) != Result.Success)
            {
                throw new Exception("failed to create image views!");
            }
        }
    }
}