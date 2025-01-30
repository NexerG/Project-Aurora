using ArctisAurora.EngineWork.ECS.RenderingComponents.Vulkan;
using ArctisAurora.EngineWork.Renderer.Helpers;
using ArctisAurora.EngineWork.Renderer.RendererTypes;
using Assimp;
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

        // Material. WILL BE SEPARATED INTO A DIFFERENT CLASS LATER ON :))
        Vector3D<float> _color = new Vector3D<float>(1,1,1);
        internal Buffer _colorBuffer;
        DeviceMemory _colorMemory;

        internal MCRaytracing()
        {
            _aditionalUsageFlags = BufferUsageFlags.AccelerationStructureBuildInputReadOnlyBitKhr | BufferUsageFlags.ShaderDeviceAddressBit | BufferUsageFlags.StorageBufferBit;
            //AVulkanBufferHandler.CreateVertexBuffer(ref _mesh._vertices, ref _vertexBuffer, ref _vertexBufferMemory, _aditionalUsageFlags);
            //AVulkanBufferHandler.CreateIndexBuffer(ref _mesh._indices, ref _indexBuffer, ref _indexBufferMemory, _aditionalUsageFlags);

            // Buffer for Vertices & Indices
            AVulkanBufferHandler.CreateBuffer(ref _mesh._vertices, ref _vertexBuffer, ref _vertexBufferMemory, AVulkanBufferHandler.vertexBufferFlags | _aditionalUsageFlags);
            AVulkanBufferHandler.CreateBuffer(ref _mesh._indices, ref _indexBuffer, ref _indexBufferMemory, AVulkanBufferHandler.indexBufferFlags | _aditionalUsageFlags);

            BufferUsageFlags additionalColorFlags = BufferUsageFlags.UniformBufferBit | BufferUsageFlags.ShaderDeviceAddressBit;
            AVulkanBufferHandler.CreateBuffer(ref _color, ref _colorBuffer, ref _colorMemory, additionalColorFlags);
        }

        public override void OnStart()
        {
            SingletonMatrix();
            CreateBLAS();
            _rendererInstance.AddEntityToRenderQueue(parent);
            //CreateDescriptorSet();
        }

        internal void UpdateColor(Vector3D<float> color)
        {
            _color = color;
            AVulkanBufferHandler.UpdateBuffer(ref _color, ref _colorBuffer, ref _colorMemory, BufferUsageFlags.None);
        }

        internal override void LoadCustomMesh(Scene sc)
        {
            VulkanRenderer._vulkan.DestroyBuffer(VulkanRenderer._logicalDevice, _vertexBuffer, null);
            VulkanRenderer._vulkan.DestroyBuffer(VulkanRenderer._logicalDevice, _indexBuffer, null);
            VulkanRenderer._vulkan.FreeMemory(VulkanRenderer._logicalDevice, _indexBufferMemory, null);
            VulkanRenderer._vulkan.FreeMemory(VulkanRenderer._logicalDevice, _vertexBufferMemory, null);
            if (_BLAS._handle.Handle != 0)
            {
                _accelerationStructure.DestroyAccelerationStructure(_logicalDevice, _BLAS._handle, null);
                _vulkan.FreeMemory(_logicalDevice, _BLAS._memory, null);
                _vulkan.DestroyBuffer(_logicalDevice, _BLAS._buffer, null);
            }

            _mesh.LoadCustomMesh(sc);

            AVulkanBufferHandler.CreateBuffer(ref _mesh._vertices, ref _vertexBuffer, ref _vertexBufferMemory, AVulkanBufferHandler.vertexBufferFlags | _aditionalUsageFlags);
            AVulkanBufferHandler.CreateBuffer(ref _mesh._indices, ref _indexBuffer, ref _indexBufferMemory, AVulkanBufferHandler.indexBufferFlags | _aditionalUsageFlags);
            CreateBLAS();
            Pathtracing.UpdateAccInstance(this);
            UpdateTLAS();
            ReinstantiateDesriptorSets();

            VulkanRenderer._rendererInstance.RecreateCommandBuffers();
        }

        private void CreateBLAS()
        {
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
            _BLAS._deviceAddress = _accelerationStructure.GetAccelerationStructureDeviceAddress(_logicalDevice, ref _adaInfo);
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
            BufferUsageFlags buf = BufferUsageFlags.StorageBufferBit | BufferUsageFlags.ShaderDeviceAddressBit;
            AVulkanBufferHandler.CreateBuffer(size, ref _b._buffer, ref _b._memory, buf, MemoryPropertyFlags.DeviceLocalBit);

            BufferDeviceAddressInfo _driverBufferAddressInfo = new BufferDeviceAddressInfo()
            {
                SType = StructureType.BufferDeviceAddressInfoKhr,
                Buffer = _b._buffer,
            };

            _b._deviceAddress = _vulkan.GetBufferDeviceAddress(_logicalDevice, &_driverBufferAddressInfo);
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

            TransformMatrixKHR _entityVulkanTransform = new TransformMatrixKHR();
            Unsafe.CopyBlock(_entityVulkanTransform.Matrix, Unsafe.AsPointer(ref _transformMatrices.ToArray()[0]), 48);

            BufferUsageFlags bufferUsageFlags = BufferUsageFlags.ShaderDeviceAddressBit | BufferUsageFlags.AccelerationStructureBuildInputReadOnlyBitKhr | BufferUsageFlags.UniformBufferBit;
            AVulkanBufferHandler.CreateBuffer(ref _entityVulkanTransform, ref _transformsBuffer, ref _transformsBufferMemory, bufferUsageFlags);
        }

        internal override void UpdateMatrices()
        {
            Quaternion<float> q = Quaternion<float>.CreateFromYawPitchRoll(parent.transform.rotation.X,parent.transform.rotation.Y,parent.transform.rotation.Z);
            Matrix4X4<float> _transform = Matrix4X4<float>.Identity;
            _transform *= Matrix4X4.CreateScale(parent.transform.scale);
            _transform *= Matrix4X4.CreateFromQuaternion(q);
            _transform *= Matrix4X4.CreateTranslation(parent.transform.position);
            _transform = Matrix4X4.Transpose(_transform);

            _transformMatrices[0] = _transform;
            TransformMatrixKHR _entityVulkanMatrix = new TransformMatrixKHR();
            Unsafe.CopyBlock(_entityVulkanMatrix.Matrix, Unsafe.AsPointer(ref _transformMatrices.ToArray()[0]), 48);
            //_accelerationInstance.Transform = _entityVulkanMatrix;
            BufferUsageFlags bufferUsageFlags = BufferUsageFlags.ShaderDeviceAddressBit | BufferUsageFlags.AccelerationStructureBuildInputReadOnlyBitKhr | BufferUsageFlags.UniformBufferBit;
            AVulkanBufferHandler.UpdateBuffer(ref _entityVulkanMatrix, ref _transformsBuffer, ref _transformsBufferMemory, bufferUsageFlags);

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