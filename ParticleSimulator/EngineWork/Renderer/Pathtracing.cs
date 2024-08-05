using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;
using Assimp;
using Silk.NET.Vulkan.Extensions.KHR;
using ArctisAurora.EngineWork.ECS.RenderingComponents.Vulkan;
using ArctisAurora.EngineWork.Renderer.Helpers;
using ArctisAurora.CustomEntities;
using Silk.NET.Core.Native;
using Image = Silk.NET.Vulkan.Image;
using ImageLayout = Silk.NET.Vulkan.ImageLayout;

namespace ArctisAurora.EngineWork.Renderer
{
    internal unsafe class Pathtracing : RendererBaseClass
    {
        //temporary
        TestingEntity _testEnt;
        //

        string[] requiredExtensions =
        {
                "VK_KHR_swapchain",
                "VK_KHR_ray_tracing_pipeline",
                "VK_KHR_deferred_host_operations",
                "VK_KHR_buffer_device_address",
                "VK_EXT_descriptor_indexing",
                "VK_KHR_spirv_1_4",
                "VK_KHR_shader_float_controls",
                KhrAccelerationStructure.ExtensionName
        };

        PhysicalDeviceRayTracingPipelinePropertiesKHR _rtPipelineProperties = default;
        PhysicalDeviceRayTracingPipelineFeaturesKHR _rtPipelineFeatures = default;

        PhysicalDeviceBufferDeviceAddressFeatures _gpuAddressFeatures = default;
        PhysicalDeviceRayTracingPipelineFeaturesKHR _enabledRTPipelineFeatures = default;
        PhysicalDeviceAccelerationStructureFeaturesKHR _accelerationStructures = default;

        DeviceOrHostAddressConstKHR _addressVertex = default;
        DeviceOrHostAddressConstKHR _addressIndex = default;
        DeviceOrHostAddressConstKHR _addressTransform = default;

        KhrAccelerationStructure _accelerationStructure = default;
        AccelerationStruct _BLAS = default;
        internal static AccelerationStruct _TLAS = default;

        internal static AVulkanSwapchain _swapchain;
        internal Image _storageImage;
        internal DeviceMemory _storageDM;
        internal static ImageView _storageImageView;

        KhrRayTracingPipeline _rtExtention;
        internal static DescriptorSetLayout _descriptorSetLayout;
        PipelineLayout _pipelineLayout;
        Pipeline _rtPipeline;

        Buffer _raygenBindingTable;
        DeviceMemory _raygenBTDM;
        Buffer _missBindingTable;
        DeviceMemory _missBTDM; 
        Buffer _hitBinddingTable;
        DeviceMemory _hitBTDM;

        CommandBuffer[] _commandBuffer;

        internal static DescriptorPool _descriptorPool;

        internal Camera _camera;

        internal struct AccelerationStruct
        {
            internal AccelerationStructureKHR _handle;
            internal ulong _deviceAddress;
            internal DeviceMemory _memory;
            internal Buffer _buffer;
        }

        struct PathtracingScratchBuffer
        {
            internal ulong _deviceAddress;
            internal Buffer _buffer;
            internal DeviceMemory _memory;
        }

