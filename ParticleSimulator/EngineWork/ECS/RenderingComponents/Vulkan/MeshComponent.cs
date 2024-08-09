using ArctisAurora.EngineWork.ComponentBehaviour;
using ArctisAurora.EngineWork.Renderer;
using ArctisAurora.EngineWork.Renderer.Helpers;
using Assimp;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using System.Runtime.CompilerServices;
using Buffer = Silk.NET.Vulkan.Buffer;
using ImageLayout = Silk.NET.Vulkan.ImageLayout;

namespace ArctisAurora.EngineWork.ECS.RenderingComponents.Vulkan
{
    internal unsafe class MeshComponent : EntityComponent
    {
        bool _render = true;
        internal AVulkanMesh _mesh = new AVulkanMesh();

        //Descriptor set
        internal DescriptorSet[] _descriptorSets;
        internal DescriptorSet[] _descriptorSetsShadow;
        internal DescriptorSet[] _descriptorSetsPathTracing;

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

        public MeshComponent()
        {
            if (_mesh != null)
            {
                AVulkanBufferHandler.CreateVertexBuffer(ref _mesh._vertices, ref _vertexBuffer, ref _vertexBufferMemory);
                AVulkanBufferHandler.CreateIndexBuffer(ref _mesh._indices, ref _indexBuffer, ref _indexBufferMemory);
                AVulkanBufferHandler.CreateTextureBuffer(ref _textureImage, ref _textureBufferMemory);
                AVulkanBufferHandler.CreateImageView(ref _textureImage, ref _textureImageView);
            }
        }

        public override void OnStart()
        {
            SingletonMatrix();
            VulkanRenderer._rendererInstance.AddEntityToRenderQueue(parent);
            CreateDescriptorSet();
            CreateShadowDescriptorSet();
        }

        internal void LoadCustomMesh(Scene sc)
        {
            VulkanRenderer._vulkan.DestroyBuffer(VulkanRenderer._logicalDevice, _vertexBuffer, null);
            VulkanRenderer._vulkan.DestroyBuffer(VulkanRenderer._logicalDevice, _indexBuffer, null);
            VulkanRenderer._vulkan.FreeMemory(VulkanRenderer._logicalDevice, _indexBufferMemory, null);
            VulkanRenderer._vulkan.FreeMemory(VulkanRenderer._logicalDevice, _vertexBufferMemory, null);
            _mesh.LoadCustomMesh(sc);
            AVulkanBufferHandler.CreateVertexBuffer(ref _mesh._vertices, ref _vertexBuffer, ref _vertexBufferMemory);
            AVulkanBufferHandler.CreateIndexBuffer(ref _mesh._indices, ref _indexBuffer, ref _indexBufferMemory);
            ((IRecreateCommandBuffer)VulkanRenderer._rendererInstance).RecreateCommandBuffers();
        }

        internal void FreeDescriptorSets()
        {
            if (_descriptorSets != null)
                VulkanRenderer._vulkan.FreeDescriptorSets(VulkanRenderer._logicalDevice, Rasterizer._descriptorPool, (uint)_descriptorSets.Length, _descriptorSets);
            if (_descriptorSetsShadow != null)
                VulkanRenderer._vulkan.FreeDescriptorSets(VulkanRenderer._logicalDevice, Rasterizer._descriptorPoolShadow, (uint)_descriptorSetsShadow.Length, _descriptorSetsShadow);
        }

        internal void ReinstantiateDesriptorSets()
        {
            CreateDescriptorSet();
            CreateShadowDescriptorSet();
        }

        internal void MakeInstanced(ref List<Matrix4X4<float>> _matrices)
        {
            _instances = _matrices.Count;
            _transformMatrices = _matrices;

            AVulkanBufferHandler.CreateTransformBuffer(ref _transformMatrices, ref _trasnformsBuffer, ref _trasnformsBufferMemory);
            VulkanRenderer._vulkan.FreeDescriptorSets(VulkanRenderer._logicalDevice, Rasterizer._descriptorPool, (uint)_descriptorSets.Length, _descriptorSets);
            VulkanRenderer._vulkan.FreeDescriptorSets(VulkanRenderer._logicalDevice, Rasterizer._descriptorPoolShadow, (uint)_descriptorSetsShadow.Length, _descriptorSetsShadow);
            CreateDescriptorSet();
            CreateShadowDescriptorSet();
            ((IRecreateCommandBuffer)VulkanRenderer._rendererInstance).RecreateCommandBuffers();
        }

