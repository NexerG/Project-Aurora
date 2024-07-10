using ArctisAurora.EngineWork.ComponentBehaviour;
using ArctisAurora.EngineWork.Rendering.Renderers.Renderer_Vulkan;
using ArctisAurora.EngineWork.Rendering.Renderers.Vulkan;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using System.Runtime.CompilerServices;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace ArctisAurora.EngineWork.ECS.RenderingComponents.Vulkan
{
    internal unsafe class AVulkanLightsourceComponent: EntityComponent
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
        internal Matrix4X4<float> _lightProjection = Matrix4X4.CreateOrthographicOffCenter(-35f, 35f, -35f, 35f, 0.1f, 1000f);

        public AVulkanLightsourceComponent()
        {
            //CreateDescriptorSet();
            CreateShadowFramebuffer(new Extent2D(1000,1000));
        }

        public override void OnStart()
        {
            VulkanRenderer._rendererInstance.AddLighToRenderQueue(parent);
            VulkanRenderer._rendererInstance.RecreateCommandBuffers();
        }

        /*internal void SingletonMatrix()
        {
            Vector3D<float> _pos = new Vector3D<float>(0, 0, 0);
            Quaternion<float> q = Quaternion<float>.CreateFromYawPitchRoll(0,0,0);

            Matrix4X4<float> _transform = Matrix4X4<float>.Identity;
            //_transform *= Matrix4X4.CreateScale(new Vector3D<float>(1,1,1));
            //_transform *= Matrix4X4.CreateFromQuaternion(q);
            //_transform *= Matrix4X4.CreateTranslation(_pos);

            _transformMatrix = _transform;
        }*/

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

        /*internal void CreateDescriptorSet()
        {
            DescriptorSetLayout[] _layouts = new DescriptorSetLayout[VulkanRenderer._swapchain!._swapchainImages.Length];
            Array.Fill(_layouts, VulkanRenderer._descriptorSetLayout);

            fixed (DescriptorSetLayout* _layoutsPtr = _layouts)
            {
                DescriptorSetAllocateInfo _allocateInfo = new DescriptorSetAllocateInfo()
                {
                    SType = StructureType.DescriptorSetAllocateInfo,
                    DescriptorPool = VulkanRenderer._descriptorPool,
                    DescriptorSetCount = (uint)VulkanRenderer._swapchain!._swapchainImages.Length,
                    PSetLayouts = _layoutsPtr
                };

                _descriptorSets = new DescriptorSet[VulkanRenderer._swapchain._swapchainImages.Length];
                fixed (DescriptorSet* _descriptorSetsPtr = _descriptorSets)
                {
                    Result r = VulkanRenderer._vulkan.AllocateDescriptorSets(VulkanRenderer._logicalDevice, _allocateInfo, _descriptorSetsPtr);
                    if (r != Result.Success)
                    {
                        throw new Exception("Failed to allocate descriptor set withe error code: " + r);
                    }
                }
            }
            for (int i = 0; i < VulkanRenderer._swapchain._swapchainImages.Length; i++)
            {
                DescriptorBufferInfo _bufferInfoUniform = new DescriptorBufferInfo()
                {
                    Buffer = _uniformBuffers[i],
                    Offset = 0,
                    Range = (ulong)Unsafe.SizeOf<UBO>()
                };

                DescriptorBufferInfo _bufferInfoMatrices = new DescriptorBufferInfo()
                {
                    Buffer = _trasnformsBuffer,
                    Offset = 0,
                    Range = Vk.WholeSize
                };

                var _writeDescriptorSets = new WriteDescriptorSet[]
                {
                    new WriteDescriptorSet()
                    {
                        SType = StructureType.WriteDescriptorSet,
                        DstSet = _descriptorSets[i],
                        DstBinding = 0,
                        DstArrayElement = 0,
                        DescriptorType = DescriptorType.UniformBuffer,
                        DescriptorCount = 1,
                        PBufferInfo = &_bufferInfoUniform
                    },
                    new WriteDescriptorSet()
                    {
                        SType = StructureType.WriteDescriptorSet,
                        DstSet = _descriptorSets[i],
                        DstBinding = 1,
                        DstArrayElement = 0,
                        DescriptorType = DescriptorType.StorageBuffer,
                        DescriptorCount = 1,
                        PBufferInfo = &_bufferInfoMatrices
                    }
                };
                fixed(WriteDescriptorSet* _descPtr = _writeDescriptorSets)
                {
                    VulkanRenderer._vulkan!.UpdateDescriptorSets(VulkanRenderer._logicalDevice, (uint)_writeDescriptorSets.Length, _descPtr, 0, null);
                }
            }
        }*/

        internal void UpdateVPMatrices(uint _currentImage)
        {
            _lightView = Matrix4X4.CreateLookAt(parent.transform.position, new Vector3D<float>(0, 0, 0), new Vector3D<float>(0, 1, 0));
        }

        /*internal void CreateUniformBuffers()
        {
            VulkanRenderer._bufferHandlerHelper.CreateUniformBuffer(ref _uniformBuffers, ref _uniformBuffersMemory);
        }*/

        internal void EnqueueDrawCommands(ulong[] _offset, int _loopIndex, ref CommandBuffer _commandBuffer)
        {
            if (_render)
            {
                VulkanRenderer._vulkan.CmdBindDescriptorSets(_commandBuffer, PipelineBindPoint.Graphics, VulkanRenderer._pipeline._pipelineLayout, 0, 1, _descriptorSets[_loopIndex], 0, null);
            }
        }

        private void CreateDepthImage(Extent2D _resolution)
        {
            Format _depthFormat = GetDepthFormat();
            VulkanRenderer._bufferHandlerHelper.CreateImage(_resolution.Width, _resolution.Height, _depthFormat, ImageTiling.Optimal, ImageUsageFlags.DepthStencilAttachmentBit, MemoryPropertyFlags.DeviceLocalBit, ref _depthImage, ref _depthBufferMemory);
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