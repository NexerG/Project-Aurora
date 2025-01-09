using ArctisAurora.EngineWork.ECS.RenderingComponents.Vulkan;
using ArctisAurora.EngineWork.Renderer.Helpers;
using ArctisAurora.EngineWork.Renderer.RendererTypes;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using System.Drawing.Drawing2D;
using System.Runtime.CompilerServices;
using static ArctisAurora.EngineWork.Renderer.RendererTypes.Pathtracing;
using static ArctisAurora.EngineWork.Renderer.VulkanRenderer;
using Buffer = Silk.NET.Vulkan.Buffer;
using ImageLayout = Silk.NET.Vulkan.ImageLayout;

namespace ArctisAurora.EngineWork.Renderer.MeshSubComponents
{
    internal unsafe class MCRaytracing : MeshComponent
    {
        public struct AccelerationStruct
        {
            public AccelerationStructureKHR _handle;
            public ulong _deviceAddress;
            public DeviceMemory _memory;
            public Buffer _buffer;
        }
        public struct PathtracingScratchBuffer
        {
            public ulong _deviceAddress;
            public Buffer _buffer;
            public DeviceMemory _memory;
        }
        //
        DeviceOrHostAddressConstKHR _addressVertex = default;
        DeviceOrHostAddressConstKHR _addressIndex = default;
        DeviceOrHostAddressConstKHR _addressTransform = default;
        //
        internal AccelerationStruct _BLAS = default;
        // data for building a TLAS
        internal AccelerationStructureInstanceKHR _accelerationInstance;
        /*internal Buffer _accInstanceTransformBuffer;
        internal DeviceMemory _accInstanceDM;*/

        internal MCRaytracing()
        {
            _aditionalUsageFlags = BufferUsageFlags.AccelerationStructureBuildInputReadOnlyBitKhr | BufferUsageFlags.ShaderDeviceAddressBit | BufferUsageFlags.StorageBufferBit;
            AVulkanBufferHandler.CreateVertexBuffer(ref _mesh._vertices, ref _vertexBuffer, ref _vertexBufferMemory, _aditionalUsageFlags);
            AVulkanBufferHandler.CreateIndexBuffer(ref _mesh._indices, ref _indexBuffer, ref _indexBufferMemory, _aditionalUsageFlags);
        }

        public override void OnStart()
        {
            SingletonMatrix();
            CreateBLAS();
            _rendererInstance.AddEntityToRenderQueue(parent);
            CreateDescriptorSet();
        }

