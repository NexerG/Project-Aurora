using ArctisAurora.EngineWork.Renderer.Helpers;
using Silk.NET.Core.Native;
using Silk.NET.GLFW;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;
using Image = Silk.NET.Vulkan.Image;
using ImageLayout = Silk.NET.Vulkan.ImageLayout;

namespace ArctisAurora.EngineWork.Renderer.RendererTypes
{
    internal unsafe class RadianceCascades2D : VulkanRenderer
    {
        string[] requiredExtensions =
        {
            "VK_KHR_swapchain",
            "VK_EXT_scalar_block_layout"
        };
        //
        Pipeline computePipeline;
        PipelineLayout computePipelineLayout;

        DescriptorPool probeDescriptorPool;
        DescriptorSetLayout probeDescriptorSetLayout;
        DescriptorSet[] probeDescriptorSets;
        Pipeline probePipeline;
        PipelineLayout probePipelineLayout;

        // images
        DeviceMemory[] storageImageDM;  // frame buffer image
        Image[] storageImage;
        ImageView[] storageImageView;
        DeviceMemory lightsDM;        // where the lights are in the 2D sceme image
        Image lightsImage;
        ImageView lightsImageView;
        DeviceMemory probesDM;        // probes
        Image probesImage;
        ImageView probesImageView;
        // Mouse position data
        Buffer mousePosBuffer;
        DeviceMemory mousePosMemory;


        struct ProbeLayer()
        {
            Vector3D<float> _startPos;
            float positionOffset;
            int rayCount;
            int rayLength;
            int rayOffset;
        }

        struct MouseData()
        {
            internal Vector4D<float> brushColor = new Vector4D<float>(1, 1, 1, 1);
            internal Vector2D<int> mousePos = new Vector2D<int>(0, 0);
            internal bool isLMBDown = false;
            internal bool padding1;
            internal bool padding2;
            internal bool padding3;
            internal bool isRMBDown = false;
            internal bool padding4;
            internal bool padding5;
            internal bool padding6;
        }
        MouseData mouseData = new MouseData();


        internal RadianceCascades2D()
        {
            Console.WriteLine(sizeof(bool));
            setup();
            //
            int _graphicsQFamilyIndex = AVulkanHelper.FindQueueFamilyIndex(ref _gpu, ref _qfm, QueueFlags.GraphicsBit);
            uint _presentSupportIndex = AVulkanHelper.FindPresentSupportIndex(ref _qfm, ref _glWindow._driverSurface, ref _glWindow._surface);
            _graphicsQueue = _vulkan.GetDeviceQueue(_logicalDevice, (uint)_graphicsQFamilyIndex, 0);
            _presentQueue = _vulkan.GetDeviceQueue(_logicalDevice, _presentSupportIndex, 0);

            //create the swapchain
            _swapchain = new Swapchain(ref _glWindow._driverSurface, ref _glWindow._surface);
            _swapchain.DoSwapchainMethodSequence(ref _extent);        //swapchain methods for simplicity sake
            _swapimageCount = _swapchain._swapchainImages.Length;     //engine related thing

            _camera = new AuroraCamera();
            CreateCommandPool();

            //create buffers
            AVulkanBufferHandler.CreateBuffer(ref mouseData, ref mousePosBuffer, ref mousePosMemory, BufferUsageFlags.UniformBufferBit);
            AVulkanBufferHandler.UpdateBuffer(ref mouseData, ref mousePosBuffer, ref mousePosMemory, BufferUsageFlags.UniformBufferBit);
            CreateImages();
            CreateDescriptorPool();
            CreateProbeDescriptorPool();
            CreateDescriptorsetlayout();
            
            setupProbes(1);
            
            UpdateDescriptorSet();
            
            CreateProbePipeline();
            CreateComputePipeline();


            CreateCommandBuffers();
            CreateSyncObjects();
        }

        private void setup()
        {
            _rendererInstance = this;
            PhysicalDeviceFeatures _deviceFeatures = new PhysicalDeviceFeatures()
            {
                SamplerAnisotropy = true
            };
            PhysicalDeviceVulkan12Features _vulkan12FT = new PhysicalDeviceVulkan12Features()
            {
                SType = StructureType.PhysicalDeviceVulkan12Features,
                BufferDeviceAddress = true,
                RuntimeDescriptorArray = true,
                DescriptorBindingVariableDescriptorCount = true,
                DescriptorIndexing = true
            };

            CreateLogicalDevice(requiredExtensions, _vulkan12FT, _deviceFeatures);        //abstract the gpu so we can communicate
        }

