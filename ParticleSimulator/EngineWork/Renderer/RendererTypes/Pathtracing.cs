using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using Silk.NET.Core.Native;
using ArctisAurora.EngineWork.ECS.RenderingComponents.Vulkan;
using ArctisAurora.EngineWork.Renderer.Helpers;
using ArctisAurora.GameObject;
using System.Runtime.InteropServices;
using Buffer = Silk.NET.Vulkan.Buffer;
using Image = Silk.NET.Vulkan.Image;
using ImageLayout = Silk.NET.Vulkan.ImageLayout;
using static ArctisAurora.EngineWork.Renderer.MeshSubComponents.MCRaytracing;
using Silk.NET.Maths;
using System.Runtime.CompilerServices;
using ArctisAurora.EngineWork.Renderer.MeshSubComponents;

namespace ArctisAurora.EngineWork.Renderer.RendererTypes
{
    internal unsafe class Pathtracing : VulkanRenderer
    {
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
                "VK_EXT_scalar_block_layout"
        };
        //
        PhysicalDeviceRayTracingPipelinePropertiesKHR _rtPipelineProperties = default;
        PhysicalDeviceRayTracingPipelineFeaturesKHR _rtPipelineFeatures = default;
        //
        internal static KhrAccelerationStructure _accelerationStructure = default;
        //
        internal Image[] _storageImage;
        internal DeviceMemory[] _storageDM;
        internal static ImageView[] _storageImageView;
        //
        KhrRayTracingPipeline _rtExtention;
        internal static PipelineLayout _pipelineLayout;
        internal static Pipeline _rtPipeline;
        //
        Buffer _raygenBindingTable;
        DeviceMemory _raygenBTDM;
        Buffer _missBindingTable;
        DeviceMemory _missBTDM;
        //Buffer _shadowBindingTable;
        //DeviceMemory _shadowBTDM;
        Buffer _hitBinddingTable;
        DeviceMemory _hitBTDM;
        //
        internal static AccelerationStruct _TLAS = default;
        static DeviceOrHostAddressConstKHR _addressInstance = default;
        internal Buffer _accelerationInstanceBuffer;
        internal static DeviceMemory _accelerationInstanceDM;
        //
        DescriptorSetLayout _DSLIndexed;
        DescriptorSet[] _descriptorSets = new DescriptorSet[3];

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

            _vulkan.TryGetDeviceExtension(_instance, _logicalDevice, out _accelerationStructure);
            _vulkan.TryGetDeviceExtension(_instance, _logicalDevice, out _rtExtention);

            CreateCommandPool();
            CreateStorageImage();
            CreateDescriptorPool();
            CreatePathtracingDescriptorSetLayout();
            CreateRaytracingPipeline();
            CreateShaderBindingTable();