        private void CreateBLAS()
        {
            TransformMatrixKHR _entityVulkanTransform = new TransformMatrixKHR();
            Unsafe.CopyBlock(_entityVulkanTransform.Matrix, Unsafe.AsPointer(ref _transformMatrices.ToArray()[0]), 48);

            AVulkanBufferHandler.CreateBuffer(
                (ulong)sizeof(TransformMatrixKHR),
                BufferUsageFlags.ShaderDeviceAddressBit | BufferUsageFlags.AccelerationStructureBuildInputReadOnlyBitKhr,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
                ref _transformsBuffer, ref _transformsBufferMemory);
            void* _data;
            _vulkan.MapMemory(_logicalDevice, _transformsBufferMemory, 0, (ulong)sizeof(TransformMatrixKHR), 0, &_data);
            new Span<TransformMatrixKHR>(_data, 1)[0] = _entityVulkanTransform;
            _vulkan.UnmapMemory(_logicalDevice, _transformsBufferMemory);

            _addressVertex.DeviceAddress = AVulkanHelper.GetBufferAdress(ref _vertexBuffer);
            _addressIndex.DeviceAddress = AVulkanHelper.GetBufferAdress(ref _indexBuffer);
            _addressTransform.DeviceAddress = AVulkanHelper.GetBufferAdress(ref _transformsBuffer);

            AccelerationStructureGeometryKHR _accelStrGeom = new AccelerationStructureGeometryKHR
            {
                SType = StructureType.AccelerationStructureGeometryKhr,
                Flags = GeometryFlagsKHR.OpaqueBitKhr,
                GeometryType = GeometryTypeKHR.TrianglesKhr,
                Geometry =
                {
                    Triangles =
                    {
                        SType = StructureType.AccelerationStructureGeometryTrianglesDataKhr,
                        VertexFormat = Format.R32G32B32Sfloat,
                        VertexData = _addressVertex,
                        MaxVertex = (uint)(_mesh._vertices.Length),
                        VertexStride = (ulong)sizeof(Vertex),
                        IndexType = IndexType.Uint32,
                        IndexData = _addressIndex,
                        TransformData = _addressTransform,
                        PNext = null
                    }
                },
                PNext = null,
            };

            AccelerationStructureBuildGeometryInfoKHR _accelStrGeomInfo = new()
            {
                SType = StructureType.AccelerationStructureBuildGeometryInfoKhr,
                Mode = BuildAccelerationStructureModeKHR.BuildKhr,
                Type = AccelerationStructureTypeKHR.BottomLevelKhr,
                Flags = BuildAccelerationStructureFlagsKHR.PreferFastTraceBitKhr | BuildAccelerationStructureFlagsKHR.AllowUpdateBitKhr,
                GeometryCount = 1,
                PGeometries = &_accelStrGeom,
                SrcAccelerationStructure = default,
            };

            uint numTris = (uint)(_mesh._indices.Length / 3);
            AccelerationStructureBuildSizesInfoKHR _asbsInfo = default;
            _asbsInfo.SType = StructureType.AccelerationStructureBuildSizesInfoKhr;
            _accelerationStructure.GetAccelerationStructureBuildSizes(_logicalDevice, AccelerationStructureBuildTypeKHR.DeviceKhr, &_accelStrGeomInfo, numTris, out _asbsInfo);
            CreateAccelerationStructureBuffer(ref _BLAS, ref _asbsInfo);

            AccelerationStructureCreateInfoKHR _accelerationStructureCreateInfo = new AccelerationStructureCreateInfoKHR()
            {
                SType = StructureType.AccelerationStructureCreateInfoKhr,
                Buffer = _BLAS._buffer,
                Size = _asbsInfo.AccelerationStructureSize,
                Type = AccelerationStructureTypeKHR.BottomLevelKhr
            };
            Result r = _accelerationStructure.CreateAccelerationStructure(_logicalDevice, ref _accelerationStructureCreateInfo, null, out _BLAS._handle);
            if (r != Result.Success)
            {
                throw new Exception("failed to create BLAS on the host");
            }
            PathtracingScratchBuffer _scratchBuffer = new PathtracingScratchBuffer();
            CreateScratchBuffer(_asbsInfo.BuildScratchSize, ref _scratchBuffer);

            AccelerationStructureBuildGeometryInfoKHR _abgInfo = new AccelerationStructureBuildGeometryInfoKHR()
            {
                SType = StructureType.AccelerationStructureBuildGeometryInfoKhr,
                Type = AccelerationStructureTypeKHR.BottomLevelKhr,
                Flags = BuildAccelerationStructureFlagsKHR.PreferFastTraceBitKhr | BuildAccelerationStructureFlagsKHR.AllowUpdateBitKhr,
                Mode = BuildAccelerationStructureModeKHR.BuildKhr,
                DstAccelerationStructure = _BLAS._handle,
                GeometryCount = 1,
                PGeometries = &_accelStrGeom,
                ScratchData =
                {
                    DeviceAddress = _scratchBuffer._deviceAddress
                }
            };

            AccelerationStructureBuildRangeInfoKHR _asbrInfo = new AccelerationStructureBuildRangeInfoKHR()
            {
                PrimitiveCount = numTris,
                PrimitiveOffset = 0,
                FirstVertex = 0,
                TransformOffset = 0
            };

            CommandBuffer _commandBuffer = AVulkanBufferHandler.BeginSingleTimeCommands();
            _accelerationStructure.CmdBuildAccelerationStructures(_commandBuffer, 1, &_abgInfo, &_asbrInfo);
            AVulkanBufferHandler.EndSingleTimeCommands(ref _commandBuffer);


            AccelerationStructureDeviceAddressInfoKHR _adaInfo = new AccelerationStructureDeviceAddressInfoKHR()
            {
                SType = StructureType.AccelerationStructureDeviceAddressInfoKhr,
                AccelerationStructure = _BLAS._handle
            };
            _BLAS._deviceAddress = _accelerationStructure.GetAccelerationStructureDeviceAddress(_logicalDevice, _adaInfo);
            DeleteScratchBuffer(ref _scratchBuffer);
        }

