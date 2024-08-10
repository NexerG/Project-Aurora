using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;
using Silk.NET.Vulkan.Extensions.KHR;
using ArctisAurora.EngineWork.ECS.RenderingComponents.Vulkan;
using ArctisAurora.EngineWork.Renderer.Helpers;
using ArctisAurora.CustomEntities;
using Silk.NET.Core.Native;
using Image = Silk.NET.Vulkan.Image;
using ImageLayout = Silk.NET.Vulkan.ImageLayout;
using Semaphore = Silk.NET.Vulkan.Semaphore;
using Silk.NET.Maths;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace ArctisAurora.EngineWork.Renderer
{
    internal unsafe class Pathtracing : VulkanRenderer
    {
        //temporary
        TestingEntity _testEnt;
        Buffer _fakeTransformBuffer;
        DeviceMemory _fakeTransformDM;
        Buffer _fakeInstanceBuffer;
        DeviceMemory _fakeInstanceDM;
        //

        string[] requiredExtensions =
        {
                "VK_KHR_swapchain",
                "VK_EXT_descriptor_indexing",
                "VK_KHR_spirv_1_4",
                "VK_KHR_shader_float_controls",
                KhrAccelerationStructure.ExtensionName,
                KhrRayTracingPipeline.ExtensionName,
                KhrDeferredHostOperations.ExtensionName,
                KhrBufferDeviceAddress.ExtensionName,
        };

        PhysicalDeviceRayTracingPipelinePropertiesKHR _rtPipelineProperties = default;
        PhysicalDeviceRayTracingPipelineFeaturesKHR _rtPipelineFeatures = default;

        DeviceOrHostAddressConstKHR _addressVertex = default;
        DeviceOrHostAddressConstKHR _addressIndex = default;
        DeviceOrHostAddressConstKHR _addressTransform = default;
        DeviceOrHostAddressConstKHR _addressInstance = default;

        KhrAccelerationStructure _accelerationStructure = default;
        AccelerationStruct _BLAS = default;
        internal static AccelerationStruct _TLAS = default;

        internal static Swapchain _swapchain;
        internal Image[] _storageImage;
        internal DeviceMemory[] _storageDM;
        internal static ImageView[] _storageImageView;

        KhrRayTracingPipeline _rtExtention;
        internal static DescriptorSetLayout _descriptorSetLayout;
        PipelineLayout _pipelineLayout;
        Pipeline _rtPipeline;
        int MAX_FRAMES_IN_FLIGHT = 2;
        int _currentFrame = 0;
        private Semaphore[] _imageAvailableSemaphores;
        private Semaphore[] _renderFinishedSemaphores;
        private Fence[] _fencesInFlight;
        private Fence[] _imagesInFlight;

        Buffer _raygenBindingTable;
        DeviceMemory _raygenBTDM;
        Buffer _missBindingTable;
        DeviceMemory _missBTDM; 
        Buffer _hitBinddingTable;
        DeviceMemory _hitBTDM;

        CommandBuffer[] _commandBuffer;

        internal static DescriptorPool _descriptorPool;

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

        internal Pathtracing() 
        {
            setup();
            //-------------- setup above

            int _graphicsQFamilyIndex = AVulkanHelper.FindQueueFamilyIndex(ref _gpu, ref _qfm, QueueFlags.GraphicsBit);
            uint _presentSupportIndex = AVulkanHelper.FindPresentSupportIndex(ref _qfm, ref _glWindow._driverSurface, ref _glWindow._surface);
            _graphicsQueue = _vulkan.GetDeviceQueue(_logicalDevice, (uint)_graphicsQFamilyIndex, 0);
            _presentQueue = _vulkan.GetDeviceQueue(_logicalDevice, _presentSupportIndex, 0);

            _swapchain = new Swapchain(ref _glWindow._driverSurface, ref _glWindow._surface);
            _swapchain.DoSwapchainMethodSequence(ref _extent);        //swapchain methods for simplicity sake
            _swapimageCount = _swapchain._swapchainImages.Length;     //engine related thing
            _camera = new AuroraCamera();

            CreateCommandPool();
            CreateStorageImage();
            CreateDescriptorPool();
            //meshes after this point
            CreateBLAS();
            CreateTLAS();
            CreateRaytracingPipeline();
            CreateShaderBindingTable();

            _testEnt.GetComponent<MeshComponent>().CreateDescriptorSet();
            CreateCommandBuffers();
            CreateSyncObjects();                                        //CPU - GPU sync logic
        }

        private void setup()
        {
            _rendererInstance = this;
            PhysicalDeviceRayTracingPipelineFeaturesKHR _rtPipelineFeature = new PhysicalDeviceRayTracingPipelineFeaturesKHR()
            {
                SType = StructureType.PhysicalDeviceRayTracingPipelineFeaturesKhr,
                RayTracingPipeline = true
            };
            PhysicalDeviceAccelerationStructureFeaturesKHR accelerationStructureFeatures = new()
            {
                SType = StructureType.PhysicalDeviceAccelerationStructureFeaturesKhr,
                AccelerationStructure = true,
                PNext = &_rtPipelineFeature
            };
            PhysicalDeviceVulkan12Features _vulkan12FT = new PhysicalDeviceVulkan12Features()
            {
                SType = StructureType.PhysicalDeviceVulkan12Features,
                DescriptorIndexing = true,
                BufferDeviceAddress = true,
                RuntimeDescriptorArray = true,
                PNext = &accelerationStructureFeatures
            };
            CreateLogicalDevice(requiredExtensions, _vulkan12FT, null);
            _rtPipelineProperties.SType = StructureType.PhysicalDeviceRayTracingPipelinePropertiesKhr;
            fixed (PhysicalDeviceRayTracingPipelinePropertiesKHR* _rtPtr = &_rtPipelineProperties)
            {
                PhysicalDeviceProperties2 _devprops2 = new PhysicalDeviceProperties2()
                {
                    SType = StructureType.PhysicalDeviceProperties2,
                    PNext = _rtPtr
                };
                _vulkan.GetPhysicalDeviceProperties2(_gpu, &_devprops2);
            }

        }

        private void CreateDescriptorPool()
        {
            var _poolSizes = new DescriptorPoolSize[]
            {
                new DescriptorPoolSize()
                {
                    Type = DescriptorType.AccelerationStructureKhr,
                    DescriptorCount = 1
                },
                new DescriptorPoolSize()
                {
                    Type = DescriptorType.StorageImage,
                    DescriptorCount = 1
                },
                new DescriptorPoolSize()
                {
                    Type = DescriptorType.UniformBuffer,
                    DescriptorCount = 1
                }
            };

            fixed(DescriptorPoolSize* _poolSizePtr = _poolSizes)
            fixed(DescriptorPool* _dpPtr = &_descriptorPool)
            {
                DescriptorPoolCreateInfo _createInfo = new DescriptorPoolCreateInfo()
                {
                    SType = StructureType.DescriptorPoolCreateInfo,
                    PoolSizeCount = (uint)_poolSizes.Length,
                    PPoolSizes = _poolSizePtr,
                    MaxSets = (uint)_swapimageCount,
                };
                if (_vulkan.CreateDescriptorPool(_logicalDevice, _createInfo, null, _dpPtr) != Result.Success)
                {
                    throw new Exception("Failed to create descriptor pool");
                }
            }
        }

        private void CreateBLAS()
        {
            _testEnt = new TestingEntity();

            Matrix4X4<float> _ftm = Matrix4X4<float>.Identity;
            TransformMatrixKHR _fakeTransformMatrix = new TransformMatrixKHR();
            Unsafe.CopyBlock(_fakeTransformMatrix.Matrix, Unsafe.AsPointer(ref _ftm), 48);

            AVulkanBufferHandler.CreateBuffer(
                (ulong)sizeof(TransformMatrixKHR),
                BufferUsageFlags.ShaderDeviceAddressBit | BufferUsageFlags.AccelerationStructureBuildInputReadOnlyBitKhr,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
                ref _fakeTransformBuffer, ref _fakeTransformDM);
            void* _data;
            _vulkan.MapMemory(_logicalDevice, _fakeTransformDM, 0, (ulong)sizeof(TransformMatrixKHR), 0, &_data);
            new Span<TransformMatrixKHR>(_data, 1)[0] = _fakeTransformMatrix;
            _vulkan.UnmapMemory(_logicalDevice, _fakeTransformDM);
            //---------------------------------------
            /*AVulkanBufferHandler.CreateBuffer(
                (ulong)(sizeof(fakeVertex) * _fakeVertexData.Length),
                BufferUsageFlags.ShaderDeviceAddressBit | BufferUsageFlags.AccelerationStructureBuildInputReadOnlyBitKhr,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
                ref _fakeVertexBuffer, ref _fakeVertexDM);
            void* _data2;
            _vulkan.MapMemory(_logicalDevice, _fakeVertexDM, 0, (ulong)(sizeof(fakeVertex) * _fakeVertexData.Length), 0, &_data2);
            _fakeVertexData.AsSpan().CopyTo(new Span<fakeVertex>(_data2, _fakeVertexData.Length));
            _vulkan.UnmapMemory(_logicalDevice, _fakeVertexDM);
            //
            AVulkanBufferHandler.CreateBuffer(
                (ulong)(sizeof(uint) * _fakeIndexData.Length),
                BufferUsageFlags.ShaderDeviceAddressBit | BufferUsageFlags.AccelerationStructureBuildInputReadOnlyBitKhr,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
                ref _fakeIndexBuffer, ref _fakeIndexDM);
            void* _data3;
            _vulkan.MapMemory(_logicalDevice, _fakeIndexDM, 0, (ulong)(sizeof(uint) * _fakeIndexData.Length), 0, &_data3);
            _fakeIndexData.AsSpan().CopyTo(new Span<uint>(_data3, _fakeIndexData.Length));
            _vulkan.UnmapMemory(_logicalDevice, _fakeIndexDM);*/
            //

            _addressVertex.DeviceAddress = GetAddress(_testEnt.GetComponent<MeshComponent>()._vertexBuffer);
            _addressIndex.DeviceAddress = GetAddress(_testEnt.GetComponent<MeshComponent>()._indexBuffer);
            //_addressVertex.DeviceAddress = GetAddress(_fakeVertexBuffer);
            //_addressIndex.DeviceAddress = GetAddress(_fakeIndexBuffer);
            _addressTransform.DeviceAddress = GetAddress(_fakeTransformBuffer);

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
                        //MaxVertex = 2,
                        MaxVertex = (uint)(_testEnt.GetComponent<MeshComponent>()._mesh._vertices.Length),
                        //VertexStride = (ulong)sizeof(fakeVertex),
                        VertexStride = (ulong)sizeof(Vertex),
                        //IndexType = IndexType.Uint32,
                        IndexType = IndexType.Uint16,
                        IndexData = _addressIndex,
                        TransformData = default,
                        PNext = null
                    }
                },
                PNext = null,
            };
            _accelStrGeom.Geometry.Triangles.TransformData = _addressTransform;

            AccelerationStructureBuildGeometryInfoKHR _accelStrGeomInfo = new()
            {
                SType = StructureType.AccelerationStructureBuildGeometryInfoKhr,
                Type = AccelerationStructureTypeKHR.BottomLevelKhr,
                Flags = BuildAccelerationStructureFlagsKHR.PreferFastTraceBitKhr,
                GeometryCount = 1,
                PGeometries = &_accelStrGeom,
                SrcAccelerationStructure = default,
            };

            uint numTris = (uint)(_testEnt.GetComponent<MeshComponent>()._mesh._indices.Length / 3);
            //uint numTris = 1;
            AccelerationStructureBuildSizesInfoKHR _asbsInfo = default;
            _asbsInfo.SType = StructureType.AccelerationStructureBuildSizesInfoKhr;
            _vulkan.TryGetDeviceExtension(_instance, _logicalDevice, out _accelerationStructure);
            _accelerationStructure.GetAccelerationStructureBuildSizes(_logicalDevice, AccelerationStructureBuildTypeKHR.DeviceKhr, &_accelStrGeomInfo, numTris, out _asbsInfo);
            CreateAccelerationStructureBuffer(ref _BLAS, ref _asbsInfo);

            AccelerationStructureCreateInfoKHR _accelerationStructureCreateInfo = new AccelerationStructureCreateInfoKHR()
            {
                SType = StructureType.AccelerationStructureCreateInfoKhr,
                Buffer = _BLAS._buffer,
                Size = _asbsInfo.AccelerationStructureSize,
                Type = AccelerationStructureTypeKHR.BottomLevelKhr
            };
            Result r = _accelerationStructure.CreateAccelerationStructure(_logicalDevice, _accelerationStructureCreateInfo, null, out _BLAS._handle);
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
                Flags = BuildAccelerationStructureFlagsKHR.PreferFastTraceBitKhr,
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

        private void CreateTLAS()
        {
            Matrix4X4<float> _ftm = Matrix4X4<float>.Identity;
            TransformMatrixKHR _matrix = new TransformMatrixKHR();
            Unsafe.CopyBlock(_matrix.Matrix, Unsafe.AsPointer(ref _ftm), 48);

            AccelerationStructureInstanceKHR _accelerationInstance = new AccelerationStructureInstanceKHR()
            {
                Flags = GeometryInstanceFlagsKHR.TriangleFacingCullDisableBitKhr,
                AccelerationStructureReference = _BLAS._deviceAddress,
                Transform = _matrix,
                InstanceCustomIndex = 0,
                Mask = 0xFF,
                InstanceShaderBindingTableRecordOffset = 0
            };

            AVulkanBufferHandler.CreateBuffer(
                (ulong)sizeof(AccelerationStructureInstanceKHR),
                BufferUsageFlags.ShaderDeviceAddressBit | BufferUsageFlags.AccelerationStructureBuildInputReadOnlyBitKhr,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
                ref _fakeInstanceBuffer, ref _fakeInstanceDM);
            void* _data;
            _vulkan.MapMemory(_logicalDevice, _fakeInstanceDM, 0, (ulong)sizeof(AccelerationStructureInstanceKHR), 0, &_data);
            new Span<AccelerationStructureInstanceKHR>(_data, 1)[0] = _accelerationInstance;
            _vulkan.UnmapMemory(_logicalDevice, _fakeInstanceDM);

            _addressInstance.DeviceAddress = GetAddress(_fakeInstanceBuffer);

            AccelerationStructureGeometryKHR _asg = new AccelerationStructureGeometryKHR()
            {
                SType = StructureType.AccelerationStructureGeometryKhr,
                GeometryType = GeometryTypeKHR.InstancesKhr,
                Flags = GeometryFlagsKHR.OpaqueBitKhr,
                Geometry =
                {
                    Instances =
                    {
                        SType = StructureType.AccelerationStructureGeometryInstancesDataKhr,
                        ArrayOfPointers = false,
                        Data = _addressInstance
                    }
                }
            };
            AccelerationStructureBuildGeometryInfoKHR _asbgInfo = new AccelerationStructureBuildGeometryInfoKHR()
            {
                SType = StructureType.AccelerationStructureBuildGeometryInfoKhr,
                Type = AccelerationStructureTypeKHR.TopLevelKhr,
                Flags = BuildAccelerationStructureFlagsKHR.PreferFastTraceBitKhr,
                GeometryCount = 1,
                PGeometries = &_asg
            };
            uint primitive_count = 1;
            AccelerationStructureBuildSizesInfoKHR _asbsInfo = new()
            {
                SType = StructureType.AccelerationStructureBuildSizesInfoKhr
            };
            _accelerationStructure.GetAccelerationStructureBuildSizes(_logicalDevice, AccelerationStructureBuildTypeKHR.DeviceKhr, &_asbgInfo, primitive_count, &_asbsInfo);

            CreateAccelerationStructureBuffer(ref _TLAS, ref _asbsInfo);

            AccelerationStructureCreateInfoKHR _asCreateInfo = new AccelerationStructureCreateInfoKHR()
            {
                SType = StructureType.AccelerationStructureCreateInfoKhr,
                Buffer = _TLAS._buffer,
                Size = _asbsInfo.AccelerationStructureSize,
                Type = AccelerationStructureTypeKHR.TopLevelKhr,
            };
            //
            Result r = _accelerationStructure.CreateAccelerationStructure(_logicalDevice, _asCreateInfo, null, out _TLAS._handle);
            if (r != Result.Success)
            {
                throw new Exception("failed to create TLAS on the host");
            }
            PathtracingScratchBuffer _scratchBuffer = new PathtracingScratchBuffer();
            CreateScratchBuffer(_asbsInfo.BuildScratchSize, ref _scratchBuffer);

            AccelerationStructureBuildGeometryInfoKHR _abgInfo = new AccelerationStructureBuildGeometryInfoKHR()
            {
                SType = StructureType.AccelerationStructureBuildGeometryInfoKhr,
                Type = AccelerationStructureTypeKHR.TopLevelKhr,
                Flags = BuildAccelerationStructureFlagsKHR.PreferFastTraceBitKhr,
                Mode = BuildAccelerationStructureModeKHR.BuildKhr,
                DstAccelerationStructure = _TLAS._handle,
                GeometryCount = 1,
                PGeometries = &_asg,
                ScratchData =
                {
                    DeviceAddress = _scratchBuffer._deviceAddress
                }
            };
            AccelerationStructureBuildRangeInfoKHR _asbrInfo = new AccelerationStructureBuildRangeInfoKHR()
            {
                PrimitiveCount = primitive_count,
                PrimitiveOffset = 0,
                FirstVertex = 0,
                TransformOffset = 0
            };
            var _buildRangeInfos = stackalloc[]
            {
                _asbrInfo
            };

            CommandBuffer _commandBuffer = AVulkanBufferHandler.BeginSingleTimeCommands();
            _accelerationStructure.CmdBuildAccelerationStructures(_commandBuffer, 1, &_abgInfo, _buildRangeInfos);
            AVulkanBufferHandler.EndSingleTimeCommands(ref _commandBuffer);

            AccelerationStructureDeviceAddressInfoKHR _adaInfo = new AccelerationStructureDeviceAddressInfoKHR()
            {
                SType = StructureType.AccelerationStructureDeviceAddressInfoKhr,
                AccelerationStructure = _TLAS._handle
            };
            _TLAS._deviceAddress = _accelerationStructure.GetAccelerationStructureDeviceAddress(_logicalDevice, _adaInfo);
            DeleteScratchBuffer(ref _scratchBuffer);
            _vulkan.DestroyBuffer(_logicalDevice, _fakeInstanceBuffer, null);
        }

        private void CreateRaytracingPipeline()
        {
            DescriptorSetLayoutBinding _asLayoutBinding = new DescriptorSetLayoutBinding()
            {
                Binding = 0,
                DescriptorType = DescriptorType.AccelerationStructureKhr,
                DescriptorCount = 1,
                StageFlags = ShaderStageFlags.RaygenBitKhr
            };

            DescriptorSetLayoutBinding _resultImageLayoutBinding = new()
            {
                Binding = 1,
                DescriptorType = DescriptorType.StorageImage,
                DescriptorCount = 1,
                StageFlags = ShaderStageFlags.RaygenBitKhr
            };

            DescriptorSetLayoutBinding _uniformBufferBinding = new DescriptorSetLayoutBinding()
            {
                Binding = 2,
                DescriptorType = DescriptorType.UniformBuffer,
                DescriptorCount = 1,
                StageFlags = ShaderStageFlags.RaygenBitKhr
            };

            var _bindings = new DescriptorSetLayoutBinding[] { _asLayoutBinding, _resultImageLayoutBinding, _uniformBufferBinding};

            fixed(DescriptorSetLayoutBinding* _bPtr = _bindings)
            fixed(DescriptorSetLayout* _setPtr = &_descriptorSetLayout)
            {
                DescriptorSetLayoutCreateInfo _layoutCreateInfo = new DescriptorSetLayoutCreateInfo()
                {
                    SType = StructureType.DescriptorSetLayoutCreateInfo,
                    BindingCount = (uint)_bindings.Length,
                    PBindings = _bPtr
                };
                if (_vulkan.CreateDescriptorSetLayout(_logicalDevice, _layoutCreateInfo, null, _setPtr) != Result.Success)
                {
                    throw new Exception("Failed to create descriptor set layout");
                }
                PipelineLayoutCreateInfo _pipelineLayoutCreateInfo = new()
                {
                    SType = StructureType.PipelineLayoutCreateInfo,
                    SetLayoutCount = 1,
                    PSetLayouts = _setPtr
                };
                fixed(PipelineLayout* _pipePtr = &_pipelineLayout)
                    if (_vulkan.CreatePipelineLayout(_logicalDevice, _pipelineLayoutCreateInfo, null, _pipePtr) != Result.Success)
                    {
                        throw new Exception("Failed to create pipeline layout");
                    }
            }

            byte[] _rayGenCode = ReadFile("../../../Shaders/raygen.rgen.spv");
            byte[] _missCode = ReadFile("../../../Shaders/miss.rmiss.spv");
            byte[] _hitCode = ReadFile("../../../Shaders/closesthit.rchit.spv");

            ShaderModule _raygenShader = CreateShaderModule(_rayGenCode);
            ShaderModule _missShader = CreateShaderModule(_missCode);
            ShaderModule _hitShader = CreateShaderModule(_hitCode);

            PipelineShaderStageCreateInfo _pipelineShaderStageRaygenCI = new()
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.RaygenBitKhr,
                Module = _raygenShader,
                PName = (byte*)SilkMarshal.StringToPtr("main")
            };
            PipelineShaderStageCreateInfo _pipelineShaderStageMissCI = new()
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.MissBitKhr,
                Module = _missShader,
                PName = (byte*)SilkMarshal.StringToPtr("main")
            };
            PipelineShaderStageCreateInfo _pipelineShaderStageHitCI = new()
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.ClosestHitBitKhr,
                Module = _hitShader,
                PName = (byte*)SilkMarshal.StringToPtr("main")
            };

            var _stages = stackalloc[]
            {
                _pipelineShaderStageRaygenCI,
                _pipelineShaderStageMissCI,
                _pipelineShaderStageHitCI
            };
            //
            RayTracingShaderGroupCreateInfoKHR _raygenCI = new()
            {
                SType = StructureType.RayTracingShaderGroupCreateInfoKhr,
                Type = RayTracingShaderGroupTypeKHR.GeneralKhr,
                GeneralShader = 0,
                ClosestHitShader = Vk.ShaderUnusedKhr,
                AnyHitShader = Vk.ShaderUnusedKhr,
                IntersectionShader = Vk.ShaderUnusedKhr,
            };
            //
            RayTracingShaderGroupCreateInfoKHR _missCI = new()
            {
                SType = StructureType.RayTracingShaderGroupCreateInfoKhr,
                Type = RayTracingShaderGroupTypeKHR.GeneralKhr,
                GeneralShader = 1,
                ClosestHitShader = Vk.ShaderUnusedKhr,
                AnyHitShader = Vk.ShaderUnusedKhr,
                IntersectionShader = Vk.ShaderUnusedKhr,
            };
            //
            RayTracingShaderGroupCreateInfoKHR _closesCI = new()
            {
                SType = StructureType.RayTracingShaderGroupCreateInfoKhr,
                Type = RayTracingShaderGroupTypeKHR.TrianglesHitGroupKhr,
                GeneralShader = Vk.ShaderUnusedKhr,
                ClosestHitShader = 2,
                AnyHitShader = Vk.ShaderUnusedKhr,
                IntersectionShader = Vk.ShaderUnusedKhr,
            };
            var _shaderGroups = stackalloc[]
                { _raygenCI , _missCI, _closesCI};
            //
            //
            RayTracingPipelineCreateInfoKHR _rtPipelineCI = new()
            {
                SType = StructureType.RayTracingPipelineCreateInfoKhr,
                StageCount = 3,
                PStages = _stages,
                GroupCount = 3,
                PGroups = _shaderGroups,
                MaxPipelineRayRecursionDepth = 1,
                Layout = _pipelineLayout
            };
            _vulkan.TryGetDeviceExtension(_instance, _logicalDevice, out _rtExtention);
            Result r = _rtExtention.CreateRayTracingPipelines(_logicalDevice, default, default, 1, _rtPipelineCI, null, out _rtPipeline);
            if (r != Result.Success)
            {
                throw new Exception("Failed to create graphics pipeline " + r);
            }
        }

        private void CreateShaderBindingTable()
        {
            uint _handleSize = _rtPipelineProperties.ShaderGroupHandleSize;
            uint _handleSizeAligned = AlignedSize(_handleSize, _rtPipelineProperties.ShaderGroupHandleAlignment);
            uint _groupCount = 3;
            uint _sbtSize = _groupCount * _handleSizeAligned;

            BufferUsageFlags _buf = BufferUsageFlags.ShaderBindingTableBitKhr | BufferUsageFlags.ShaderDeviceAddressBit;
            MemoryPropertyFlags _mpf = MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit;

            AVulkanBufferHandler.CreateBuffer(_handleSize, _buf, _mpf, ref _raygenBindingTable, ref _raygenBTDM);
            AVulkanBufferHandler.CreateBuffer(_handleSize, _buf, _mpf, ref _missBindingTable, ref _missBTDM);
            AVulkanBufferHandler.CreateBuffer(_handleSize, _buf, _mpf, ref _hitBinddingTable, ref _hitBTDM);
            
            byte[] _sbt = new byte[_sbtSize];
            fixed (byte* _ptr = _sbt)
            {
                _rtExtention.GetRayTracingShaderGroupHandles(_logicalDevice, _rtPipeline, 0, _groupCount, _sbtSize, _ptr);
            }

            CopyHandles(ref _raygenBTDM, (int)_handleSize, 0, _sbt);
            CopyHandles(ref _missBTDM, (int)_handleSize, (int)_handleSizeAligned, _sbt);
            CopyHandles(ref _hitBTDM, (int)_handleSize, (int)_handleSizeAligned *2, _sbt);
        }

        private void CopyHandles(ref DeviceMemory _memory, int _size, int _offset, byte[] _sbt)
        {
            void* _data;
            _vulkan.MapMemory(_logicalDevice, _memory, 0, (ulong)_size, 0, &_data);
            Marshal.Copy(_sbt, _offset, (nint)_data, _size);
            _vulkan.UnmapMemory(_logicalDevice, _memory);
        }

        private void DeleteScratchBuffer(ref PathtracingScratchBuffer _sBuffer)
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

        private void CreateScratchBuffer(ulong size, ref PathtracingScratchBuffer _b)
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

        private void CreateAccelerationStructureBuffer(ref AccelerationStruct _accelStructure, ref AccelerationStructureBuildSizesInfoKHR _buildSizeInfo)
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

        private void CreateStorageImage()
        {
            _storageDM = new DeviceMemory[_swapimageCount];
            _storageImage = new Image[_swapimageCount];
            _storageImageView = new ImageView[_swapimageCount];

            for (int i = 0; i < _swapimageCount; i++)
            {
                AVulkanBufferHandler.CreateImage(_extent.Width, _extent.Height, Format.R8G8B8A8Unorm, ImageTiling.Optimal, ImageUsageFlags.TransferSrcBit | ImageUsageFlags.StorageBit, MemoryPropertyFlags.DeviceLocalBit, ref _storageImage[i], ref _storageDM[i]);
                _swapchain.CreateImageView(ref _storageImageView[i], ref _storageImage[i], ImageAspectFlags.ColorBit, Format.R8G8B8A8Unorm);

                CommandBuffer _imageTransition = AVulkanBufferHandler.BeginSingleTimeCommands();

                ImageMemoryBarrier _barrier = new()
                {
                    SType = StructureType.ImageMemoryBarrier,
                    OldLayout = ImageLayout.Undefined,
                    NewLayout = ImageLayout.General,
                    SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
                    DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
                    Image = _storageImage[i],
                    SubresourceRange =
                    {
                    AspectMask = ImageAspectFlags.ColorBit,
                    BaseMipLevel = 0,
                    LevelCount = 1,
                    BaseArrayLayer = 0,
                    LayerCount = 1,
                    },
                    SrcAccessMask = 0,
                    DstAccessMask = AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit,
                };
                _vulkan.CmdPipelineBarrier(_imageTransition, PipelineStageFlags.TopOfPipeBit, PipelineStageFlags.ComputeShaderBit | PipelineStageFlags.RayTracingShaderBitKhr, 0, 0, null, 0, null, 1, _barrier);
                AVulkanBufferHandler.EndSingleTimeCommands(ref _imageTransition);
            }
        }

        private void CreateCommandBuffers()
        {
            _commandBuffer = new CommandBuffer[_swapimageCount];

            CommandBufferAllocateInfo _allocInfo = new CommandBufferAllocateInfo()
            {
                SType = StructureType.CommandBufferAllocateInfo,
                CommandPool = _commandPool,
                Level = CommandBufferLevel.Primary,
                CommandBufferCount = (uint)_commandBuffer.Length
            };
            fixed (CommandBuffer* _commandBufferPtr = _commandBuffer)
            {
                Result r = _vulkan.AllocateCommandBuffers(_logicalDevice, _allocInfo, _commandBufferPtr);
                if (r != Result.Success)
                {
                    throw new Exception("Failed to allocate command buffer with error " + r);
                }
            }

            ImageSubresourceRange _sr = new()
            {
                AspectMask = ImageAspectFlags.ColorBit,
                BaseArrayLayer = 0,
                BaseMipLevel = 0,
                LayerCount = 1,
                LevelCount = 1
            };

            for (int i = 0; i < _commandBuffer.Length; i++)
            {
                CommandBufferBeginInfo _beginInfo = new CommandBufferBeginInfo()
                {
                    SType = StructureType.CommandBufferBeginInfo
                };
                if (_vulkan.BeginCommandBuffer(_commandBuffer[i], _beginInfo) != Result.Success)
                {
                    throw new Exception("Failed to create BEGIN command buffer at index " + i);
                }
                //render code
                uint _handleSizeAligned = AlignedSize(_rtPipelineProperties.ShaderGroupHandleSize, _rtPipelineProperties.ShaderGroupHandleAlignment);
                StridedDeviceAddressRegionKHR _raygenShaderSbtEntry = new()
                {
                    DeviceAddress = GetAddress(_raygenBindingTable),
                    Stride = _handleSizeAligned,
                    Size = _handleSizeAligned
                };
                StridedDeviceAddressRegionKHR _missnShaderSbtEntry = new()
                {
                    DeviceAddress = GetAddress(_missBindingTable),
                    Stride = _handleSizeAligned,
                    Size = _handleSizeAligned
                };
                StridedDeviceAddressRegionKHR _hitShaderSbtEntry = new()
                {
                    DeviceAddress = GetAddress(_hitBinddingTable),
                    Stride = _handleSizeAligned,
                    Size = _handleSizeAligned
                };
                StridedDeviceAddressRegionKHR _callableShaderSbt = default;
                _vulkan.CmdBindPipeline(_commandBuffer[i], PipelineBindPoint.RayTracingKhr, _rtPipeline);
                _vulkan.CmdBindDescriptorSets(_commandBuffer[i], PipelineBindPoint.RayTracingKhr, _pipelineLayout, 0, 1, _testEnt.GetComponent<MeshComponent>()._descriptorSets[i], 0, null);
                _rtExtention.CmdTraceRays(
                    _commandBuffer[i],
                    &_raygenShaderSbtEntry,
                    &_missnShaderSbtEntry,
                    &_hitShaderSbtEntry,
                    &_callableShaderSbt,
                    _extent.Width,
                    _extent.Height,
                    1);
                SetImageLayout(ref _commandBuffer[i], ref _swapchain._swapchainImages[i], ImageLayout.Undefined, ImageLayout.TransferDstOptimal, _sr);
                SetImageLayout(ref _commandBuffer[i], ref _storageImage[i], ImageLayout.General, ImageLayout.TransferSrcOptimal, _sr);
                
                ImageCopy _ic = new ImageCopy()
                {
                    SrcSubresource =
                    {
                        AspectMask = ImageAspectFlags.ColorBit,
                        BaseArrayLayer = 0,
                        LayerCount = 1,
                        MipLevel = 0
                    },
                    SrcOffset = { X = 0, Y = 0, Z = 0},
                    DstSubresource =
                    {
                        AspectMask = ImageAspectFlags.ColorBit,
                        MipLevel = 0,
                        LayerCount = 1,
                        BaseArrayLayer = 0
                    },
                    DstOffset = { X = 0,Y = 0, Z = 0},
                    Extent =
                    {
                        Height = _extent.Height , Width = _extent.Width, Depth = 1
                    }
                };
                _vulkan.CmdCopyImage(_commandBuffer[i], _storageImage[i], ImageLayout.TransferSrcOptimal, _swapchain._swapchainImages[i], ImageLayout.TransferDstOptimal, 1, &_ic);

                SetImageLayout(ref _commandBuffer[i], ref _swapchain._swapchainImages[i], ImageLayout.TransferDstOptimal, ImageLayout.PresentSrcKhr, _sr);
                SetImageLayout(ref _commandBuffer[i], ref _storageImage[i], ImageLayout.TransferSrcOptimal, ImageLayout.General, _sr);
                //done queueing rendering commands
                if (_vulkan.EndCommandBuffer(_commandBuffer[i]) != Result.Success)
                {
                    throw new Exception("Failed to record command buffer");
                }
            }
        }

        internal override void Draw()
        {
            Result r;
            _camera.ProcessKeyboard();
            r = _vulkan.WaitForFences(_logicalDevice, 1, _fencesInFlight[_currentFrame], true, ulong.MaxValue);
            uint _imageIndex = 0;
            r = _swapchain._driverSwapchain.AcquireNextImage(_logicalDevice, _swapchain._swapchainKHR, ulong.MaxValue, _imageAvailableSemaphores[_currentFrame], default, ref _imageIndex);

            if (r == Result.ErrorOutOfDateKhr)
            {
                //RecreateSwapChain();
                return;
            }
            else if (r != Result.Success && r != Result.SuboptimalKhr)
            {
                throw new Exception("Failed to acquire swapchain image");
            }

            _camera.UpdateCameraMatrix(_extent, _imageIndex);
            //cia turetu buti kitu buffer updates
            //-----------------------------------
            if (_imagesInFlight[_imageIndex].Handle != default)
            {
                r = _vulkan.WaitForFences(_logicalDevice, 1, _imagesInFlight[_imageIndex], true, ulong.MaxValue);
            }
            _imagesInFlight[_imageIndex] = _fencesInFlight[_currentFrame];

            SubmitInfo _submitInfo = new SubmitInfo()
            {
                SType = StructureType.SubmitInfo
            };

            var _waitSemaphores = stackalloc[]
            {
                _imageAvailableSemaphores[_currentFrame]
            };
            var _waitStages = stackalloc[]
            {
                PipelineStageFlags.RayTracingShaderBitKhr
            };

            CommandBuffer _buffer = _commandBuffer[_imageIndex];
            _submitInfo = _submitInfo with
            {
                WaitSemaphoreCount = 1,
                PWaitSemaphores = _waitSemaphores,
                PWaitDstStageMask = _waitStages,

                CommandBufferCount = 1,
                PCommandBuffers = &_buffer
            };

            var _signalSemaphores = stackalloc[]
            {
                _renderFinishedSemaphores[_currentFrame]
            };

            _submitInfo = _submitInfo with
            {
                SignalSemaphoreCount = 1,
                PSignalSemaphores = _signalSemaphores
            };

            _vulkan.ResetFences(_logicalDevice, 1, _fencesInFlight[_currentFrame]);
            r = _vulkan.QueueSubmit(_graphicsQueue, 1, _submitInfo, _fencesInFlight[_currentFrame]);
            if (r != Result.Success)
            {
                throw new Exception("command buffer is not recorded or invalid:" + r);
            }

            var _swapChains = stackalloc[] { _swapchain._swapchainKHR };
            PresentInfoKHR _presentInfo = new PresentInfoKHR()
            {
                SType = StructureType.PresentInfoKhr,
                WaitSemaphoreCount = 1,
                PWaitSemaphores = _signalSemaphores,
                SwapchainCount = 1,
                PSwapchains = _swapChains,
                PImageIndices = &_imageIndex
            };
            r = _swapchain._driverSwapchain.QueuePresent(_presentQueue, _presentInfo);
            if (r == Result.ErrorOutOfDateKhr || r == Result.SuboptimalKhr || _glWindow._frameBufferResized)
            {
                _glWindow._frameBufferResized = false;
                //RecreateSwapChain();
            }
            else if (r != Result.Success)
            {
                throw new Exception("Failed to present swap chain image");
            }
            _currentFrame = (_currentFrame + 1) % MAX_FRAMES_IN_FLIGHT;
        }

        private void SetImageLayout(ref CommandBuffer _cBuffer, ref Image _image, ImageLayout _oldLayout, ImageLayout _newLayout, ImageSubresourceRange _subresource)
        {
            ImageMemoryBarrier _barrier = new()
            {
                SType = StructureType.ImageMemoryBarrier,
                OldLayout = _oldLayout,
                NewLayout = _newLayout,
                SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
                DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
                Image = _image,
                SubresourceRange = _subresource
            };
            PipelineStageFlags sourceStage;
            PipelineStageFlags destinationStage;

            if (_oldLayout == ImageLayout.Undefined && _newLayout == ImageLayout.TransferDstOptimal)
            {
                _barrier.SrcAccessMask = 0;
                _barrier.DstAccessMask = AccessFlags.TransferWriteBit;

                sourceStage = PipelineStageFlags.TopOfPipeBit;
                destinationStage = PipelineStageFlags.TransferBit;
            }
            else if (_oldLayout == ImageLayout.TransferDstOptimal && _newLayout == ImageLayout.PresentSrcKhr)
            {
                _barrier.SrcAccessMask = AccessFlags.TransferWriteBit;
                _barrier.DstAccessMask = AccessFlags.MemoryReadBit;

                sourceStage = PipelineStageFlags.TransferBit;
                destinationStage = PipelineStageFlags.BottomOfPipeBit;
            }
            else if (_oldLayout == ImageLayout.TransferSrcOptimal && _newLayout == ImageLayout.General)
            {
                _barrier.SrcAccessMask = AccessFlags.TransferReadBit;
                _barrier.DstAccessMask = AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit;

                sourceStage = PipelineStageFlags.TransferBit;
                destinationStage = PipelineStageFlags.RayTracingShaderBitKhr;
            }
            else if (_oldLayout == ImageLayout.General && _newLayout == ImageLayout.TransferSrcOptimal)
            {
                _barrier.SrcAccessMask = AccessFlags.ShaderWriteBit;
                _barrier.DstAccessMask = AccessFlags.TransferReadBit;

                sourceStage = PipelineStageFlags.RayTracingShaderBitKhr;
                destinationStage = PipelineStageFlags.TransferBit;
            }
            else
            {
                throw new Exception("unsupported layout transition!");
            }
            _vulkan!.CmdPipelineBarrier(_cBuffer, sourceStage, destinationStage, 0, 0, null, 0, null, 1, &_barrier);
        }

        private void CreateSyncObjects()
        {
            _imageAvailableSemaphores = new Semaphore[MAX_FRAMES_IN_FLIGHT];
            _renderFinishedSemaphores = new Semaphore[MAX_FRAMES_IN_FLIGHT];
            _fencesInFlight = new Fence[MAX_FRAMES_IN_FLIGHT];
            _imagesInFlight = new Fence[_swapchain._swapchainImages.Length];

            SemaphoreCreateInfo _semaphoreCreateInfo = new SemaphoreCreateInfo()
            {
                SType = StructureType.SemaphoreCreateInfo
            };

            FenceCreateInfo _fenceCreateInfo = new FenceCreateInfo()
            {
                SType = StructureType.FenceCreateInfo,
                Flags = FenceCreateFlags.SignaledBit
            };

            for (int i = 0; i < MAX_FRAMES_IN_FLIGHT; i++)
            {
                if (_vulkan.CreateSemaphore(_logicalDevice, _semaphoreCreateInfo, null, out _imageAvailableSemaphores[i]) != Result.Success ||
                    _vulkan.CreateSemaphore(_logicalDevice, _semaphoreCreateInfo, null, out _renderFinishedSemaphores[i]) != Result.Success ||
                    _vulkan.CreateFence(_logicalDevice, _fenceCreateInfo, null, out _fencesInFlight[i]) != Result.Success)
                {
                    throw new Exception("Failed to create synch objects for a frame at index " + i);
                }
            }
        }

        private ulong GetAddress(Buffer _b)
        {
            BufferDeviceAddressInfo _addressInfo = new BufferDeviceAddressInfo()
            {
                SType = StructureType.BufferDeviceAddressInfo,
                Buffer = _b,
            };
            return _vulkan.GetBufferDeviceAddress(_logicalDevice, _addressInfo);
        }

        private ShaderModule CreateShaderModule(byte[] _shaderCode)
        {
            ShaderModuleCreateInfo _createInfo = new ShaderModuleCreateInfo
            {
                SType = StructureType.ShaderModuleCreateInfo,
                CodeSize = (nuint)_shaderCode.Length,
            };
            ShaderModule _shaderModule;

            fixed (byte* _shaderCodePtr = _shaderCode)
            {
                _createInfo.PCode = (uint*)_shaderCodePtr;
                if (Rasterizer._vulkan.CreateShaderModule(Rasterizer._logicalDevice, _createInfo, null, out _shaderModule) != Result.Success)
                {
                    throw new Exception("Failed to create shader module");
                }
            }
            return _shaderModule;
        }

        private byte[] ReadFile(string FileName)
        {
            byte[] contents = File.ReadAllBytes(FileName);
            return contents;
        }

        private uint AlignedSize(uint _value, uint _alignment)
        {
            uint a = (_value + _alignment - 1) & ~(_alignment - 1);
            return a;
        }
    }
}