        internal void SingletonMatrix()
        {
            Quaternion<float> q = Quaternion<float>.CreateFromYawPitchRoll(0,0,0);
            Matrix4X4<float> _transform = Matrix4X4<float>.Identity;
            _transform *= Matrix4X4.CreateTranslation(parent.transform.position);
            //_transform *= Matrix4X4.CreateFromQuaternion(q);
            _transform *= Matrix4X4.CreateScale(parent.transform.scale);

            _transformMatrices.Add(_transform);
            AVulkanBufferHandler.CreateTransformBuffer(ref _transformMatrices, ref _trasnformsBuffer, ref _trasnformsBufferMemory);
        }

        internal void CreateDescriptorSet()
        {
            DescriptorSetLayout[] _layouts = new DescriptorSetLayout[VulkanRenderer._swapimageCount];
            Array.Fill(_layouts, Rasterizer._descriptorSetLayout);

            fixed (DescriptorSetLayout* _layoutsPtr = _layouts)
            {
                DescriptorSetAllocateInfo _allocateInfo = new DescriptorSetAllocateInfo()
                {
                    SType = StructureType.DescriptorSetAllocateInfo,
                    DescriptorPool = Rasterizer._descriptorPool,
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
                    Buffer = Rasterizer._camera._cameraBuffer[i],
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
                    Buffer = Rasterizer._lightBuffer,
                    Offset = 0,
                    Range = (ulong)(sizeof(LightData) * Rasterizer._lightsToRender.Count + sizeof(int))
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
                    ImageView = Rasterizer._lightsToRender[0].GetComponent<LightsourceComponent>()._depthImageView,
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
                    Rasterizer._vulkan!.UpdateDescriptorSets(Rasterizer._logicalDevice, (uint)_writeDescriptorSets.Length, _descPtr, 0, null);
                }
            }
        }