        private void UpdateBLAS()
        {
            AccelerationStructureGeometryKHR _accelStrGeom = new AccelerationStructureGeometryKHR
            {
                SType = StructureType.AccelerationStructureGeometryKhr,
                Flags = GeometryFlagsKHR.OpaqueBitKhr,
                GeometryType = GeometryTypeKHR.TrianglesKhr,
                Geometry =
                {
                    Triangles =
                    {
                        SType = StructureType.AccelerationStructureGeometryTrianglesDataKhr,
                        VertexFormat = Format.R32G32B32Sfloat,
                        VertexData = _addressVertex,
                        MaxVertex = (uint)(_mesh._vertices.Length),
                        VertexStride = (ulong)sizeof(Vertex),
                        IndexType = IndexType.Uint32,
                        IndexData = _addressIndex,
                        TransformData = _addressTransform,
                        PNext = null
                    }
                },
                PNext = null
            };

            AccelerationStructureBuildGeometryInfoKHR _accelStrGeomInfo = new()
            {
                SType = StructureType.AccelerationStructureBuildGeometryInfoKhr,
                Type = AccelerationStructureTypeKHR.BottomLevelKhr,
                Flags = BuildAccelerationStructureFlagsKHR.PreferFastTraceBitKhr | BuildAccelerationStructureFlagsKHR.AllowUpdateBitKhr,
                GeometryCount = 1,
                PGeometries = &_accelStrGeom,
                SrcAccelerationStructure = _BLAS._handle,
                DstAccelerationStructure = _BLAS._handle,
                Mode = BuildAccelerationStructureModeKHR.UpdateKhr
            };

            uint numTris = (uint)(_mesh._indices.Length / 3);
            AccelerationStructureBuildSizesInfoKHR _asbsInfo = default;
            _asbsInfo.SType = StructureType.AccelerationStructureBuildSizesInfoKhr;

            _accelerationStructure.GetAccelerationStructureBuildSizes(_logicalDevice, AccelerationStructureBuildTypeKHR.DeviceKhr, &_accelStrGeomInfo, numTris, out _asbsInfo);
            PathtracingScratchBuffer _scratchBuffer = new PathtracingScratchBuffer();
            CreateScratchBuffer(_asbsInfo.BuildScratchSize, ref _scratchBuffer);

            AccelerationStructureBuildGeometryInfoKHR _abgInfo = new AccelerationStructureBuildGeometryInfoKHR()
            {
                SType = StructureType.AccelerationStructureBuildGeometryInfoKhr,
                Type = AccelerationStructureTypeKHR.BottomLevelKhr,
                Flags = BuildAccelerationStructureFlagsKHR.PreferFastTraceBitKhr | BuildAccelerationStructureFlagsKHR.AllowUpdateBitKhr,
                Mode = BuildAccelerationStructureModeKHR.UpdateKhr,
                SrcAccelerationStructure = _BLAS._handle,
                DstAccelerationStructure = _BLAS._handle,
                GeometryCount = 1,
                PGeometries = &_accelStrGeom,
                ScratchData =
                {
                    DeviceAddress = _scratchBuffer._deviceAddress
                }
            };

            AccelerationStructureBuildRangeInfoKHR _asbrInfo = new AccelerationStructureBuildRangeInfoKHR()
            {
                PrimitiveCount = numTris,
                PrimitiveOffset = 0,
                FirstVertex = 0,
                TransformOffset = 0
            };

            CommandBuffer _commandBuffer = AVulkanBufferHandler.BeginSingleTimeCommands();
            _accelerationStructure.CmdBuildAccelerationStructures(_commandBuffer, 1, &_abgInfo, &_asbrInfo);
            AVulkanBufferHandler.EndSingleTimeCommands(ref _commandBuffer);


            AccelerationStructureDeviceAddressInfoKHR _adaInfo = new AccelerationStructureDeviceAddressInfoKHR()
            {
                SType = StructureType.AccelerationStructureDeviceAddressInfoKhr,
                AccelerationStructure = _BLAS._handle
            };
            _BLAS._deviceAddress = _accelerationStructure.GetAccelerationStructureDeviceAddress(_logicalDevice, _adaInfo);
            DeleteScratchBuffer(ref _scratchBuffer);
        }

