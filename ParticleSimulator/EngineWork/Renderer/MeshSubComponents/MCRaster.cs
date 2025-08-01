using ArctisAurora.EngineWork.ECS.RenderingComponents.Vulkan;
using ArctisAurora.EngineWork.Renderer.Helpers;
using ArctisAurora.EngineWork.Renderer.RendererTypes;
using Assimp;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using System.Runtime.CompilerServices;
using Buffer = Silk.NET.Vulkan.Buffer;
using ImageLayout = Silk.NET.Vulkan.ImageLayout;

namespace ArctisAurora.EngineWork.Renderer.MeshSubComponents
{
    internal unsafe class MCRaster : MeshComponent
    {
        //shadow map descriptor set
        //internal DescriptorSet[] _descriptorSetsShadow;
        //texture image
        internal Silk.NET.Vulkan.Image _textureImage;
        internal ImageView _textureImageView;
        internal DeviceMemory _textureBufferMemory;

        internal MCRaster()
        {
            //AVulkanBufferHandler.CreateBuffer(ref _mesh._vertices, ref _vertexBuffer, ref _vertexBufferMemory, AVulkanBufferHandler.vertexBufferFlags | _aditionalUsageFlags);
            //AVulkanBufferHandler.CreateBuffer(ref _mesh._indices, ref _indexBuffer, ref _indexBufferMemory, AVulkanBufferHandler.indexBufferFlags | _aditionalUsageFlags);
            _mesh = new AVulkanMesh();
            _mesh.BufferMesh();
            AVulkanBufferHandler.CreateTextureBuffer(ref _textureImage, ref _textureBufferMemory, "../../../Shaders/Brick2.png", Format.R8G8B8A8Srgb);
            AVulkanBufferHandler.CreateImageView(ref _textureImage, ref _textureImageView, Format.R8G8B8A8Srgb);
        }

        public override void OnStart()
        {
            base.OnStart();
            CreateDescriptorSet();
        }

        internal override void LoadCustomMesh(Scene sc)
        {
            base.LoadCustomMesh(sc);
        }

        internal override void FreeDescriptorSets()
        {
            base.FreeDescriptorSets();
            //if (_descriptorSets != null)
            //    VulkanRenderer._vulkan.FreeDescriptorSets(VulkanRenderer._logicalDevice, Rasterizer._descriptorPool, (uint)_descriptorSets.Length, _descriptorSets);
            //if (_descriptorSetsShadow != null)
            //    VulkanRenderer._vulkan.FreeDescriptorSets(VulkanRenderer._logicalDevice, Rasterizer._descriptorPoolShadow, (uint)_descriptorSetsShadow.Length, _descriptorSetsShadow);
        }

        internal override void ReinstantiateDesriptorSets()
        {
            base.ReinstantiateDesriptorSets();
            CreateDescriptorSet();
        }

        internal override void CreateDescriptorSet()
        {
            CreateRasterDescriptorSet();
            CreateShadowDescriptorSet();
        }

        private void CreateRasterDescriptorSet()
        {
            /*DescriptorSetLayout[] _layouts = new DescriptorSetLayout[VulkanRenderer._swapimageCount];
            Array.Fill(_layouts, Rasterizer._descriptorSetLayout);

            fixed (DescriptorSetLayout* _layoutsPtr = _layouts)
            {
                DescriptorSetAllocateInfo _allocateInfo = new DescriptorSetAllocateInfo()
                {
                    SType = StructureType.DescriptorSetAllocateInfo,
                    DescriptorPool = VulkanRenderer._descriptorPool,
                    DescriptorSetCount = (uint)VulkanRenderer._swapimageCount,
                    PSetLayouts = _layoutsPtr
                };

                _descriptorSets = new DescriptorSet[VulkanRenderer._swapimageCount];
                fixed (DescriptorSet* _descriptorSetsPtr = _descriptorSets)
                {
                    Result r = VulkanRenderer._vulkan.AllocateDescriptorSets(VulkanRenderer._logicalDevice, _allocateInfo, _descriptorSetsPtr);
                    if (r != Result.Success)
                    {
                        throw new Exception("Failed to allocate descriptor set with error code: " + r);
                    }
                }
            }

            for (int i = 0; i < VulkanRenderer._swapimageCount; i++)
            {
                DescriptorBufferInfo _bufferInfoUniform = new DescriptorBufferInfo()
                {
                    Buffer = VulkanRenderer._camera._cameraBuffer[i],
                    Offset = 0,
                    Range = (ulong)Unsafe.SizeOf<UBO>()
                };

                DescriptorBufferInfo _bufferInfoMatrices = new DescriptorBufferInfo()
                {
                    Buffer = _transformsBuffer,
                    Offset = 0,
                    Range = Vk.WholeSize
                };

                DescriptorBufferInfo _lightBufferUniform = new DescriptorBufferInfo()
                {
                    Buffer = Rasterizer._lightBuffer,
                    Offset = 0,
                    Range = (ulong)(sizeof(LightData) * VulkanRenderer._lightsToRender.Count + sizeof(int))
                };

                DescriptorImageInfo _imageInfo = new DescriptorImageInfo()
                {
                    ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
                    ImageView = _textureImageView,
                    Sampler = Rasterizer._textureSampler
                };

                DescriptorImageInfo _shadowmapInfo = new DescriptorImageInfo()
                {
                    ImageLayout = ImageLayout.DepthStencilReadOnlyOptimal,
                    ImageView = VulkanRenderer._lightsToRender[0].GetComponent<LightsourceComponent>()._depthImageView,
                    Sampler = Rasterizer._shadowmapSampler
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
                    },
                    new WriteDescriptorSet()
                    {
                        SType = StructureType.WriteDescriptorSet,
                        DstSet = _descriptorSets[i],
                        DstBinding = 4,
                        DstArrayElement = 0,
                        DescriptorType = DescriptorType.CombinedImageSampler,
                        DescriptorCount = 1,
                        PImageInfo = &_shadowmapInfo
                    }
                };
                fixed (WriteDescriptorSet* _descPtr = _writeDescriptorSets)
                {
                    VulkanRenderer._vulkan!.UpdateDescriptorSets(VulkanRenderer._logicalDevice, (uint)_writeDescriptorSets.Length, _descPtr, 0, null);
                }
            }*/
        }