        internal void CreateShadowDescriptorSet()
        {
            DescriptorSetLayout[] _layouts = new DescriptorSetLayout[Rasterizer._swapchain!._swapchainImages.Length];
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
                    Rasterizer._vulkan!.UpdateDescriptorSets(Rasterizer._logicalDevice, (uint)_writeDescriptorSets.Length, _descPtr, 0, null);
                }
            }
        }

        internal void CreateRTDescriptorSet()
        {
            DescriptorSetLayout[] _layouts = new DescriptorSetLayout[Pathtracing._swapchain!._swapchainImages.Length];
            Array.Fill(_layouts, Pathtracing._descriptorSetLayout);

            fixed (DescriptorSetLayout* _layoutsPtr = _layouts)
            {
                DescriptorSetAllocateInfo _allocateInfo = new DescriptorSetAllocateInfo()
                {
                    SType = StructureType.DescriptorSetAllocateInfo,
                    DescriptorPool = Pathtracing._descriptorPool,
                    DescriptorSetCount = (uint)VulkanRenderer._swapimageCount,
                    PSetLayouts = _layoutsPtr
                };

                _descriptorSetsPathTracing = new DescriptorSet[Pathtracing._swapchain!._swapchainImages.Length];
                fixed (DescriptorSet* _descriptorSetsPtr = _descriptorSetsPathTracing)
                {
                    Result r = VulkanRenderer._vulkan.AllocateDescriptorSets(VulkanRenderer._logicalDevice, _allocateInfo, _descriptorSetsPtr);
                    if (r != Result.Success)
                    {
                        throw new Exception("Failed to allocate descriptor set with error code: " + r);
                    }
                }
            }
            for (int i = 0; i < Pathtracing._swapchain!._swapchainImages.Length; i++)
            {
                fixed (AccelerationStructureKHR* _accelStrPtr = &Pathtracing._TLAS._handle)
                {
                    WriteDescriptorSetAccelerationStructureKHR _dasinfo = new()
                    {
                        SType = StructureType.WriteDescriptorSetAccelerationStructureKhr,
                        AccelerationStructureCount = 1,
                        PAccelerationStructures = _accelStrPtr,
                    };
                    DescriptorImageInfo _dImageInfo = new()
                    {
                        ImageLayout = ImageLayout.General,
                        ImageView = Pathtracing._storageImageView[i]
                    };
                    DescriptorBufferInfo _bufferInfoMatrices = new DescriptorBufferInfo()
                    {
                        Buffer = VulkanRenderer._camera._cameraBuffer[i],
                        Offset = 0,
                        Range = (ulong)Unsafe.SizeOf<UBO>()
                    };

                    var _writeDescriptorSets = new WriteDescriptorSet[]
                    {
                        new WriteDescriptorSet
                        {
                            SType = StructureType.WriteDescriptorSet,
                            PNext = &_dasinfo,
                            DstSet = _descriptorSetsPathTracing[i],
                            DstBinding = 0,
                            DstArrayElement = 0,
                            DescriptorCount = 1,
                            DescriptorType = DescriptorType.AccelerationStructureKhr
                        },
                        new WriteDescriptorSet
                        {
                            SType = StructureType.WriteDescriptorSet,
                            DstSet = _descriptorSetsPathTracing[i],
                            DstBinding = 1,
                            DescriptorCount = 1,
                            DstArrayElement = 0,
                            DescriptorType = DescriptorType.StorageImage,
                            PImageInfo = &_dImageInfo
                        },
                        new WriteDescriptorSet
                        {
                            SType = StructureType.WriteDescriptorSet,
                            DstSet = _descriptorSetsPathTracing[i],
                            DstBinding = 2,
                            DescriptorCount = 1,
                            DstArrayElement = 0,
                            DescriptorType = DescriptorType.UniformBuffer,
                            PBufferInfo = &_bufferInfoMatrices
                        }
                    };
                    fixed (WriteDescriptorSet* _descPtr = _writeDescriptorSets)
                    {
                        VulkanRenderer._vulkan!.UpdateDescriptorSets(VulkanRenderer._logicalDevice, (uint)_writeDescriptorSets.Length, _descPtr, 0, null);
                    }
                }
            }
        }

        internal void UpdateMatrices()
        {
            Quaternion<float> q = Quaternion<float>.CreateFromYawPitchRoll(30f * MathF.PI / 180f, 0, 0);
            Matrix4X4<float> _transform = Matrix4X4<float>.Identity;
            _transform *= Matrix4X4.CreateScale(parent.transform.scale);
            _transform *= Matrix4X4.CreateFromQuaternion(q);
            _transform *= Matrix4X4.CreateTranslation(parent.transform.position);

            _transformMatrices[0] = _transform;
            AVulkanBufferHandler.UpdateTransformBuffer(ref _transformMatrices, ref _trasnformsBufferMemory);
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
                VulkanRenderer._vulkan.CmdBindDescriptorSets(_commandBuffer, PipelineBindPoint.Graphics, Rasterizer._pipeline._pipelineLayout, 0, 1, _descriptorSets[_loopIndex], 0, null);
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
                    Rasterizer._vulkan.CmdBindVertexBuffers(_commandBuffer, 0, 1, _vertBuffersPtr, _offsetsPtr);
                }
                Rasterizer._vulkan.CmdBindIndexBuffer(_commandBuffer, _indexBuffer, 0, IndexType.Uint16);
                Rasterizer._vulkan.CmdBindDescriptorSets(_commandBuffer, PipelineBindPoint.Graphics, Rasterizer._pipeline._shadowLayout, 0, 1, _descriptorSetsShadow[descriptorIndex], 0, null);
                //push constants
                Rasterizer._vulkan.CmdPushConstants(_commandBuffer, Rasterizer._pipeline._shadowLayout, ShaderStageFlags.VertexBit, 0, sizeof(int), &lightIndex);
                Rasterizer._vulkan.CmdDrawIndexed(_commandBuffer, (uint)_mesh._indices.Length, (uint)_instances, 0, 0, 0);
            }
        }
    }
}