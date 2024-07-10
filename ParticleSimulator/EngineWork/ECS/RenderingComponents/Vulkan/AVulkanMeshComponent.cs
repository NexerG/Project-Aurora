using ArctisAurora.EngineWork.ComponentBehaviour;
using ArctisAurora.EngineWork.Rendering.Renderers.Renderer_Vulkan;
using ArctisAurora.EngineWork.Rendering.Renderers.Vulkan;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using System.Runtime.CompilerServices;
using Buffer = Silk.NET.Vulkan.Buffer;
using ImageLayout = Silk.NET.Vulkan.ImageLayout;

namespace ArctisAurora.EngineWork.ECS.RenderingComponents.Vulkan
{
    internal unsafe class AVulkanMeshComponent : EntityComponent
    {
        bool _render = true;
        internal AVulkanMesh _mesh = new AVulkanMesh();

        //Descriptor set
        internal DescriptorSet[] _descriptorSets;
        internal DescriptorSet[] _descriptorSetsShadow;

        //buffer objects
        internal Buffer _vertexBuffer;
        internal DeviceMemory _vertexBufferMemory;

        internal Buffer _indexBuffer;
        internal DeviceMemory _indexBufferMemory;

        internal Buffer _trasnformsBuffer;
        internal DeviceMemory _trasnformsBufferMemory;

        internal Silk.NET.Vulkan.Image _textureImage;
        internal ImageView _textureImageView;
        internal DeviceMemory _textureBufferMemory;


        int _instances = 1;
        internal List<Matrix4X4<float>> _transformMatrices = new List<Matrix4X4<float>>();

        public AVulkanMeshComponent()
        {
            if (_mesh != null)
            {
                VulkanRenderer._bufferHandlerHelper.CreateVertexBuffer(ref _mesh._vertices, ref _vertexBuffer, ref _vertexBufferMemory);
                VulkanRenderer._bufferHandlerHelper.CreateIndexBuffer(ref _mesh._indices, ref _indexBuffer, ref _indexBufferMemory);
                SingletonMatrix();
                VulkanRenderer._bufferHandlerHelper.CreateTextureBuffer(ref _textureImage, ref _textureBufferMemory);
                VulkanRenderer._bufferHandlerHelper.CreateImageView(ref _textureImage, ref _textureImageView);
                
                CreateDescriptorSet();
                CreateShadowDescriptorSet();
            }
        }

        public override void OnStart()
        {
            VulkanRenderer._rendererInstance.AddEntityToRenderQueue(parent);
            VulkanRenderer._rendererInstance.RecreateCommandBuffers();
        }

        internal void MakeInstanced(ref List<Matrix4X4<float>> _matrices)
        {
            _instances = _matrices.Count;
            _transformMatrices = _matrices;

            VulkanRenderer._bufferHandlerHelper.CreateTransformBuffer(ref _transformMatrices, ref _trasnformsBuffer, ref _trasnformsBufferMemory);
            VulkanRenderer._vulkan.FreeDescriptorSets(VulkanRenderer._logicalDevice, VulkanRenderer._descriptorPool, (uint)_descriptorSets.Length, _descriptorSets);
            VulkanRenderer._vulkan.FreeDescriptorSets(VulkanRenderer._logicalDevice, VulkanRenderer._descriptorPoolShadow, (uint)_descriptorSetsShadow.Length, _descriptorSetsShadow);
            CreateDescriptorSet();
            CreateShadowDescriptorSet();
            VulkanRenderer._rendererInstance.RecreateCommandBuffers();
        }

        internal void SingletonMatrix()
        {
            Vector3D<float> _pos = new Vector3D<float>(0, 0, 0);
            Quaternion<float> q = Quaternion<float>.CreateFromYawPitchRoll(0,0,0);

            Matrix4X4<float> _transform = Matrix4X4<float>.Identity;
            //_transform *= Matrix4X4.CreateScale(new Vector3D<float>(1,1,1));
            //_transform *= Matrix4X4.CreateFromQuaternion(q);
            //_transform *= Matrix4X4.CreateTranslation(_pos);

            _transformMatrices.Add(_transform);
            VulkanRenderer._bufferHandlerHelper.CreateTransformBuffer(ref _transformMatrices, ref _trasnformsBuffer, ref _trasnformsBufferMemory);
        }