        private void CreateShadowDescriptorSet()
        {
            /*DescriptorSetLayout[] _layouts = new DescriptorSetLayout[Rasterizer._swapchain!._swapchainImages.Length];
            Array.Fill(_layouts, Rasterizer._descriptorSetLayoutShadow);

            fixed (DescriptorSetLayout* _layoutsPtr = _layouts)
            {
                DescriptorSetAllocateInfo _allocateInfo = new DescriptorSetAllocateInfo()
                {
                    SType = StructureType.DescriptorSetAllocateInfo,
                    DescriptorPool = Rasterizer._descriptorPoolShadow,
                    DescriptorSetCount = (uint)Rasterizer._swapchain!._swapchainImages.Length,
                    PSetLayouts = _layoutsPtr
                };

                _descriptorSetsShadow = new DescriptorSet[Rasterizer._swapchain!._swapchainImages.Length];
                fixed (DescriptorSet* _descriptorSetsPtr = _descriptorSetsShadow)
                {
                    Result r = Rasterizer._vulkan.AllocateDescriptorSets(Rasterizer._logicalDevice, _allocateInfo, _descriptorSetsPtr);
                    if (r != Result.Success)
                    {
                        throw new Exception("Failed to allocate descriptor set with error code: " + r);
                    }
                }
            }
            for (int i = 0; i < Rasterizer._swapchain._swapchainImages.Length; i++)
            {
                DescriptorBufferInfo _bufferInfoUniform = new DescriptorBufferInfo()
                {
                    Buffer = Rasterizer._lightUBO[i],
                    Offset = 0,
                    Range = Vk.WholeSize
                };

                DescriptorBufferInfo _bufferInfoMatrices = new DescriptorBufferInfo()
                {
                    Buffer = _transformsBuffer,
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
            }*/
        }

        internal override void MakeInstanced(ref List<Matrix4X4<float>> _matrices)
        {
            base.MakeInstanced(ref _matrices);
            _instances = _matrices.Count;
            _transformMatrices = _matrices;

            Matrix4X4<float>[] _mats = _matrices.ToArray();
            AVulkanBufferHandler.CreateBuffer(ref _mats, ref _transformsBuffer, ref _transformsBufferMemory, BufferUsageFlags.StorageBufferBit);
            //VulkanRenderer._vulkan.FreeDescriptorSets(VulkanRenderer._logicalDevice, Rasterizer._descriptorPool, (uint)_descriptorSets.Length, _descriptorSets);
            //VulkanRenderer._vulkan.FreeDescriptorSets(VulkanRenderer._logicalDevice, Rasterizer._descriptorPoolShadow, (uint)_descriptorSetsShadow.Length, _descriptorSetsShadow);
            //CreateDescriptorSet();
            VulkanRenderer._rendererInstance.RecreateCommandBuffers();
        }

        internal override void SingletonMatrix()
        {
            base.SingletonMatrix();

            Matrix4X4<float>[] _mats = _transformMatrices.ToArray();
            AVulkanBufferHandler.CreateBuffer(ref _mats, ref _transformsBuffer, ref _transformsBufferMemory, BufferUsageFlags.StorageBufferBit);
        }

        internal override void UpdateMatrices()
        {
            base.UpdateMatrices();
        }

        internal override void EnqueueDrawCommands(ref ulong[] _offset, int _loopIndex, ref CommandBuffer _commandBuffer)
        {
            base.EnqueueDrawCommands(ref _offset, _loopIndex, ref _commandBuffer);
        }

        internal void EnqueuShadowDrawCommands(ulong[] _offset, int descriptorIndex, ref CommandBuffer _commandBuffer, int lightIndex)
        {
            if (_render)
            {
                Buffer[] _vertBuffer = new Buffer[] { _mesh._vertexBuffer };
                fixed (ulong* _offsetsPtr = _offset)
                fixed (Buffer* _vertBuffersPtr = _vertBuffer)
                {
                    VulkanRenderer._vulkan.CmdBindVertexBuffers(_commandBuffer, 0, 1, _vertBuffersPtr, _offsetsPtr);
                }
                VulkanRenderer._vulkan.CmdBindIndexBuffer(_commandBuffer, _mesh._indexBuffer, 0, IndexType.Uint32);
                //VulkanRenderer._vulkan.CmdBindDescriptorSets(_commandBuffer, PipelineBindPoint.Graphics, Rasterizer._pipeline._shadowLayout, 0, 1, _descriptorSetsShadow[descriptorIndex], 0, null);
                //push constants
                VulkanRenderer._vulkan.CmdPushConstants(_commandBuffer, Rasterizer._pipeline._shadowLayout, ShaderStageFlags.VertexBit, 0, sizeof(int), &lightIndex);
                VulkanRenderer._vulkan.CmdDrawIndexed(_commandBuffer, (uint)_mesh._indices.Length, (uint)_instances, 0, 0, 0);
            }
        }
    }
}