        internal Pathtracing() 
        {
            _rtPipelineProperties.SType = StructureType.PhysicalDeviceRayTracingPipelinePropertiesKhr;
            fixed(PhysicalDeviceRayTracingPipelinePropertiesKHR* _rtPtr = &_rtPipelineProperties)
            {
                PhysicalDeviceProperties2 _devprops2 = new PhysicalDeviceProperties2()
                {
                    SType = StructureType.PhysicalDeviceProperties2,
                    PNext = _rtPtr
                };
                _vulkan.GetPhysicalDeviceProperties2(_gpu, &_devprops2);
            }
            _rendererInstance = this;
            CreateLogicalDevice(requiredExtensions);

            int _graphicsQFamilyIndex = AVulkanHelper.FindQueueFamilyIndex(ref _gpu, ref _qfm, QueueFlags.GraphicsBit);
            uint _presentSupportIndex = AVulkanHelper.FindPresentSupportIndex(ref _qfm, ref _glWindow._driverSurface, ref _glWindow._surface);
            _graphicsQueue = _vulkan.GetDeviceQueue(_logicalDevice, (uint)_graphicsQFamilyIndex, 0);
            _presentQueue = _vulkan.GetDeviceQueue(_logicalDevice, _presentSupportIndex, 0);

            _swapchain = new AVulkanSwapchain(ref _glWindow._driverSurface, ref _glWindow._surface);
            _swapchain.DoSwapchainMethodSequence(ref _extent);        //swapchain methods for simplicity sake
            _swapimageCount = _swapchain._swapchainImages.Length;     //engine related thing

            CreateCommandPool();

            CreateDescriptorPool();
            CreateBLAS();
            CreateTLAS();
            CreateStorageImage();
            //create uniform buffer DONE
            CreateRaytracingPipeline();
            CreateShaderBindingTable();

            _testEnt.GetComponent<MeshComponent>().CreateRTDescriptorSet();
            CreateCommandBuffers();
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
                    Type = DescriptorType.StorageBuffer,
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
                    MaxSets = 3
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

            _addressVertex.DeviceAddress = GetAddress(_testEnt.GetComponent<MeshComponent>()._vertexBuffer);
            _addressIndex.DeviceAddress = GetAddress(_testEnt.GetComponent<MeshComponent>()._indexBuffer);
            _addressTransform.DeviceAddress = GetAddress(_testEnt.GetComponent<MeshComponent>()._trasnformsBuffer);

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
                        MaxVertex = (uint)(_testEnt.GetComponent<MeshComponent>()._mesh._vertices.Length),
                        VertexStride = (ulong)sizeof(Vertex),
                        IndexType = IndexType.Uint32,
                        IndexData = _addressIndex,
                        TransformData = _addressTransform
                    }
                }
            };

            AccelerationStructureBuildGeometryInfoKHR _accelStrGeomInfo = new()
            {
                SType = StructureType.AccelerationStructureBuildGeometryInfoKhr,
                Type = AccelerationStructureTypeKHR.BottomLevelKhr,
                Flags = BuildAccelerationStructureFlagsKHR.PreferFastTraceBitKhr,
                GeometryCount = 1,
                PGeometries = &_accelStrGeom,
            };

            uint numTris = 6;
            AccelerationStructureBuildSizesInfoKHR _asbsInfo = default;
            _asbsInfo.SType = StructureType.AccelerationStructureBuildSizesInfoKhr;
            _vulkan.TryGetDeviceExtension(_instance, _logicalDevice, out _accelerationStructure);
            _accelerationStructure.GetAccelerationStructureBuildSizes(_logicalDevice, AccelerationStructureBuildTypeKHR.DeviceKhr, &_accelStrGeomInfo, numTris, out _asbsInfo);
            CreateAccelerationStructureBuffer(ref _BLAS, _asbsInfo);

            AccelerationStructureCreateInfoKHR _accelerationStructureCreateInfo = new AccelerationStructureCreateInfoKHR()
            {
                SType = StructureType.AccelerationStructureCreateInfoKhr,
                Buffer = _BLAS._buffer,
                Size = _asbsInfo.AccelerationStructureSize,
                Type = AccelerationStructureTypeKHR.BottomLevelKhr
            };
            _accelerationStructure.CreateAccelerationStructure(_logicalDevice, _accelerationStructureCreateInfo, null, out _BLAS._handle);
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

            void* _data;
            ulong _size = ((ulong)(6 * sizeof(Vertex)));
            _vulkan.MapMemory(_logicalDevice, _BLAS._memory, 0, _size, 0, &_data);
            Span<AccelerationStructureBuildGeometryInfoKHR> _ASBGSpan = new Span<AccelerationStructureBuildGeometryInfoKHR>(_data, 1);
            _ASBGSpan[0] = _abgInfo;
            _vulkan.UnmapMemory(_logicalDevice, _BLAS._memory);
            _accelerationStructure.CmdBuildAccelerationStructures(_commandBuffer, 1, _ASBGSpan, &_asbrInfo);

            AVulkanBufferHandler.EndSingleTimeCommands(_commandBuffer);
            
            AccelerationStructureDeviceAddressInfoKHR _adaInfo = new AccelerationStructureDeviceAddressInfoKHR()
            {
                SType = StructureType.AccelerationStructureBuildGeometryInfoKhr,
                AccelerationStructure = _BLAS._handle
            };
            _BLAS._deviceAddress = _accelerationStructure.GetAccelerationStructureDeviceAddress(_logicalDevice, _adaInfo);
            DeleteScratchBuffer(ref _scratchBuffer);
        }

        private void CreateTLAS()
        {
            TransformMatrixKHR _matrix = new TransformMatrixKHR();
            _matrix.Matrix[0] = _testEnt.GetComponent<MeshComponent>()._transformMatrices[0].M11;
            _matrix.Matrix[1] = _testEnt.GetComponent<MeshComponent>()._transformMatrices[0].M12;
            _matrix.Matrix[2] = _testEnt.GetComponent<MeshComponent>()._transformMatrices[0].M13;
            _matrix.Matrix[3] = _testEnt.GetComponent<MeshComponent>()._transformMatrices[0].M14;
            _matrix.Matrix[4] = _testEnt.GetComponent<MeshComponent>()._transformMatrices[0].M21;
            _matrix.Matrix[5] = _testEnt.GetComponent<MeshComponent>()._transformMatrices[0].M22;
            _matrix.Matrix[6] = _testEnt.GetComponent<MeshComponent>()._transformMatrices[0].M23;
            _matrix.Matrix[7] = _testEnt.GetComponent<MeshComponent>()._transformMatrices[0].M24;
            _matrix.Matrix[8] = _testEnt.GetComponent<MeshComponent>()._transformMatrices[0].M31;
            _matrix.Matrix[9] = _testEnt.GetComponent<MeshComponent>()._transformMatrices[0].M32;
            _matrix.Matrix[10] = _testEnt.GetComponent<MeshComponent>()._transformMatrices[0].M33;
            _matrix.Matrix[11] = _testEnt.GetComponent<MeshComponent>()._transformMatrices[0].M34;

            AccelerationStructureInstanceKHR _accelerationInstance = new AccelerationStructureInstanceKHR()
            {
                Flags = GeometryInstanceFlagsKHR.TriangleFacingCullDisableBitKhr,
                AccelerationStructureReference = _BLAS._deviceAddress,
                Transform = _matrix,
                InstanceCustomIndex = 0,
                Mask = 0xFF,
                InstanceShaderBindingTableRecordOffset = 0
            };
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
                        Data = _addressTransform
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
            uint primitive_count = 6;
            AccelerationStructureBuildSizesInfoKHR _asbsInfo = new()
            {
                SType = StructureType.AccelerationStructureBuildSizesInfoKhr
            };
            _accelerationStructure.GetAccelerationStructureBuildSizes(_logicalDevice, AccelerationStructureBuildTypeKHR.DeviceKhr, &_asbgInfo, primitive_count, &_asbsInfo);

            CreateAccelerationStructureBuffer(ref _TLAS, _asbsInfo);

            AccelerationStructureCreateInfoKHR _asCreateInfo = new AccelerationStructureCreateInfoKHR()
            {
                SType = StructureType.AccelerationStructureCreateInfoKhr,
                Buffer = _TLAS._buffer,
                Size = _asbsInfo.AccelerationStructureSize,
                Type = AccelerationStructureTypeKHR.TopLevelKhr,
            };
            //
            _accelerationStructure.CreateAccelerationStructure(_logicalDevice, _asCreateInfo, null, out _TLAS._handle);
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

            CommandBuffer _commandBuffer = AVulkanBufferHandler.BeginSingleTimeCommands();

            void* _data;
            ulong _size = ((ulong)(6 * sizeof(Vertex)));
            _vulkan.MapMemory(_logicalDevice, _TLAS._memory, 0, _size, 0, &_data);
            Span<AccelerationStructureBuildGeometryInfoKHR> _ASBGSpan = new Span<AccelerationStructureBuildGeometryInfoKHR>(_data, 1);
            _ASBGSpan[0] = _abgInfo;
            _vulkan.UnmapMemory(_logicalDevice, _TLAS._memory);
            _accelerationStructure.CmdBuildAccelerationStructures(_commandBuffer, 1, _ASBGSpan, &_asbrInfo);

            AVulkanBufferHandler.EndSingleTimeCommands(_commandBuffer);

            AccelerationStructureDeviceAddressInfoKHR _adaInfo = new AccelerationStructureDeviceAddressInfoKHR()
            {
                SType = StructureType.AccelerationStructureBuildGeometryInfoKhr,
                AccelerationStructure = _TLAS._handle
            };
            _TLAS._deviceAddress = _accelerationStructure.GetAccelerationStructureDeviceAddress(_logicalDevice, _adaInfo);
            DeleteScratchBuffer(ref _scratchBuffer);
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
                DescriptorType = DescriptorType.StorageBuffer,
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
                        throw new Exception("Failed to create descriptor set layout");
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

            CopyHandles(_handleSize, ref _raygenBTDM, _groupCount, _sbtSize, 0);
            CopyHandles(_handleSize, ref _missBTDM, _groupCount, _sbtSize, _handleSizeAligned);
            CopyHandles(_handleSize, ref _hitBTDM, _groupCount, _sbtSize, _handleSizeAligned * 2);
        }

        private void CopyHandles(uint _handleSize, ref DeviceMemory _memory, uint _groupCount, uint _sbtSize, uint _handleSizeAligned)
        {
            void* _data;
            _vulkan.MapMemory(_logicalDevice, _memory, 0, _handleSize, 0, &_data);
            Span<uint> _shaderHandleStorage = new Span<uint>(_data, 1);
            _rtExtention.GetRayTracingShaderGroupHandles(_logicalDevice, _rtPipeline, 0, _groupCount, _sbtSize, _shaderHandleStorage);
            _shaderHandleStorage[0] = _shaderHandleStorage[0] + _handleSizeAligned;
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

        private void CreateAccelerationStructureBuffer(ref AccelerationStruct _accelStructure, AccelerationStructureBuildSizesInfoKHR _buildSizeInfo)
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
                MemoryTypeIndex = AVulkanBufferHandler.FindMemoryType(_memReqs.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit | MemoryPropertyFlags.HostVisibleBit)
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
            AVulkanBufferHandler.CreateImage(_extent.Width, _extent.Height, Format.R32G32B32A32Sfloat, ImageTiling.Optimal, ImageUsageFlags.TransferSrcBit | ImageUsageFlags.StorageBit, MemoryPropertyFlags.DeviceLocalBit, ref _storageImage, ref _storageDM);
            _swapchain.CreateImageView(ref _storageImageView, ref _storageImage, ImageAspectFlags.ColorBit, Format.R32G32B32A32Sfloat);

            CommandBuffer _imageTransition = AVulkanBufferHandler.BeginSingleTimeCommands();

            ImageMemoryBarrier _barrier = new()
            {
                SType = StructureType.ImageMemoryBarrier,
                OldLayout = Silk.NET.Vulkan.ImageLayout.Undefined,
                NewLayout   = Silk.NET.Vulkan.ImageLayout.General,
                SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
                DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
                Image = _storageImage,
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
            //Format _f = AVulkanHelper.FindSupportedFormat();
            _vulkan.CmdPipelineBarrier(_imageTransition, PipelineStageFlags.TopOfPipeBit, PipelineStageFlags.ComputeShaderBit | PipelineStageFlags.FragmentShaderBit, 0, 0, null, 0, null, 1, _barrier);
            AVulkanBufferHandler.EndSingleTimeCommands(_imageTransition);
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
                fixed(DescriptorSet* _dSetPtr = _testEnt.GetComponent<MeshComponent>()._descriptorSetsPathTracing)
                    _vulkan.CmdBindDescriptorSets(_commandBuffer[i], PipelineBindPoint.RayTracingKhr, _pipelineLayout, 0, 1, _dSetPtr, 0, 0);
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
                SetImageLayout(ref _commandBuffer[i], ref _storageImage, ImageLayout.General, ImageLayout.TransferSrcOptimal, _sr);
                
                ImageCopy _ic = new ImageCopy()
                {
                    SrcSubresource =
                    {
                        AspectMask = ImageAspectFlags.ColorBit,
                        BaseArrayLayer = 0,
                        LayerCount = 1,
                        MipLevel = 0
                    },
                    SrcOffset ={ X = 0, Y = 0, Z = 0},
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
                _vulkan.CmdCopyImage(_commandBuffer[i], _storageImage, ImageLayout.TransferSrcOptimal, _swapchain._swapchainImages[i], ImageLayout.TransferDstOptimal, 1, &_ic);

                SetImageLayout(ref _commandBuffer[i], ref _swapchain._swapchainImages[i], ImageLayout.TransferDstOptimal, ImageLayout.PresentSrcKhr, _sr);
                SetImageLayout(ref _commandBuffer[i], ref _storageImage, ImageLayout.TransferSrcOptimal, ImageLayout.General, _sr);
                //done rendering
                if (_vulkan.EndCommandBuffer(_commandBuffer[i]) != Result.Success)
                {
                    throw new Exception("Failed to record command buffer");
                }
            }
        }

        private void SetImageLayout(ref CommandBuffer _cBuffer, ref Image _swapchainImage, ImageLayout _oldLayout, ImageLayout _newLayout, ImageSubresourceRange _subresource)
        {
            ImageMemoryBarrier _barrier = new()
            {
                SType = StructureType.ImageMemoryBarrier,
                OldLayout = _oldLayout,
                NewLayout = _newLayout,
                SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
                DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
                Image = _swapchainImage,
                SubresourceRange =
                {
                    AspectMask = ImageAspectFlags.ColorBit,
                    BaseMipLevel = 0,
                    LevelCount = 1,
                    BaseArrayLayer = 0,
                    LayerCount = 1,
                }
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
                _barrier.DstAccessMask = 0;

                sourceStage = PipelineStageFlags.TransferBit;
                destinationStage = PipelineStageFlags.TopOfPipeBit;
            }
            else if (_oldLayout == ImageLayout.TransferSrcOptimal && _newLayout == ImageLayout.General)
            {
                _barrier.SrcAccessMask = AccessFlags.TransferWriteBit;
                _barrier.DstAccessMask = 0;

                sourceStage = PipelineStageFlags.TransferBit;
                destinationStage = PipelineStageFlags.TopOfPipeBit;
            }
            else if (_oldLayout == ImageLayout.General && _newLayout == ImageLayout.TransferSrcOptimal)
            {
                _barrier.SrcAccessMask = 0;
                _barrier.DstAccessMask = AccessFlags.ShaderReadBit;

                sourceStage = PipelineStageFlags.TransferBit;
                destinationStage = PipelineStageFlags.TopOfPipeBit;
            }
            else
            {
                throw new Exception("unsupported layout transition!");
            }
            _vulkan!.CmdPipelineBarrier(_cBuffer, sourceStage, destinationStage, 0, 0, null, 0, null, 1, _barrier);
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
                if (VulkanRenderer._vulkan.CreateShaderModule(VulkanRenderer._logicalDevice, _createInfo, null, out _shaderModule) != Result.Success)
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
            return (_value + _alignment - 1) * ~(_alignment - 1);
        }
    }
}