        private void setupProbes(int layers)
        {
            for (int i = 0; i < layers; i++)
            {

            }
            SetupProbeImages();
        }

        private void SetupProbeImages()
        {
            Extent2D imageSize = new Extent2D(2, 2);
            CreateImage(ref imageSize, ref probesImage, ref probesDM, ref probesImageView, Format.R8G8B8A8Unorm);
        }

        internal override void MouseUpdate(double xPos, double yPos)
        {
            // here we do mouse updates for the compute shader.
            mouseData.mousePos = new Vector2D<int>((int)xPos, (int)yPos);
            AVulkanBufferHandler.UpdateBuffer(ref mouseData, ref mousePosBuffer, ref mousePosMemory, BufferUsageFlags.UniformBufferBit);
        }

        internal override void MouseClick(MouseButton button, InputAction action)
        {
            //change logic into this vvv
            int buttonID = 0; // 0 = LMB 1 = RMB 2 = MMB
            buttonID = (int)button;
            // ^^^

            bool left = false;
            bool right = false;
            bool middle = false;
            switch (button)
            {
                case MouseButton.Left:
                    left = true;
                    break;
                case MouseButton.Right:
                    left = false;
                    break;
                case MouseButton.Middle:
                    break;
                default: break;
            }

            switch (action)
            {
                case InputAction.Press:
                    if (left)
                    {
                        mouseData.isLMBDown = true;
                    }
                    else
                    {
                        mouseData.isRMBDown = true;
                    }
                    break;
                case InputAction.Release:
                    if (left)
                    {
                        mouseData.isLMBDown = false;
                    }
                    else
                    {
                        mouseData.isRMBDown = false;
                    }
                    break;
                default: break;
            }
            AVulkanBufferHandler.UpdateBuffer(ref mouseData, ref mousePosBuffer, ref mousePosMemory, BufferUsageFlags.UniformBufferBit);
        }

        private void CreateDescriptorsetlayout()
        {
            List<DescriptorType> _types1 = new List<DescriptorType> {
                DescriptorType.StorageImage, DescriptorType.StorageImage, DescriptorType.StorageImage, DescriptorType.UniformBuffer};
            List<ShaderStageFlags> _flags1 = new List<ShaderStageFlags> {
                ShaderStageFlags.ComputeBit, ShaderStageFlags.ComputeBit, ShaderStageFlags.ComputeBit, ShaderStageFlags.ComputeBit };
            DescriptorBindingFlags[] _DBF = {
                DescriptorBindingFlags.None, DescriptorBindingFlags.None, DescriptorBindingFlags.None, DescriptorBindingFlags.None};

            CreateDescriptorSetLayout(_types1.Count, _types1, _flags1, ref _descriptorSetLayout, _DBF);

            List<DescriptorType> _types2 = new List<DescriptorType> {
                DescriptorType.StorageImage, DescriptorType.StorageImage };
            List<ShaderStageFlags> _flags2 = new List<ShaderStageFlags> {
                ShaderStageFlags.ComputeBit, ShaderStageFlags.ComputeBit };
            DescriptorBindingFlags[] _DBF2 = {
                DescriptorBindingFlags.None, DescriptorBindingFlags.None };

            CreateDescriptorSetLayout(_types2.Count, _types2, _flags2, ref probeDescriptorSetLayout, _DBF2);
        }

        private void CreateComputePipeline()
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
                fixed (PipelineLayout* _pipePtr = &computePipelineLayout)
                    if (_vulkan.CreatePipelineLayout(_logicalDevice, ref _pipelineLayoutCreateInfo, null, _pipePtr) != Result.Success)
                    {
                        throw new Exception("Failed to create pipeline layout");
                    }
            }

            ShaderModule computeShaderModule = LoadShader("../../../Shaders/RadianceCascades2D/Radiance.comp.spv");