        internal static void CreateAccelerationStructureBuffer(ref AccelerationStruct _accelStructure, ref AccelerationStructureBuildSizesInfoKHR _buildSizeInfo)
        {
            BufferCreateInfo _bufferCreateInfo = new BufferCreateInfo()
            {
                SType = StructureType.BufferCreateInfo,
                Size = _buildSizeInfo.AccelerationStructureSize,
                Usage = BufferUsageFlags.AccelerationStructureStorageBitKhr | BufferUsageFlags.ShaderDeviceAddressBit,
            };
            fixed (Buffer* _bufferPtr = &_accelStructure._buffer)
            {
                _vulkan.CreateBuffer(_logicalDevice, _bufferCreateInfo, null, _bufferPtr);
            }

            MemoryRequirements _memReqs = new MemoryRequirements();
            _vulkan.GetBufferMemoryRequirements(_logicalDevice, _accelStructure._buffer, out _memReqs);
            MemoryAllocateFlagsInfo _memFlags = new MemoryAllocateFlagsInfo()
            {
                SType = StructureType.MemoryAllocateFlagsInfo,
                Flags = MemoryAllocateFlags.AddressBit
            };
            MemoryAllocateInfo _memAllocInfo = new MemoryAllocateInfo()
            {
                SType = StructureType.MemoryAllocateInfo,
                PNext = &_memFlags,
                AllocationSize = _memReqs.Size,
                MemoryTypeIndex = AVulkanBufferHandler.FindMemoryType(_memReqs.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit)
            };

            fixed (DeviceMemory* _bufferMemoryPtr = &_accelStructure._memory)
            {
                if (_vulkan.AllocateMemory(_logicalDevice, _memAllocInfo, null, _bufferMemoryPtr) != Result.Success)
                {
                    throw new Exception("Failed to allocate memory");
                }
            }
            _vulkan.BindBufferMemory(_logicalDevice, _accelStructure._buffer, _accelStructure._memory, 0);
        }

        internal static void DeleteScratchBuffer(ref PathtracingScratchBuffer _sBuffer)
        {
            if (_sBuffer._memory.Handle != 0)
            {
                _vulkan.FreeMemory(_logicalDevice, _sBuffer._memory, null);
            }
            if (_sBuffer._buffer.Handle != 0)
            {
                _vulkan.DestroyBuffer(_logicalDevice, _sBuffer._buffer, null);
            }
        }

        internal static void CreateScratchBuffer(ulong size, ref PathtracingScratchBuffer _b)
        {
            BufferCreateInfo _bufferCreateInfo = new()
            {
                SType = StructureType.BufferCreateInfo,
                Size = size,
                Usage = BufferUsageFlags.StorageBufferBit | BufferUsageFlags.ShaderDeviceAddressBit
            };

            fixed (Buffer* _bufferPtr = &_b._buffer)
            {
                _vulkan.CreateBuffer(_logicalDevice, _bufferCreateInfo, null, _bufferPtr);
            }
            MemoryRequirements _memReqs = new MemoryRequirements();
            _vulkan.GetBufferMemoryRequirements(_logicalDevice, _b._buffer, out _memReqs);
            MemoryAllocateFlagsInfo _memFlags = new MemoryAllocateFlagsInfo()
            {
                SType = StructureType.MemoryAllocateFlagsInfo,
                Flags = MemoryAllocateFlags.AddressBit
            };
            MemoryAllocateInfo _memAllocInfo = new MemoryAllocateInfo()
            {
                SType = StructureType.MemoryAllocateInfo,
                PNext = &_memFlags,
                AllocationSize = _memReqs.Size,
                MemoryTypeIndex = AVulkanBufferHandler.FindMemoryType(_memReqs.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit)
            };

            fixed (DeviceMemory* _bufferMemoryPtr = &_b._memory)
            {
                _vulkan.AllocateMemory(_logicalDevice, _memAllocInfo, null, _bufferMemoryPtr);
            }
            _vulkan.BindBufferMemory(_logicalDevice, _b._buffer, _b._memory, 0);

            BufferDeviceAddressInfo _driverBufferAddressInfo = new BufferDeviceAddressInfo()
            {
                SType = StructureType.BufferDeviceAddressInfoKhr,
                Buffer = _b._buffer,
            };
            _b._deviceAddress = _vulkan.GetBufferDeviceAddress(_logicalDevice, &_driverBufferAddressInfo);
        }