            CreateCommandBuffers();
            CreateSyncObjects();                                        //CPU - GPU sync logic
        }

        private void setup()
        {
            _rendererInstance = this;

            PhysicalDeviceFeatures _deviceFeatures = new PhysicalDeviceFeatures() { };

            PhysicalDeviceRayTracingPipelineFeaturesKHR _rtPipelineFeature = new PhysicalDeviceRayTracingPipelineFeaturesKHR()
            {
                SType = StructureType.PhysicalDeviceRayTracingPipelineFeaturesKhr,
                RayTracingPipeline = true,
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
                ScalarBlockLayout = true,
                DescriptorBindingVariableDescriptorCount = true,
                PNext = &accelerationStructureFeatures
            };
            CreateLogicalDevice(requiredExtensions, _vulkan12FT, _deviceFeatures);
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

        internal override void AddEntityToRenderQueue(Entity _m)
        {
            base.AddEntityToRenderQueue(_m);
            CreateDescriptorPool();
            if (_TLAS._handle.Handle != 0)
            {
                _accelerationStructure.DestroyAccelerationStructure(_logicalDevice, _TLAS._handle, null);
                _vulkan.FreeMemory(_logicalDevice, _TLAS._memory, null);
                _vulkan.DestroyBuffer(_logicalDevice, _TLAS._buffer, null);
            }
            CreateTLAS();
            for (int i = 0; i < _entitiesToRender.Count; i++)
                _entitiesToRender[i].GetComponent<MeshComponent>().ReinstantiateDesriptorSets();

            RecreateCommandBuffers();
        }

        internal override void AddLightToRenderQueue(Entity _m)
        {
            base.AddLightToRenderQueue(_m);
        }

        internal override void CreateDescriptorPool()
        {
            var _poolSizes = new DescriptorPoolSize[]
            {
                new DescriptorPoolSize()
                {
                    Type = DescriptorType.AccelerationStructureKhr,
                    DescriptorCount = (uint)(_swapimageCount)
                },
                new DescriptorPoolSize()
                {
                    Type = DescriptorType.StorageImage,
                    DescriptorCount = (uint)(_swapimageCount)
                },
                new DescriptorPoolSize()
                {
                    Type = DescriptorType.UniformBuffer,
                    DescriptorCount = (uint)(_swapimageCount * _entitiesToRender.Count + 1)
                },
                new DescriptorPoolSize()
                {
                    Type = DescriptorType.StorageBuffer,
                    DescriptorCount = (uint)(_swapimageCount * _entitiesToRender.Count + 1) * 2
                }
            };

            fixed (DescriptorPoolSize* _poolSizePtr = _poolSizes)
            fixed (DescriptorPool* _dpPtr = &_descriptorPool)
            {
                DescriptorPoolCreateInfo _createInfo = new DescriptorPoolCreateInfo()
                {
                    SType = StructureType.DescriptorPoolCreateInfo,
                    PoolSizeCount = (uint)_poolSizes.Length,
                    PPoolSizes = _poolSizePtr,
                    MaxSets = (uint)(_swapimageCount * 3 * _entitiesToRender.Count + 1),
                    Flags = DescriptorPoolCreateFlags.FreeDescriptorSetBit
                };
                if (_vulkan.CreateDescriptorPool(_logicalDevice, ref _createInfo, null, _dpPtr) != Result.Success)
                {
                    throw new Exception("Failed to create descriptor pool");
                }
            }
        }

        private void CreatePathtracingDescriptorSetLayout()
        {
            /// <summary>
            /// for every piece of data we want to use in shaders:
            ///     first we set the type of the data referenced in shaders.
            ///     then we set in which shader stage it is going to be used.
            ///     then we tell each desriptor (data piece) how many it will hold.
            ///     then we tell which descriptors (data pieces) are variable.
            /// </summary>

            List<DescriptorType> _types1 = new List<DescriptorType> { DescriptorType.AccelerationStructureKhr, DescriptorType.StorageImage, DescriptorType.UniformBuffer, DescriptorType.StorageBuffer, DescriptorType.StorageBuffer, DescriptorType.UniformBuffer };
            List<ShaderStageFlags> _flags1 = new List<ShaderStageFlags> { ShaderStageFlags.RaygenBitKhr | ShaderStageFlags.ClosestHitBitKhr, ShaderStageFlags.RaygenBitKhr, ShaderStageFlags.RaygenBitKhr, ShaderStageFlags.ClosestHitBitKhr, ShaderStageFlags.ClosestHitBitKhr, ShaderStageFlags.ClosestHitBitKhr };
            uint _indexedCount = 50000;
            //uint _indexedCount = (uint)_entitiesToRender.Count;
            uint[] _descriptorCount = { 1, 1, 1, _indexedCount, _indexedCount, _indexedCount };

            DescriptorBindingFlags[] _dbfEXT = {
                DescriptorBindingFlags.None, DescriptorBindingFlags.None, DescriptorBindingFlags.None,
                DescriptorBindingFlags.VariableDescriptorCountBit, DescriptorBindingFlags.VariableDescriptorCountBit, DescriptorBindingFlags.VariableDescriptorCountBit };

            CreateDescriptorSetLayout(_types1.Count, _types1, _flags1, ref _descriptorSetLayout, _dbfEXT, _descriptorCount);
        }

        private void CreateRaytracingPipeline()
        {
            DescriptorSetLayout[] _DSLayouts = new DescriptorSetLayout[1];
            _DSLayouts[0] = _descriptorSetLayout;
            fixed (DescriptorSetLayout* _setPtr = _DSLayouts)
            {
                PipelineLayoutCreateInfo _pipelineLayoutCreateInfo = new()
                {
                    SType = StructureType.PipelineLayoutCreateInfo,
                    SetLayoutCount = (uint)_DSLayouts.Length,
                    PSetLayouts = _setPtr
                };
                fixed (PipelineLayout* _pipePtr = &_pipelineLayout)
                    if (_vulkan.CreatePipelineLayout(_logicalDevice, ref _pipelineLayoutCreateInfo, null, _pipePtr) != Result.Success)
                    {
                        throw new Exception("Failed to create pipeline layout");
                    }
            }

            byte[] _rayGenCode = ReadFile("../../../Shaders/PathtracingShaders/raygen.rgen.spv");
            byte[] _missCode = ReadFile("../../../Shaders/PathtracingShaders/miss.rmiss.spv");
            byte[] _shadowCode = ReadFile("../../../Shaders/PathtracingShaders/shadows.rmiss.spv");
            byte[] _hitCode = ReadFile("../../../Shaders/PathtracingShaders/closesthit.rchit.spv");

            ShaderModule _raygenShader = CreateShaderModule(_rayGenCode);
            ShaderModule _missShader = CreateShaderModule(_missCode);
            ShaderModule _shadowShader = CreateShaderModule(_shadowCode);
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
            PipelineShaderStageCreateInfo _pipelineShaderStageShadowCI = new()
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.MissBitKhr,
                Module = _shadowShader,
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
                _pipelineShaderStageShadowCI,
                _pipelineShaderStageHitCI
            };
            // raygen
            RayTracingShaderGroupCreateInfoKHR _raygenCI = new()
            {
                SType = StructureType.RayTracingShaderGroupCreateInfoKhr,
                Type = RayTracingShaderGroupTypeKHR.GeneralKhr,
                GeneralShader = 0,
                ClosestHitShader = Vk.ShaderUnusedKhr,
                AnyHitShader = Vk.ShaderUnusedKhr,
                IntersectionShader = Vk.ShaderUnusedKhr,
            };
            // miss
            // general miss
            RayTracingShaderGroupCreateInfoKHR _missCI = new()
            {
                SType = StructureType.RayTracingShaderGroupCreateInfoKhr,
                Type = RayTracingShaderGroupTypeKHR.GeneralKhr,
                GeneralShader = 1,
                ClosestHitShader = Vk.ShaderUnusedKhr,
                AnyHitShader = Vk.ShaderUnusedKhr,
                IntersectionShader = Vk.ShaderUnusedKhr,
            };
            // shadows miss
            RayTracingShaderGroupCreateInfoKHR _shadowCI = new()
            {
                SType = StructureType.RayTracingShaderGroupCreateInfoKhr,
                Type = RayTracingShaderGroupTypeKHR.GeneralKhr,
                GeneralShader = 2,
                ClosestHitShader = Vk.ShaderUnusedKhr,
                AnyHitShader = Vk.ShaderUnusedKhr,
                IntersectionShader = Vk.ShaderUnusedKhr,
            };
            // hit
            RayTracingShaderGroupCreateInfoKHR _closesCI = new()
            {
                SType = StructureType.RayTracingShaderGroupCreateInfoKhr,
                Type = RayTracingShaderGroupTypeKHR.TrianglesHitGroupKhr,
                GeneralShader = Vk.ShaderUnusedKhr,
                ClosestHitShader = 3,
                AnyHitShader = Vk.ShaderUnusedKhr,
                IntersectionShader = Vk.ShaderUnusedKhr,
            };
            var _shaderGroups = stackalloc[]
                { _raygenCI , _missCI, _shadowCI, _closesCI};
            //
            //
            RayTracingPipelineCreateInfoKHR _rtPipelineCI = new()
            {
                SType = StructureType.RayTracingPipelineCreateInfoKhr,
                StageCount = 4,
                PStages = _stages,
                GroupCount = 4,
                PGroups = _shaderGroups,
                MaxPipelineRayRecursionDepth = Math.Min(2, _rtPipelineProperties.MaxRayRecursionDepth),
                Layout = _pipelineLayout
            };
            Result r = _rtExtention.CreateRayTracingPipelines(_logicalDevice, default, default, 1, ref _rtPipelineCI, null, out _rtPipeline);
            if (r != Result.Success)
            {
                throw new Exception("Failed to create graphics pipeline " + r);
            }
        }

        private void CreateShaderBindingTable()
        {
            uint _handleSize = _rtPipelineProperties.ShaderGroupHandleSize;
            uint _handleSizeAligned = AVulkanHelper.AlignedSize(_handleSize, _rtPipelineProperties.ShaderGroupHandleAlignment);
            uint _groupCount = 4;
            uint _sbtSize = _groupCount * _handleSizeAligned;

            BufferUsageFlags _buf = BufferUsageFlags.ShaderBindingTableBitKhr | BufferUsageFlags.ShaderDeviceAddressBit;
            MemoryPropertyFlags _mpf = MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit;

            AVulkanBufferHandler.CreateBuffer(_handleSize, _buf, _mpf, ref _raygenBindingTable, ref _raygenBTDM);
            AVulkanBufferHandler.CreateBuffer(_handleSize * 2, _buf, _mpf, ref _missBindingTable, ref _missBTDM);
            AVulkanBufferHandler.CreateBuffer(_handleSize, _buf, _mpf, ref _hitBinddingTable, ref _hitBTDM);

            byte[] _sbt = new byte[_sbtSize];
            fixed (byte* _ptr = _sbt)
            {
                Result r = _rtExtention.GetRayTracingShaderGroupHandles(_logicalDevice, _rtPipeline, 0, _groupCount, _sbtSize, _ptr);
                if (r != Result.Success)
                {
                    throw new Exception("Failed to get raytracing shader group handles");
                }
            }

            CopyHandles(ref _raygenBTDM, (int)_handleSize, 0, _sbt);
            CopyHandles(ref _missBTDM, (int)_handleSize * 2, (int)_handleSizeAligned, _sbt);
            CopyHandles(ref _hitBTDM, (int)_handleSize, (int)_handleSizeAligned * 3, _sbt);
        }

        private void CopyHandles(ref DeviceMemory _memory, int _size, int _offset, byte[] _sbt)
        {
            void* _data;
            _vulkan.MapMemory(_logicalDevice, _memory, 0, (ulong)_size, 0, &_data);
            Marshal.Copy(_sbt, _offset, (nint)_data, _size);
            _vulkan.UnmapMemory(_logicalDevice, _memory);
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

        private void CreateTLAS()
        {
            List<AccelerationStructureInstanceKHR> _structures = new List<AccelerationStructureInstanceKHR>();
            foreach (Entity e in _entitiesToRender)
            {
                if (e.GetComponent<MCRaytracing>() != null)
                {
                    MCRaytracing component = e.GetComponent<MCRaytracing>();
                    //Quaternion<float> q = Quaternion<float>.CreateFromYawPitchRoll(30f * MathF.PI / 180f, 0, 0);
                    //Quaternion<float> q = Quaternion<float>.Identity;
                    Matrix4X4<float> _transform = Matrix4X4<float>.Identity;
                    /*_transform *= Matrix4X4.CreateScale(e.transform.scale);
                    _transform *= Matrix4X4.CreateFromQuaternion(q);
                    _transform *= Matrix4X4.CreateTranslation(e.transform.position);*/
                    _transform = Matrix4X4.Transpose(_transform);

                    TransformMatrixKHR _instanceMatrix = new TransformMatrixKHR();
                    Unsafe.CopyBlock(_instanceMatrix.Matrix, Unsafe.AsPointer(ref _transform), 48);

                    component._accelerationInstance = new AccelerationStructureInstanceKHR()
                    {
                        Flags = GeometryInstanceFlagsKHR.TriangleFacingCullDisableBitKhr,
                        AccelerationStructureReference = component._BLAS._deviceAddress,
                        Transform = _instanceMatrix,
                        InstanceCustomIndex = (uint)_entitiesToRender.IndexOf(e),
                        Mask = 0xFF,
                        InstanceShaderBindingTableRecordOffset = 0
                    };
                    _structures.Add(component._accelerationInstance);
                    //_structures.Add(_accInstance);
                }
            }
            ulong _bufferSize = (ulong)(sizeof(AccelerationStructureInstanceKHR) * _entitiesToRender.Count);
            AVulkanBufferHandler.CreateBuffer(
                _bufferSize,
                BufferUsageFlags.ShaderDeviceAddressBit | BufferUsageFlags.AccelerationStructureBuildInputReadOnlyBitKhr,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
                ref _accelerationInstanceBuffer, ref _accelerationInstanceDM);
            void* _data;
            _vulkan.MapMemory(_logicalDevice, _accelerationInstanceDM, 0, _bufferSize, 0, &_data);
            _structures.ToArray().AsSpan().CopyTo(new Span<AccelerationStructureInstanceKHR>(_data, _structures.Count));
            _vulkan.UnmapMemory(_logicalDevice, _accelerationInstanceDM);
            _addressInstance.DeviceAddress = AVulkanHelper.GetBufferAdress(ref _accelerationInstanceBuffer);

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
                        Data = _addressInstance,
                    }
                }
            };

            AccelerationStructureBuildGeometryInfoKHR _asbgInfo = new AccelerationStructureBuildGeometryInfoKHR()
            {
                SType = StructureType.AccelerationStructureBuildGeometryInfoKhr,
                Type = AccelerationStructureTypeKHR.TopLevelKhr,
                Flags = BuildAccelerationStructureFlagsKHR.PreferFastTraceBitKhr | BuildAccelerationStructureFlagsKHR.AllowUpdateBitKhr,
                GeometryCount = 1,
                PGeometries = &_asg
            };
            uint primitive_count = (uint)_entitiesToRender.Count;
            AccelerationStructureBuildSizesInfoKHR _asbsInfo = new()
            {
                SType = StructureType.AccelerationStructureBuildSizesInfoKhr
            };
            _accelerationStructure.GetAccelerationStructureBuildSizes(_logicalDevice, AccelerationStructureBuildTypeKHR.DeviceKhr, &_asbgInfo, ref primitive_count, &_asbsInfo);
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
                Flags = BuildAccelerationStructureFlagsKHR.PreferFastTraceBitKhr | BuildAccelerationStructureFlagsKHR.AllowUpdateBitKhr,
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
        }

        internal static void UpdateTLAS()
        {
            List<AccelerationStructureInstanceKHR> _structures = new List<AccelerationStructureInstanceKHR>();
            foreach (Entity e in _entitiesToRender)
            {
                _structures.Add(e.GetComponent<MCRaytracing>()._accelerationInstance);
            }

            void* _data;
            _vulkan.MapMemory(_logicalDevice, _accelerationInstanceDM, 0, (ulong)sizeof(AccelerationStructureInstanceKHR), 0, &_data);
            _structures.ToArray().AsSpan().CopyTo(new Span<AccelerationStructureInstanceKHR>(_data, _structures.Count));
            _vulkan.UnmapMemory(_logicalDevice, _accelerationInstanceDM);

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
                Flags = BuildAccelerationStructureFlagsKHR.PreferFastTraceBitKhr | BuildAccelerationStructureFlagsKHR.AllowUpdateBitKhr,
                GeometryCount = 1,
                PGeometries = &_asg
            };
            uint primitive_count = 1;
            AccelerationStructureBuildSizesInfoKHR _asbsInfo = new()
            {
                SType = StructureType.AccelerationStructureBuildSizesInfoKhr
            };
            _accelerationStructure.GetAccelerationStructureBuildSizes(_logicalDevice, AccelerationStructureBuildTypeKHR.DeviceKhr, &_asbgInfo, primitive_count, &_asbsInfo);
            PathtracingScratchBuffer _scratchBuffer = new PathtracingScratchBuffer();
            CreateScratchBuffer(_asbsInfo.BuildScratchSize, ref _scratchBuffer);

            AccelerationStructureBuildGeometryInfoKHR _abgInfo = new AccelerationStructureBuildGeometryInfoKHR()
            {
                SType = StructureType.AccelerationStructureBuildGeometryInfoKhr,
                Type = AccelerationStructureTypeKHR.TopLevelKhr,
                Flags = BuildAccelerationStructureFlagsKHR.PreferFastTraceBitKhr | BuildAccelerationStructureFlagsKHR.AllowUpdateBitKhr,
                Mode = BuildAccelerationStructureModeKHR.UpdateKhr,
                SrcAccelerationStructure = _TLAS._handle,
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
                PrimitiveCount = (uint)_entitiesToRender.Count,
                PrimitiveOffset = 0,
                FirstVertex = 0,
                TransformOffset = 0
            };
            var _buildRangeInfos = stackalloc[]
            {
                _asbrInfo
            };

            CommandBuffer _commandBuffer = AVulkanBufferHandler.BeginSingleTimeCommands();
            _accelerationStructure.CmdBuildAccelerationStructures(_commandBuffer, 1, &_abgInfo, ref _buildRangeInfos);
            AVulkanBufferHandler.EndSingleTimeCommands(ref _commandBuffer);
            DeleteScratchBuffer(ref _scratchBuffer);
        }

        internal static void UpdateAccInstance(MCRaytracing component)
        {
            //Quaternion<float> q = Quaternion<float>.CreateFromYawPitchRoll(30f * MathF.PI / 180f, 0, 0);
            //Quaternion<float> q = Quaternion<float>.Identity;
            Matrix4X4<float> _transform = Matrix4X4<float>.Identity;
            /*_transform *= Matrix4X4.CreateScale(e.transform.scale);
            _transform *= Matrix4X4.CreateFromQuaternion(q);
            _transform *= Matrix4X4.CreateTranslation(e.transform.position);*/
            _transform = Matrix4X4.Transpose(_transform);

            TransformMatrixKHR _instanceMatrix = new TransformMatrixKHR();
            Unsafe.CopyBlock(_instanceMatrix.Matrix, Unsafe.AsPointer(ref _transform), 48);

            component._accelerationInstance = new AccelerationStructureInstanceKHR()
            {
                Flags = GeometryInstanceFlagsKHR.TriangleFacingCullDisableBitKhr,
                AccelerationStructureReference = component._BLAS._deviceAddress,
                Transform = _instanceMatrix,
                InstanceCustomIndex = (uint)_entitiesToRender.IndexOf(component.parent),
                Mask = 0xFF,
                InstanceShaderBindingTableRecordOffset = 0
            };
        }

        internal void CreateGlobalDescriptorSets()
        {
            /*if (_descriptorSets[0].Handle != 0)
            {
                fixed(DescriptorSet* descPtr = _descriptorSets)
                {
                    _vulkan.FreeDescriptorSets(_logicalDevice, _descriptorPool, (uint)_descriptorSets.Length, descPtr);
                }
            }*/
            DescriptorSetLayout[] _layouts = new DescriptorSetLayout[_swapimageCount];
            Array.Fill(_layouts, _descriptorSetLayout);

            fixed (DescriptorSetLayout* _layoutsPtr = _layouts)
            {
                uint bufferCount = (uint)_entitiesToRender.Count;
                uint[] entriesPer = { bufferCount, bufferCount, bufferCount };
                fixed (uint* perPtr = entriesPer)
                {
                    DescriptorSetVariableDescriptorCountAllocateInfo _variableDSCount = new()
                    {
                        SType = StructureType.DescriptorSetVariableDescriptorCountAllocateInfo,
                        DescriptorSetCount = (uint)_swapimageCount, // total amount of descriptor sets
                        PDescriptorCounts = perPtr                  // how many descriptor sets are variable
                    };

                    DescriptorSetAllocateInfo _allocateInfo = new DescriptorSetAllocateInfo()
                    {
                        SType = StructureType.DescriptorSetAllocateInfo,
                        DescriptorPool = _descriptorPool,
                        DescriptorSetCount = (uint)_swapimageCount,
                        PSetLayouts = _layoutsPtr,
                        PNext = &_variableDSCount
                    };

                    //_descriptorSets = new DescriptorSet[_swapimageCount];
                    fixed (DescriptorSet* _descriptorSetsPtr = _descriptorSets)
                    {
                        Result r = _vulkan.AllocateDescriptorSets(_logicalDevice, ref _allocateInfo, _descriptorSetsPtr);
                        if (r != Result.Success)
                        {
                            throw new Exception("Failed to allocate descriptor set with error code: " + r);
                        }
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

                    DescriptorBufferInfo[] _vertexBufferInfos = new DescriptorBufferInfo[_entitiesToRender.Count];
                    DescriptorBufferInfo[] _indexBufferInfos = new DescriptorBufferInfo[_entitiesToRender.Count];
                    DescriptorBufferInfo[] _transformUniformInfos = new DescriptorBufferInfo[_entitiesToRender.Count];
                    for (int k = 0; k < _entitiesToRender.Count;k++)
                    {
                        MCRaytracing component = _entitiesToRender[k].GetComponent<MCRaytracing>();
                        _vertexBufferInfos[k] = new DescriptorBufferInfo()
                        {
                            Buffer = component._vertexBuffer,
                            Offset = 0,
                            Range = (ulong)(sizeof(Vertex) * component._mesh._vertices.Length)
                        };
                        _transformUniformInfos[k] = new()
                        {
                            Buffer = component._transformsBuffer,
                            Offset = 0,
                            Range = (ulong)sizeof(float) * 12
                        };
                        _indexBufferInfos[k] = new()
                        {
                            Buffer = component._indexBuffer,
                            Offset = 0,
                            Range = (ulong)(sizeof(uint) * component._mesh._indices.Length)
                        };
                    }
                    fixed (DescriptorBufferInfo* _transforUniformInfoPtr = _transformUniformInfos)
                    fixed (DescriptorBufferInfo* _indexBufferInfoPtr = _indexBufferInfos)
                    fixed (DescriptorBufferInfo* _vertexBufferInfoPtr = _vertexBufferInfos)
                    {
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
                                DescriptorCount = (uint)_vertexBufferInfos.Length,
                                DstArrayElement = 0,
                                DescriptorType = DescriptorType.StorageBuffer,
                                PBufferInfo = _vertexBufferInfoPtr
                            },
                            new WriteDescriptorSet
                            {
                                SType = StructureType.WriteDescriptorSet,
                                DstSet = _descriptorSets[i],
                                DstBinding = 4,
                                DescriptorCount = (uint)_indexBufferInfos.Length,
                                DstArrayElement = 0,
                                DescriptorType = DescriptorType.StorageBuffer,
                                PBufferInfo = _indexBufferInfoPtr
                            },
                            // transform
                            new WriteDescriptorSet
                            {
                                SType = StructureType.WriteDescriptorSet,
                                DstSet = _descriptorSets[i],
                                DstBinding = 5,
                                DescriptorCount = (uint)_transformUniformInfos.Length,
                                DstArrayElement = 0,
                                DescriptorType = DescriptorType.UniformBuffer,
                                PBufferInfo = _transforUniformInfoPtr
                            }
                        };
                        fixed (WriteDescriptorSet* _descPtr = _writeDescriptorSets)
                        {
                            _vulkan!.UpdateDescriptorSets(_logicalDevice, (uint)_writeDescriptorSets.Length, _descPtr, 0, null);
                        }
                    }
                }
            }

        }

        internal override void CreateCommandBuffers()
        {
            base.CreateCommandBuffers();
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
                if (_vulkan.BeginCommandBuffer(_commandBuffer[i], ref _beginInfo) != Result.Success)
                {
                    throw new Exception("Failed to create BEGIN command buffer at index " + i);
                }
                //render code
                uint _handleSizeAligned = AVulkanHelper.AlignedSize(_rtPipelineProperties.ShaderGroupHandleSize, _rtPipelineProperties.ShaderGroupHandleAlignment);
                StridedDeviceAddressRegionKHR _raygenShaderSbtEntry = new()
                {
                    DeviceAddress = AVulkanHelper.GetBufferAdress(ref _raygenBindingTable),
                    Stride = _handleSizeAligned,
                    Size = _handleSizeAligned
                };
                StridedDeviceAddressRegionKHR _missnShaderSbtEntry = new()
                {
                    DeviceAddress = AVulkanHelper.GetBufferAdress(ref _missBindingTable),
                    Stride = _handleSizeAligned,
                    Size = _handleSizeAligned * 2
                };
                StridedDeviceAddressRegionKHR _hitShaderSbtEntry = new()
                {
                    DeviceAddress = AVulkanHelper.GetBufferAdress(ref _hitBinddingTable),
                    Stride = _handleSizeAligned,
                    Size = _handleSizeAligned
                };
                StridedDeviceAddressRegionKHR _callableShaderSbt = default;
                _vulkan.CmdBindPipeline(_commandBuffer[i], PipelineBindPoint.RayTracingKhr, _rtPipeline);
                var _offset = new ulong[] { 0 };
                DescriptorSet[] _allDS = new DescriptorSet[_entitiesToRender.Count];
                for (int j = 0; j < _entitiesToRender.Count; j++)
                {
                    //_entitiesToRender[j].GetComponent<MeshComponent>().EnqueueDrawCommands(ref _offset, i, ref _commandBuffer[i]);
                    //_allDS[j] = _entitiesToRender[j].GetComponent<MeshComponent>()._descriptorSets[i];
                }
                if (_entitiesToRender.Count > 0)
                {
                    /*fixed (DescriptorSet* ptr = _allDS)
                    {
                        _vulkan.CmdBindDescriptorSets(
                                _commandBuffer[i],
                                PipelineBindPoint.RayTracingKhr,
                                _pipelineLayout,
                                0,
                                (uint)_entitiesToRender.Count,
                                ptr,
                                0,
                                null
                            );
                    }*/
                    _vulkan.CmdBindDescriptorSets(_commandBuffer[i], PipelineBindPoint.RayTracingKhr, _pipelineLayout, 0, 1, ref _descriptorSets[i], 0, null);
                    _rtExtention.CmdTraceRays(
                        _commandBuffer[i],

                        &_raygenShaderSbtEntry,
                        &_missnShaderSbtEntry,
                        &_hitShaderSbtEntry,
                        &_callableShaderSbt,
                        _extent.Width,
                        _extent.Height,
                        1);
                }
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
                    SrcOffset = { X = 0, Y = 0, Z = 0 },
                    DstSubresource =
                    {
                        AspectMask = ImageAspectFlags.ColorBit,
                        MipLevel = 0,
                        LayerCount = 1,
                        BaseArrayLayer = 0
                    },
                    DstOffset = { X = 0, Y = 0, Z = 0 },
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

        internal override void RecreateCommandBuffers()
        {
            base.RecreateCommandBuffers();
            CreateCommandBuffers();
        }

        internal override void Draw()
        {
            Result r;
            _camera.ProcessKeyboard();
            r = _vulkan.WaitForFences(_logicalDevice, 1, ref _fencesInFlight[_currentFrame], true, ulong.MaxValue);
            uint _imageIndex = 0;
            r = _swapchain._driverSwapchain.AcquireNextImage(_logicalDevice, _swapchain._swapchainKHR, ulong.MaxValue, _imageAvailableSemaphores[_currentFrame], default, ref _imageIndex);

            if (r == Result.ErrorOutOfDateKhr)
            {
                RecreateSwapchain();
                return;
            }
            else if (r != Result.Success && r != Result.SuboptimalKhr)
            {
                throw new Exception("Failed to acquire swapchain image");
            }

            _camera.UpdateCameraMatrix(_extent, _imageIndex);
            //cia turetu buti kitu buffer updates
            int localEntityCount = 0;
            foreach (Entity e in _updateEntities)
            {
                e.transform._changed = false;
                e.GetComponent<MeshComponent>().UpdateMatrices();
                localEntityCount++;
            }
            if (localEntityCount > 0)
            {
                UpdateTLAS();
                _updateEntities.RemoveRange(0, localEntityCount);
            }
            //-----------------------------------
            if (_imagesInFlight[_imageIndex].Handle != default)
            {
                r = _vulkan.WaitForFences(_logicalDevice, 1, ref _imagesInFlight[_imageIndex], true, ulong.MaxValue);
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
                RecreateSwapchain();
            }
            else if (r != Result.Success)
            {
                throw new Exception("Failed to present swap chain image");
            }
            _currentFrame = (_currentFrame + 1) % MAX_FRAMES_IN_FLIGHT;
        }

        private void RecreateSwapchain()
        {
            //cleanup
            _glWindow.UpdateWindowSize(ref _extent);
            _vulkan.DeviceWaitIdle(_logicalDevice);
            CleanUpSwapChain();
            //visuals
            _swapchain.DoSwapchainMethodSequence(ref _extent);
            CreateRaytracingPipeline();
            //api calls
            for (int i = 0; i < _swapimageCount; i++)
            {
                _vulkan.FreeMemory(_logicalDevice, _storageDM[i], null);
                _vulkan.DestroyImage(_logicalDevice, _storageImage[i], null);
                _vulkan.DestroyImageView(_logicalDevice, _storageImageView[i], null);
            }
            CreateStorageImage();
            CreateDescriptorPool();
            CreateGlobalDescriptorSets();
            RecreateCommandBuffers();

            _imagesInFlight = new Fence[_swapchain._swapchainImages.Length];
        }

        private void CleanUpSwapChain()
        {
            _vulkan.DestroyRenderPass(_logicalDevice, _swapchain._renderPass, null);
            _vulkan.DestroyRenderPass(_logicalDevice, _swapchain._shadowmapRenderPass, null);
            _swapchain.DestroySwapchain();
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
                if (_vulkan.CreateShaderModule(_logicalDevice, _createInfo, null, out _shaderModule) != Result.Success)
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
    }
}