using ArctisAurora.EngineWork.ComponentBehaviour;
using ArctisAurora.EngineWork.Renderer;
using ArctisAurora.EngineWork.Renderer.Helpers;
using ArctisAurora.EngineWork.Renderer.RendererTypes;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using static ArctisAurora.EngineWork.Renderer.Helpers.AVulkanHelper;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace ArctisAurora.EngineWork.ECS.RenderingComponents.Vulkan
{
    internal unsafe class LightsourceComponent: MeshComponent
    {
        internal struct LightData
        {
            internal Matrix4X4<float> view;
            internal Matrix4X4<float> projection;
            internal Vector3D<float> position;
            internal Vector3D<float> color;

            public LightData()
            {
                position = new Vector3D<float>(0, 0, 0);
                color = new Vector3D<float>(1, 1, 1);
            }
        }

        internal Silk.NET.Vulkan.Image _depthImage;
        internal ImageView _depthImageView;
        internal DeviceMemory _depthBufferMemory;
        internal Framebuffer _shadowFramebuffer;

        internal LightData _lightData = new LightData();
        internal Buffer _lightDataBuffer;
        internal DeviceMemory _lightDataDM;

        public LightsourceComponent()
        {
            //CreateDescriptorSet();
            CreateShadowFramebuffer(new Extent2D(2000,2000));
        }

        public override void OnStart()
        {
            SingletonMatrix();
            VulkanRenderer._rendererInstance.AddLightToRenderQueue(parent);
            //VulkanRenderer._rendererInstance.RecreateCommandBuffers();
        }

        internal override void SingletonMatrix()
        {
            base.SingletonMatrix();
            _lightData.projection = Matrix4X4.CreateOrthographicOffCenter(-35f, 35f, -35f, 35f, 5, 300f);
            _lightData.projection.M22 *= -1;
            _lightData.view = Matrix4X4.CreateLookAt(parent.transform.position, Vector3D<float>.Zero, Vector3D<float>.UnitY);

            AVulkanBufferHandler.CreateBuffer(ref _lightData, ref _lightDataBuffer, ref _lightDataDM, BufferUsageFlags.ShaderDeviceAddressBit | BufferUsageFlags.UniformBufferBit);
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
                    RenderPass = Rasterizer._swapchain._shadowmapRenderPass,
                    AttachmentCount = (uint)_attachment.Length,
                    PAttachments = _imAttachmentPtr,
                    Width = _resolution.Width,
                    Height = _resolution.Height,
                    Layers = 1
                };
                if (Rasterizer._vulkan.CreateFramebuffer(Rasterizer._logicalDevice, ref _framebufferInfo, null, out _shadowFramebuffer) != Result.Success)
                {
                    throw new Exception("Failed to create frame buffer");
                }
            }
        }

        internal override void UpdateMatrices()
        {
            base.UpdateMatrices();
        }

        internal override void EnqueueDrawCommands(ref ulong[] _offset, int _loopIndex, ref CommandBuffer _commandBuffer)
        {
            //base.EnqueueDrawCommands(ref _offset, _loopIndex, ref _commandBuffer);
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

            if (Rasterizer._vulkan!.CreateImageView(Rasterizer._logicalDevice, ref _createInfo, null, out _depthImageView) != Result.Success)
            {
                throw new Exception("failed to create image views!");
            }
        }
    }
}