        internal override void FreeDescriptorSets()
        {
            base.FreeDescriptorSets();
            if (_descriptorSets != null)
                VulkanRenderer._vulkan.FreeDescriptorSets(VulkanRenderer._logicalDevice, VulkanRenderer._descriptorPool, (uint)_descriptorSets.Length, _descriptorSets);
        }

        internal override void ReinstantiateDesriptorSets()
        {
            base.ReinstantiateDesriptorSets();
            CreateDescriptorSet();
        }

        internal override void CreateDescriptorSet()
        {
            DescriptorSetLayout[] _layouts = new DescriptorSetLayout[_swapimageCount];
            Array.Fill(_layouts, _descriptorSetLayout);

            fixed (DescriptorSetLayout* _layoutsPtr = _layouts)
            {
                DescriptorSetAllocateInfo _allocateInfo = new DescriptorSetAllocateInfo()
                {
                    SType = StructureType.DescriptorSetAllocateInfo,
                    DescriptorPool = _descriptorPool,
                    DescriptorSetCount = (uint)_swapimageCount,
                    PSetLayouts = _layoutsPtr
                };

                _descriptorSets = new DescriptorSet[_swapimageCount];
                fixed (DescriptorSet* _descriptorSetsPtr = _descriptorSets)
                {
                    Result r = _vulkan.AllocateDescriptorSets(_logicalDevice, ref _allocateInfo, _descriptorSetsPtr);
                    if (r != Result.Success)
                    {
                        throw new Exception("Failed to allocate descriptor set with error code: " + r);
                    }
                }
            }
            for (int i = 0; i < _swapimageCount; i++)
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
                        ImageView = _storageImageView[i]
                    };
                    DescriptorBufferInfo _bufferInfoMatrices = new DescriptorBufferInfo()
                    {
                        Buffer = _camera._cameraBuffer[i],
                        Offset = 0,
                        Range = (ulong)Unsafe.SizeOf<UBO>()
                    };

                    DescriptorBufferInfo _vertexData = new()
                    {
                        Buffer = _vertexBuffer,
                        Offset = 0,
                        Range = (ulong)(sizeof(Vertex) * _mesh._vertices.Length)
                    };

                    DescriptorBufferInfo _indexData = new()
                    {
                        Buffer = _indexBuffer,
                        Offset = 0,
                        Range = (ulong)(sizeof(uint) * _mesh._indices.Length)
                    };

