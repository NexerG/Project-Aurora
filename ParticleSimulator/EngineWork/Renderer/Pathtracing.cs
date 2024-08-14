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

namespace ArctisAurora.EngineWork.Renderer
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
        Buffer _hitBinddingTable;
        DeviceMemory _hitBTDM;


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

            PhysicalDeviceFeatures _deviceFeatures = new PhysicalDeviceFeatures()
            {
            };
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
                    DescriptorCount = (uint)((_swapimageCount * _entitiesToRender.Count) + 1)
                },
                new DescriptorPoolSize()
                {
                    Type = DescriptorType.StorageImage,
                    DescriptorCount = (uint)((_swapimageCount * _entitiesToRender.Count) + 1)
                },
                new DescriptorPoolSize()
                {
                    Type = DescriptorType.UniformBuffer,
                    DescriptorCount = (uint)((_swapimageCount * _entitiesToRender.Count) + 1)
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
                    MaxSets = (uint)(_swapimageCount * 3 * _entitiesToRender.Count + 1),
                };
                if (_vulkan.CreateDescriptorPool(_logicalDevice, _createInfo, null, _dpPtr) != Result.Success)
                {
                    throw new Exception("Failed to create descriptor pool");
                }
            }
        }

        private void CreatePathtracingDescriptorSetLayout()
        {
            List<DescriptorType> _types1 = new List<DescriptorType> { DescriptorType.AccelerationStructureKhr, DescriptorType.StorageImage, DescriptorType.UniformBuffer };
            List<ShaderStageFlags> _flags1 = new List<ShaderStageFlags> { ShaderStageFlags.RaygenBitKhr, ShaderStageFlags.RaygenBitKhr, ShaderStageFlags.RaygenBitKhr };
            CreateDescriptorSetLayout(_types1.Count, _types1, _flags1, ref _descriptorSetLayout);
        }

        private void CreateRaytracingPipeline()
        {
            fixed (DescriptorSetLayout* _setPtr = &_descriptorSetLayout)
            {
                PipelineLayoutCreateInfo _pipelineLayoutCreateInfo = new()
                {
                    SType = StructureType.PipelineLayoutCreateInfo,
                    SetLayoutCount = 1,
                    PSetLayouts = _setPtr
                };
                fixed (PipelineLayout* _pipePtr = &_pipelineLayout)
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
            Result r = _rtExtention.CreateRayTracingPipelines(_logicalDevice, default, default, 1, _rtPipelineCI, null, out _rtPipeline);
            if (r != Result.Success)
            {
                throw new Exception("Failed to create graphics pipeline " + r);
            }
        }

        private void CreateShaderBindingTable()
        {
            uint _handleSize = _rtPipelineProperties.ShaderGroupHandleSize;
            uint _handleSizeAligned = AVulkanHelper.AlignedSize(_handleSize, _rtPipelineProperties.ShaderGroupHandleAlignment);
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
                if (_vulkan.BeginCommandBuffer(_commandBuffer[i], _beginInfo) != Result.Success)
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
                    Size = _handleSizeAligned
                };
                StridedDeviceAddressRegionKHR _hitShaderSbtEntry = new()
                {
                    DeviceAddress = AVulkanHelper.GetBufferAdress(ref _hitBinddingTable),
                    Stride = _handleSizeAligned,
                    Size = _handleSizeAligned
                };
                StridedDeviceAddressRegionKHR _callableShaderSbt = default;
                _vulkan.CmdBindPipeline(_commandBuffer[i], PipelineBindPoint.RayTracingKhr, _rtPipeline);
                for (int j = 0; j < _entitiesToRender.Count; j++)
                {
                    var _offset = new ulong[] { 0 };
                    _entitiesToRender[j].GetComponent<MeshComponent>().EnqueueDrawCommands(_offset, i, ref _commandBuffer[i]);
                }
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

        internal override void RecreateCommandBuffers()
        {
            base.RecreateCommandBuffers();
            CreateCommandBuffers();
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
            int localEntityCount = 0;
            foreach (Entity e in _updateEntities)
            {
                e.GetComponent<MeshComponent>().UpdateMatrices();
                localEntityCount++;
            }
            _updateEntities.RemoveRange(0, localEntityCount);
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