        internal void CreateDescriptorSet()
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
                    Buffer = VulkanRenderer._camera._cameraBuffer[i],
                    Offset = 0,
                    Range = (ulong)Unsafe.SizeOf<UBO>()
                };

                DescriptorBufferInfo _bufferInfoMatrices = new DescriptorBufferInfo()
                {
                    Buffer = _trasnformsBuffer,
                    Offset = 0,
                    Range = Vk.WholeSize
                };

                DescriptorBufferInfo _lightBufferUniform = new DescriptorBufferInfo()
                {
                    Buffer = VulkanRenderer._lightBuffer,
                    Offset = 0,
                    Range = Vk.WholeSize
                };

                DescriptorImageInfo _imageInfo = new DescriptorImageInfo()
                {
                    ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
                    ImageView = _textureImageView,
                    Sampler = VulkanRenderer._textureSampler
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
                    },
                    new WriteDescriptorSet()
                    {
                        SType = StructureType.WriteDescriptorSet,
                        DstSet = _descriptorSets[i],
                        DstBinding = 2,
                        DstArrayElement = 0,
                        DescriptorType = DescriptorType.StorageBuffer,
                        DescriptorCount = 1,
                        PBufferInfo = &_lightBufferUniform
                    },
                    new WriteDescriptorSet()
                    {
                        SType = StructureType.WriteDescriptorSet,
                        DstSet = _descriptorSets[i],
                        DstBinding = 3,
                        DstArrayElement = 0,
                        DescriptorType = DescriptorType.CombinedImageSampler,
                        DescriptorCount = 1,
                        PImageInfo = &_imageInfo
                    }
                };
                fixed (WriteDescriptorSet* _descPtr = _writeDescriptorSets)
                {
                    VulkanRenderer._vulkan!.UpdateDescriptorSets(VulkanRenderer._logicalDevice, (uint)_writeDescriptorSets.Length, _descPtr, 0, null);
                }
            }
        }

        internal void CreateShadowDescriptorSet()
        {
            DescriptorSetLayout[] _layouts = new DescriptorSetLayout[VulkanRenderer._swapchain!._swapchainImages.Length];
            Array.Fill(_layouts, VulkanRenderer._descriptorSetLayoutShadow);

            fixed (DescriptorSetLayout* _layoutsPtr = _layouts)
            {
                DescriptorSetAllocateInfo _allocateInfo = new DescriptorSetAllocateInfo()
                {
                    SType = StructureType.DescriptorSetAllocateInfo,
                    DescriptorPool = VulkanRenderer._descriptorPoolShadow,
                    DescriptorSetCount = (uint)VulkanRenderer._swapchain!._swapchainImages.Length,
                    PSetLayouts = _layoutsPtr
                };

                _descriptorSetsShadow = new DescriptorSet[VulkanRenderer._swapchain!._swapchainImages.Length];
                fixed (DescriptorSet* _descriptorSetsPtr = _descriptorSetsShadow)
                {
                    Result r = VulkanRenderer._vulkan.AllocateDescriptorSets(VulkanRenderer._logicalDevice, _allocateInfo, _descriptorSetsPtr);
                    if (r != Result.Success)
                    {
                        throw new Exception("Failed to allocate descriptor set with error code: " + r);
                    }
                }
            }
            for (int i = 0; i < VulkanRenderer._swapchain._swapchainImages.Length; i++)
            {
                DescriptorBufferInfo _bufferInfoUniform = new DescriptorBufferInfo()
                {
                    Buffer = VulkanRenderer._lightUBO[i],
                    Offset = 0,
                    Range = Vk.WholeSize
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
                        DstSet = _descriptorSetsShadow[i],
                        DstBinding = 0,
                        DstArrayElement = 0,
                        DescriptorType = DescriptorType.StorageBuffer,
                        DescriptorCount = 1,
                        PBufferInfo = &_bufferInfoUniform
                    },
                    new WriteDescriptorSet()
                    {
                        SType = StructureType.WriteDescriptorSet,
                        DstSet = _descriptorSetsShadow[i],
                        DstBinding = 1,
                        DstArrayElement = 0,
                        DescriptorType = DescriptorType.StorageBuffer,
                        DescriptorCount = 1,
                        PBufferInfo = &_bufferInfoMatrices
                    }
                };
                fixed (WriteDescriptorSet* _descPtr = _writeDescriptorSets)
                {
                    VulkanRenderer._vulkan!.UpdateDescriptorSets(VulkanRenderer._logicalDevice, (uint)_writeDescriptorSets.Length, _descPtr, 0, null);
                }
            }
        }

        internal void UpdateMatrices()
        {

            /*Vector3D<float> _pos = new Vector3D<float>(0, 0, 0);

            Matrix4X4<float> _transform = Matrix4X4<float>.Identity;
            _transform *= Matrix4X4.CreateScale(new Vector3D<float>(2, 2, 2));
            //_transform *= Matrix4X4.CreateTranslation(_pos);

            _transformMatrices[0] = _transform;*/
            VulkanRenderer._bufferHandlerHelper.UpdateTransformBuffer(ref _transformMatrices, ref _trasnformsBufferMemory);
        }

        internal void EnqueueDrawCommands(ulong[] _offset, int _loopIndex, ref CommandBuffer _commandBuffer)
        {
            if (_render)
            {
                Buffer[] _vertBuffer = new Buffer[] { _vertexBuffer };
                fixed (ulong* _offsetsPtr = _offset)
                fixed (Buffer* _vertBuffersPtr = _vertBuffer)
                {
                    VulkanRenderer._vulkan.CmdBindVertexBuffers(_commandBuffer, 0, 1, _vertBuffersPtr, _offsetsPtr);
                }
                VulkanRenderer._vulkan.CmdBindIndexBuffer(_commandBuffer, _indexBuffer, 0, IndexType.Uint16);
                VulkanRenderer._vulkan.CmdBindDescriptorSets(_commandBuffer, PipelineBindPoint.Graphics, VulkanRenderer._pipeline._pipelineLayout, 0, 1, _descriptorSets[_loopIndex], 0, null);
                VulkanRenderer._vulkan.CmdDrawIndexed(_commandBuffer, (uint)_mesh._indices.Length, (uint)_instances, 0, 0, 0);
            }
        }

        internal void EnqueuShadowDrawCommands(ulong[] _offset, int descriptorIndex, ref CommandBuffer _commandBuffer, int lightIndex)
        {
            if (_render)
            {
                Buffer[] _vertBuffer = new Buffer[] { _vertexBuffer };
                fixed (ulong* _offsetsPtr = _offset)
                fixed (Buffer* _vertBuffersPtr = _vertBuffer)
                {
                    VulkanRenderer._vulkan.CmdBindVertexBuffers(_commandBuffer, 0, 1, _vertBuffersPtr, _offsetsPtr);
                }
                VulkanRenderer._vulkan.CmdBindIndexBuffer(_commandBuffer, _indexBuffer, 0, IndexType.Uint16);
                VulkanRenderer._vulkan.CmdBindDescriptorSets(_commandBuffer, PipelineBindPoint.Graphics, VulkanRenderer._pipeline._shadowLayout, 0, 1, _descriptorSetsShadow[descriptorIndex], 0, null);
                //push constants
                VulkanRenderer._vulkan.CmdPushConstants(_commandBuffer, VulkanRenderer._pipeline._shadowLayout, ShaderStageFlags.VertexBit, 0, sizeof(int), &lightIndex);
                VulkanRenderer._vulkan.CmdDrawIndexed(_commandBuffer, (uint)_mesh._indices.Length, (uint)_instances, 0, 0, 0);
            }
        }
    }
}