                    var _writeDescriptorSets = new WriteDescriptorSet[]
                    {
                        new WriteDescriptorSet
                        {
                            SType = StructureType.WriteDescriptorSet,
                            PNext = &_dasinfo,
                            DstSet = _descriptorSets[i],
                            DstBinding = 0,
                            DescriptorCount = 1,
                            DstArrayElement = 0,
                            DescriptorType = DescriptorType.AccelerationStructureKhr
                        },
                        new WriteDescriptorSet
                        {
                            SType = StructureType.WriteDescriptorSet,
                            DstSet = _descriptorSets[i],
                            DstBinding = 1,
                            DescriptorCount = 1,
                            DstArrayElement = 0,
                            DescriptorType = DescriptorType.StorageImage,
                            PImageInfo = &_dImageInfo
                        },
                        new WriteDescriptorSet
                        {
                            SType = StructureType.WriteDescriptorSet,
                            DstSet = _descriptorSets[i],
                            DstBinding = 2,
                            DescriptorCount = 1,
                            DstArrayElement = 0,
                            DescriptorType = DescriptorType.UniformBuffer,
                            PBufferInfo = &_bufferInfoMatrices
                        },
                        // vertex data
                        new WriteDescriptorSet
                        {
                            SType = StructureType.WriteDescriptorSet,
                            DstSet = _descriptorSets[i],
                            DstBinding = 3,
                            DescriptorCount = 1,
                            DstArrayElement = 0,
                            DescriptorType = DescriptorType.StorageBuffer,
                            PBufferInfo = &_vertexData
                        },
                        new WriteDescriptorSet
                        {
                            SType = StructureType.WriteDescriptorSet,
                            DstSet = _descriptorSets[i],
                            DstBinding = 4,
                            DescriptorCount = 1,
                            DstArrayElement = 0,
                            DescriptorType = DescriptorType.StorageBuffer,
                            PBufferInfo = &_indexData
                        }
                    };
                    fixed (WriteDescriptorSet* _descPtr = _writeDescriptorSets)
                    {
                        _vulkan!.UpdateDescriptorSets(_logicalDevice, (uint)_writeDescriptorSets.Length, _descPtr, 0, null);
                    }
                }
            }
        }

        internal override void MakeInstanced(ref List<Matrix4X4<float>> _matrices)
        {
            base.MakeInstanced(ref _matrices);
            //will have to adjust later because this should have to remake the TLAS
        }

        internal override void SingletonMatrix()
        {
            Quaternion<float> q = Quaternion<float>.Identity;
            Matrix4X4<float> _transform = Matrix4X4<float>.Identity;
            _transform *= Matrix4X4.CreateScale(parent.transform.scale);
            _transform *= Matrix4X4.CreateFromQuaternion(q);
            _transform *= Matrix4X4.CreateTranslation(parent.transform.position);
            _transform = Matrix4X4.Transpose(_transform);

            _transformMatrices.Add(_transform);
            AVulkanBufferHandler.CreateTransformBuffer(ref _transformMatrices, ref _transformsBuffer, ref _transformsBufferMemory, _aditionalUsageFlags);
        }

        internal override void UpdateMatrices()
        {
            Quaternion<float> q = Quaternion<float>.Identity;
            Matrix4X4<float> _transform = Matrix4X4<float>.Identity;
            _transform *= Matrix4X4.CreateScale(parent.transform.scale);
            _transform *= Matrix4X4.CreateFromQuaternion(q);
            _transform *= Matrix4X4.CreateTranslation(parent.transform.position);
            _transform = Matrix4X4.Transpose(_transform);

            _transformMatrices[0] = _transform;
            TransformMatrixKHR _entityVulkanMatric = new TransformMatrixKHR();
            Unsafe.CopyBlock(_entityVulkanMatric.Matrix, Unsafe.AsPointer(ref _transformMatrices.ToArray()[0]), 48);
            //_accelerationInstance.Transform = _entityVulkanMatric;

            void* _data;
            _vulkan.MapMemory(_logicalDevice, _transformsBufferMemory, 0, (ulong)sizeof(TransformMatrixKHR), 0, &_data);
            new Span<TransformMatrixKHR>(_data, 1)[0] = _entityVulkanMatric;
            _vulkan.UnmapMemory(_logicalDevice, _transformsBufferMemory);
            UpdateBLAS();

            parent.transform._changed = false;
        }

        internal override void EnqueueDrawCommands(ref ulong[] _offset, int _loopIndex, ref CommandBuffer _commandBuffer)
        {
            if (_render)
            {
                _vulkan.CmdBindDescriptorSets(_commandBuffer, PipelineBindPoint.RayTracingKhr, _pipelineLayout, 0, 1, ref _descriptorSets[_loopIndex], 0, null);
            }
        }
    }
}