            PipelineShaderStageCreateInfo shaderStageCreateInfo = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.ComputeBit,
                Module = computeShaderModule,
                PName = (byte*)SilkMarshal.StringToPtr("main")
            };

            ComputePipelineCreateInfo pipelineCreateInfo = new ComputePipelineCreateInfo
            {
                SType = StructureType.ComputePipelineCreateInfo,
                Stage = shaderStageCreateInfo,
                Layout = computePipelineLayout
            };
            if (_vulkan.CreateComputePipelines(_logicalDevice, default, 1, &pipelineCreateInfo, null, out computePipeline) != Result.Success)
                throw new Exception("Failed to create compute pipeline");

            _vulkan.DestroyShaderModule(_logicalDevice, computeShaderModule, null);
        }

        private void CreateProbePipeline()
        {
            DescriptorSetLayout[] _DSLayouts = new DescriptorSetLayout[1];
            _DSLayouts[0] = probeDescriptorSetLayout;
            fixed (DescriptorSetLayout* _setPtr = _DSLayouts)
            {
                PipelineLayoutCreateInfo _pipelineLayoutCreateInfo = new()
                {
                    SType = StructureType.PipelineLayoutCreateInfo,
                    SetLayoutCount = (uint)_DSLayouts.Length,
                    PSetLayouts = _setPtr
                };
                fixed (PipelineLayout* _pipePtr = &probePipelineLayout)
                    if (_vulkan.CreatePipelineLayout(_logicalDevice, ref _pipelineLayoutCreateInfo, null, _pipePtr) != Result.Success)
                    {
                        throw new Exception("Failed to create pipeline layout");
                    }
            }

            ShaderModule computeShaderModule = LoadShader("../../../Shaders/RadianceCascades2D/Radiance.Probes.comp.spv");

            PipelineShaderStageCreateInfo shaderStageCreateInfo = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.ComputeBit,
                Module = computeShaderModule,
                PName = (byte*)SilkMarshal.StringToPtr("main")
            };

            ComputePipelineCreateInfo pipelineCreateInfo = new ComputePipelineCreateInfo
            {
                SType = StructureType.ComputePipelineCreateInfo,
                Stage = shaderStageCreateInfo,
                Layout = probePipelineLayout
            };
            if (_vulkan.CreateComputePipelines(_logicalDevice, default, 1, &pipelineCreateInfo, null, out probePipeline) != Result.Success)
                throw new Exception("Failed to create compute pipeline");

            _vulkan.DestroyShaderModule(_logicalDevice, computeShaderModule, null);
        }

        private void CreateImages()
        {
            storageImage = new Image[_swapimageCount];
            storageImageDM = new DeviceMemory[_swapimageCount];
            storageImageView = new ImageView[_swapimageCount];

            lightsImage = new Image();
            lightsDM = new DeviceMemory();
            lightsImageView = new ImageView();

            for (int i = 0; i < _swapimageCount; i++)
            {
                CreateImage(ref _extent, ref storageImage[i], ref storageImageDM[i], ref storageImageView[i], Format.R8G8B8A8Unorm);
            }
            CreateImage(ref _extent, ref lightsImage, ref lightsDM, ref lightsImageView, Format.R8G8B8A8Unorm);
        }

        private void CreateImage(ref Extent2D size, ref Image image, ref DeviceMemory deviceMemory, ref ImageView imageView, Format format)
        {
            AVulkanBufferHandler.CreateImage(size.Width, size.Height, format, ImageTiling.Optimal, ImageUsageFlags.TransferSrcBit | ImageUsageFlags.StorageBit | ImageUsageFlags.TransferDstBit, MemoryPropertyFlags.DeviceLocalBit, ref image, ref deviceMemory);
            _swapchain.CreateImageView(ref imageView, ref image, ImageAspectFlags.ColorBit, format);

            CommandBuffer _imageTransition = AVulkanBufferHandler.BeginSingleTimeCommands();

            ImageMemoryBarrier _barrier = new()
            {
                SType = StructureType.ImageMemoryBarrier,
                OldLayout = ImageLayout.Undefined,
                NewLayout = ImageLayout.General,
                SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
                DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
                Image = image,
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
            _vulkan.CmdPipelineBarrier(_imageTransition, PipelineStageFlags.TopOfPipeBit, PipelineStageFlags.ComputeShaderBit, 0, 0, null, 0, null, 1, ref _barrier);
            AVulkanBufferHandler.EndSingleTimeCommands(ref _imageTransition);
        }

        internal override void CreateDescriptorPool()
        {
            var _poolSizes = new DescriptorPoolSize[]
            {
                new DescriptorPoolSize()
                {
                    Type = DescriptorType.StorageImage,
                    DescriptorCount = (uint)(_swapimageCount * 3)
                },
                new DescriptorPoolSize()
                {
                    Type = DescriptorType.UniformBuffer,
                    DescriptorCount = (uint)_swapimageCount
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
                    MaxSets = (uint)_swapimageCount,
                    Flags = DescriptorPoolCreateFlags.FreeDescriptorSetBit
                };
                if (_vulkan.CreateDescriptorPool(_logicalDevice, ref _createInfo, null, _dpPtr) != Result.Success)
                {
                    throw new Exception("Failed to create descriptor pool");
                }
            }
        }

        private void CreateProbeDescriptorPool()
        {
            var _poolSizes = new DescriptorPoolSize[]
            {
                new DescriptorPoolSize()
                {
                    Type = DescriptorType.StorageImage,
                    DescriptorCount = (uint)_swapimageCount
                }
            };
            fixed (DescriptorPoolSize* _poolSizePtr = _poolSizes)
            fixed (DescriptorPool* _dpPtr = &probeDescriptorPool)
            {
                DescriptorPoolCreateInfo _createInfo = new DescriptorPoolCreateInfo()
                {
                    SType = StructureType.DescriptorPoolCreateInfo,
                    PoolSizeCount = (uint)_poolSizes.Length,
                    PPoolSizes = _poolSizePtr,
                    MaxSets = (uint)_swapimageCount,
                    Flags = DescriptorPoolCreateFlags.FreeDescriptorSetBit
                };
                if (_vulkan.CreateDescriptorPool(_logicalDevice, ref _createInfo, null, _dpPtr) != Result.Success)
                {
                    throw new Exception("Failed to create descriptor pool");
                }
            }
        }

        private void UpdateDescriptorSet()
        {
            DescriptorSetLayout[] localLayout = new DescriptorSetLayout[_swapimageCount];
            Array.Fill(localLayout, _descriptorSetLayout);
            DescriptorSetLayout[] localLayout2 = new DescriptorSetLayout[_swapimageCount];
            Array.Fill(localLayout2, probeDescriptorSetLayout);

            fixed (DescriptorSetLayout* _layoutsPtr = localLayout)
            {
                uint bufferCount = (uint)_entitiesToRender.Count;
                uint[] entriesPer = { bufferCount, bufferCount, bufferCount };
                fixed (uint* perPtr = entriesPer)
                {
                    /*DescriptorSetVariableDescriptorCountAllocateInfo _variableDSCount = new()
                    {
                        SType = StructureType.DescriptorSetVariableDescriptorCountAllocateInfo,
                        DescriptorSetCount = (uint)_swapimageCount, // total amount of descriptor sets
                        PDescriptorCounts = perPtr                  // how many descriptor sets are variable
                    };*/

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
            }

            fixed (DescriptorSetLayout* _layoutsPtr = localLayout2)
            {
                uint bufferCount = (uint)_entitiesToRender.Count;
                uint[] entriesPer = { bufferCount, bufferCount, bufferCount };
                fixed (uint* perPtr = entriesPer)
                {
                    /*DescriptorSetVariableDescriptorCountAllocateInfo _variableDSCount = new()
                    {
                        SType = StructureType.DescriptorSetVariableDescriptorCountAllocateInfo,
                        DescriptorSetCount = (uint)_swapimageCount, // total amount of descriptor sets
                        PDescriptorCounts = perPtr                  // how many descriptor sets are variable
                    };*/

                    DescriptorSetAllocateInfo _allocateInfo = new DescriptorSetAllocateInfo()
                    {
                        SType = StructureType.DescriptorSetAllocateInfo,
                        DescriptorPool = probeDescriptorPool,
                        DescriptorSetCount = (uint)_swapimageCount,
                        PSetLayouts = _layoutsPtr
                    };

                    probeDescriptorSets = new DescriptorSet[_swapimageCount];
                    fixed (DescriptorSet* _descriptorSetsPtr = probeDescriptorSets)
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
                // probes
                DescriptorImageInfo probeTexel = new DescriptorImageInfo
                {
                    ImageView = probesImageView,
                    ImageLayout = ImageLayout.General // Compute shader writes to it
                };
                DescriptorImageInfo lightsImageInfo = new DescriptorImageInfo
                {
                    ImageView = lightsImageView,
                    ImageLayout = ImageLayout.General // Compute shader writes to it
                };

                var probeWrites = new WriteDescriptorSet[]
                {
                    new WriteDescriptorSet
                    {
                        SType = StructureType.WriteDescriptorSet,
                        DstSet = probeDescriptorSets[i],
                        DstBinding = 0,
                        DescriptorCount = 1,
                        DescriptorType = DescriptorType.StorageImage,
                        PImageInfo = &probeTexel
                    },
                    new WriteDescriptorSet
                    {
                        SType = StructureType.WriteDescriptorSet,
                        DstSet = probeDescriptorSets[i],
                        DstBinding = 1,
                        DescriptorCount = 1,
                        DescriptorType = DescriptorType.StorageImage,
                        PImageInfo = &lightsImageInfo
                    }
                };

                fixed (WriteDescriptorSet* _descPtr = probeWrites)
                {
                    _vulkan!.UpdateDescriptorSets(_logicalDevice, (uint)probeWrites.Length, _descPtr, 0, null);
                }
                //-----------------------------------------------------------------------------------------------
                // framebuffer
                DescriptorImageInfo imageInfo = new DescriptorImageInfo
                {
                    ImageView = storageImageView[i],
                    ImageLayout = ImageLayout.General // Compute shader writes to it
                };
                DescriptorBufferInfo mousePosInfo = new DescriptorBufferInfo()
                {
                    Buffer = mousePosBuffer,
                    Offset = 0,
                    Range = (ulong)sizeof(MouseData)
                };

                var writeDescriptorSets = new WriteDescriptorSet[]
                {
                    new WriteDescriptorSet
                    {
                        SType = StructureType.WriteDescriptorSet,
                        DstSet = _descriptorSets[i],
                        DstBinding = 0,
                        DescriptorCount = 1,
                        DescriptorType = DescriptorType.StorageImage,
                        PImageInfo = &imageInfo
                    },
                    new WriteDescriptorSet
                    {
                        SType = StructureType.WriteDescriptorSet,
                        DstSet = _descriptorSets[i],
                        DstBinding = 1,
                        DescriptorCount = 1,
                        DescriptorType = DescriptorType.StorageImage,
                        PImageInfo = &lightsImageInfo
                    },
                    new WriteDescriptorSet
                    {
                        SType = StructureType.WriteDescriptorSet,
                        DstSet = _descriptorSets[i],
                        DstBinding = 2,
                        DescriptorCount = 1,
                        DescriptorType = DescriptorType.StorageImage,
                        PImageInfo = &probeTexel
                    },
                    new WriteDescriptorSet
                    {
                        SType = StructureType.WriteDescriptorSet,
                        DstSet = _descriptorSets[i],
                        DstBinding = 3,
                        DescriptorCount = 1,
                        DescriptorType = DescriptorType.UniformBuffer,
                        PBufferInfo = &mousePosInfo
                    }
                };

                fixed (WriteDescriptorSet* _descPtr = writeDescriptorSets)
                {
                    _vulkan!.UpdateDescriptorSets(_logicalDevice, (uint)writeDescriptorSets.Length, _descPtr, 0, null);
                }
            }
        }

        internal override void CreateCommandBuffers()
        {
            ImageSubresourceRange _sr = new()
            {
                AspectMask = ImageAspectFlags.ColorBit,
                BaseArrayLayer = 0,
                BaseMipLevel = 0,
                LayerCount = 1,
                LevelCount = 1
            };

            _commandBuffer = new CommandBuffer[_swapimageCount];
            CommandBufferAllocateInfo allocInfo = new CommandBufferAllocateInfo
            {
                SType = StructureType.CommandBufferAllocateInfo,
                Level = CommandBufferLevel.Primary,
                CommandPool = _commandPool,
                CommandBufferCount = (uint)_swapimageCount
            };
            _vulkan.AllocateCommandBuffers(_logicalDevice, &allocInfo, _commandBuffer);
            CommandBufferBeginInfo beginInfo = new CommandBufferBeginInfo { SType = StructureType.CommandBufferBeginInfo };
            for (int i = 0; i < _swapimageCount; i++)
            {
                _vulkan.BeginCommandBuffer(_commandBuffer[i], &beginInfo);
                // probes
                _vulkan.CmdBindPipeline(_commandBuffer[i], PipelineBindPoint.Compute, probePipeline);
                _vulkan.CmdBindDescriptorSets(_commandBuffer[i], PipelineBindPoint.Compute, probePipelineLayout, 0, 1, ref probeDescriptorSets[i], 0, null);
                _vulkan.CmdDispatch(_commandBuffer[i], _extent.Width / 16, _extent.Height / 16, 1);

                // drawing
                _vulkan.CmdBindPipeline(_commandBuffer[i], PipelineBindPoint.Compute, computePipeline);
                _vulkan.CmdBindDescriptorSets(_commandBuffer[i], PipelineBindPoint.Compute, computePipelineLayout, 0, 1, ref _descriptorSets[i], 0, null);
                _vulkan.CmdDispatch(_commandBuffer[i], _extent.Width / 16, _extent.Height / 16, 1);

                SetImageLayout(ref _commandBuffer[i], ref _swapchain._swapchainImages[i], ImageLayout.Undefined, ImageLayout.TransferDstOptimal, _sr);
                SetImageLayout(ref _commandBuffer[i], ref storageImage[i], ImageLayout.General, ImageLayout.TransferSrcOptimal, _sr);
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
                _vulkan.CmdCopyImage(_commandBuffer[i], storageImage[i], ImageLayout.TransferSrcOptimal, _swapchain._swapchainImages[i], ImageLayout.TransferDstOptimal, 1, &_ic);
                SetImageLayout(ref _commandBuffer[i], ref _swapchain._swapchainImages[i], ImageLayout.TransferDstOptimal, ImageLayout.PresentSrcKhr, _sr);
                SetImageLayout(ref _commandBuffer[i], ref storageImage[i], ImageLayout.TransferSrcOptimal, ImageLayout.General, _sr);

                _vulkan.EndCommandBuffer(_commandBuffer[i]);
            }
        }

        internal override void Draw()
        {
            Result r;
            //_camera.ProcessKeyboard();
            r = _vulkan.WaitForFences(_logicalDevice, 1, ref _fencesInFlight[_currentFrame], true, ulong.MaxValue);
            uint _imageIndex = 0;
            r = _swapchain._driverSwapchain.AcquireNextImage(_logicalDevice, _swapchain._swapchainKHR, ulong.MaxValue, _imageAvailableSemaphores[_currentFrame], default, ref _imageIndex);

            if (r == Result.ErrorOutOfDateKhr)
            {
                //RecreateSwapchain();
                return;
            }
            else if (r != Result.Success && r != Result.SuboptimalKhr)
            {
                throw new Exception("Failed to acquire swapchain image");
            }

            //_camera.UpdateCameraMatrix(_extent, _imageIndex);
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
                PipelineStageFlags.ColorAttachmentOutputBit
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

            _vulkan.ResetFences(_logicalDevice, 1, ref _fencesInFlight[_currentFrame]);
            r = _vulkan.QueueSubmit(_graphicsQueue, 1, ref _submitInfo, _fencesInFlight[_currentFrame]);
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
            r = _swapchain._driverSwapchain.QueuePresent(_presentQueue, ref _presentInfo);
            if (r == Result.ErrorOutOfDateKhr || r == Result.SuboptimalKhr || _glWindow._frameBufferResized)
            {
                _glWindow._frameBufferResized = false;
                //RecreateSwapchain();
            }
            else if (r != Result.Success)
            {
                throw new Exception("Failed to present swap chain image");
            }
            _currentFrame = (_currentFrame + 1) % MAX_FRAMES_IN_FLIGHT;
        }

        private void TransitionImageLayout(CommandBuffer commandBuffer, Image image, ImageLayout oldLayout, ImageLayout newLayout)
        {
            ImageMemoryBarrier barrier = new ImageMemoryBarrier
            {
                SType = StructureType.ImageMemoryBarrier,
                OldLayout = oldLayout,
                NewLayout = newLayout,
                SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
                DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
                Image = image,
                SubresourceRange = new ImageSubresourceRange
                {
                    AspectMask = ImageAspectFlags.ColorBit,
                    BaseMipLevel = 0,
                    LevelCount = 1,
                    BaseArrayLayer = 0,
                    LayerCount = 1
                }
            };

            PipelineStageFlags sourceStage;
            PipelineStageFlags destinationStage;


            if (oldLayout == ImageLayout.Undefined && newLayout == ImageLayout.General)
            {
                barrier.SrcAccessMask = 0;
                barrier.DstAccessMask = AccessFlags.ShaderWriteBit;
                sourceStage = PipelineStageFlags.TopOfPipeBit;
                destinationStage = PipelineStageFlags.ComputeShaderBit;
            }
            else if (oldLayout == ImageLayout.General && newLayout == ImageLayout.TransferSrcOptimal)
            {
                barrier.SrcAccessMask = AccessFlags.ShaderWriteBit;
                barrier.DstAccessMask = AccessFlags.TransferReadBit;
                sourceStage = PipelineStageFlags.ComputeShaderBit;
                destinationStage = PipelineStageFlags.TransferBit;
            }
            else if (oldLayout == ImageLayout.PresentSrcKhr && newLayout == ImageLayout.TransferDstOptimal)
            {
                barrier.SrcAccessMask = AccessFlags.None;
                barrier.DstAccessMask = AccessFlags.TransferWriteBit;
                sourceStage = PipelineStageFlags.TopOfPipeBit;
                destinationStage = PipelineStageFlags.TransferBit;
            }
            else if (oldLayout == ImageLayout.TransferDstOptimal && newLayout == ImageLayout.PresentSrcKhr)
            {
                barrier.SrcAccessMask = AccessFlags.TransferWriteBit;
                barrier.DstAccessMask = AccessFlags.None;
                sourceStage = PipelineStageFlags.TransferBit;
                destinationStage = PipelineStageFlags.BottomOfPipeBit;
            }
            else if (oldLayout == ImageLayout.General && newLayout == ImageLayout.PresentSrcKhr)
            {
                barrier.SrcAccessMask = AccessFlags.ShaderWriteBit;
                barrier.DstAccessMask = AccessFlags.None;
                sourceStage = PipelineStageFlags.ComputeShaderBit;
                destinationStage = PipelineStageFlags.BottomOfPipeBit;
            }
            else
            {
                throw new Exception($"Unsupported layout transition: {oldLayout} -> {newLayout}");
            }
            _vulkan.CmdPipelineBarrier(commandBuffer, sourceStage, destinationStage, 0, 0, null, 0, null, 1, &barrier);
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
                destinationStage = PipelineStageFlags.ComputeShaderBit;
            }
            else if (_oldLayout == ImageLayout.General && _newLayout == ImageLayout.TransferSrcOptimal)
            {
                _barrier.SrcAccessMask = AccessFlags.ShaderWriteBit;
                _barrier.DstAccessMask = AccessFlags.TransferReadBit;

                sourceStage = PipelineStageFlags.ComputeShaderBit;
                destinationStage = PipelineStageFlags.TransferBit;
            }
            else
            {
                throw new Exception("unsupported layout transition!");
            }
            _vulkan!.CmdPipelineBarrier(_cBuffer, sourceStage, destinationStage, 0, 0, null, 0, null, 1, &_barrier);
        }

        private ShaderModule LoadShader(string path)
        {
            byte[] code = File.ReadAllBytes(path);
            fixed (byte* codePtr = code)
            {
                ShaderModuleCreateInfo createInfo = new ShaderModuleCreateInfo
                {
                    SType = StructureType.ShaderModuleCreateInfo,
                    CodeSize = (nuint)code.Length,
                    PCode = (uint*)codePtr
                };

                ShaderModule shaderModule;
                if (_vulkan.CreateShaderModule(_logicalDevice, &createInfo, null, &shaderModule) != Result.Success)
                    throw new Exception("Failed to create shader module");

                return shaderModule;
            }
        }
    }
}