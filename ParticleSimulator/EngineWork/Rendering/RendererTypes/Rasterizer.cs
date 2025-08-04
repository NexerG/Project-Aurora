using Silk.NET.Vulkan;
using ArctisAurora.EngineWork.ECS.RenderingComponents.Vulkan;
using Silk.NET.Maths;
using ArctisAurora.EngineWork.Rendering.Helpers;
using ArctisAurora.EngineWork.Rendering.MeshSubComponents;
using Buffer = Silk.NET.Vulkan.Buffer;
using System.Runtime.CompilerServices;
using static ArctisAurora.EngineWork.ECS.RenderingComponents.Vulkan.LightsourceComponent;
using ArctisAurora.EngineWork.EngineEntity;

namespace ArctisAurora.EngineWork.Rendering.RendererTypes
{
    internal unsafe class Rasterizer : VulkanRenderer
    {
        string[] requiredExtensions = 
        {
            "VK_KHR_swapchain",
            "VK_EXT_descriptor_indexing",
        };
        //buffers
        private Framebuffer[] _frameBuffer;
        //descriptors
        internal static DescriptorSetLayout _descriptorSetLayoutShadow;
        internal static DescriptorPool _descriptorPoolShadow;
        internal DescriptorSet[] _descriptorSetsShadow;
        //-------------------------------------
        internal static Sampler _textureSampler;
        internal static Sampler _shadowmapSampler;
        //

        public Rasterizer()
        {
            Setup();
            //getting the render queues ready
            int _graphicsQFamilyIndex = AVulkanHelper.FindQueueFamilyIndex(ref _vulkan, ref _gpu, ref _qfm, QueueFlags.GraphicsBit);
            uint _presentSupportIndex = AVulkanHelper.FindPresentSupportIndex(ref _gpu, ref _qfm, ref _glWindow.driverSurface, ref _glWindow.surface);
            _graphicsQueue = _vulkan.GetDeviceQueue(_logicalDevice, (uint)_graphicsQFamilyIndex, 0);
            _presentQueue = _vulkan.GetDeviceQueue(_logicalDevice, _presentSupportIndex, 0);

            //create the swapchain
            _swapchain = new Swapchain(ref _glWindow.driverSurface, ref _glWindow.surface);
            _swapchain.DoSwapchainMethodSequence(ref _extent);        //swapchain methods for simplicity sake
            _swapimageCount = _swapchain._swapchainImages.Length;     //engine related thing

            _descriptorSets = new DescriptorSet[_swapimageCount];
            _descriptorSetsShadow = new DescriptorSet[_swapimageCount];
            _camera = new AuroraCamera();

            //here go any extensions required for the renderer.

            //initiate the draw command pipeline
            CreateRasterizerDescritorSetLayouts();
            CreateGraphicsPipeline();                       //graphics pipeline
            CreateFrameBuffers();                           //frame buffers
            CreateCommandPool();                            //
            CreateImageSampler();                           //

            CreateDescriptorPool();                         //descriptor pool
            CreateShadowDescriptorPool();                   //descriptor pool for shadows
            //from this point the entities can be created

            CreateCommandBuffers();                         //the draw command sequence that'll be used for rendering
            CreateSyncObjects();                            //CPU - GPU sync logic
        }

        private void Setup()
        {
            //some engine specific rendering prerequisites
            _rendererInstance = this;
            //end of prerequisites
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

        internal override void AddEntityToRenderQueue(Entity _m)
        {
            base.AddEntityToRenderQueue(_m);
            CreateDescriptorPool();
            CreateShadowDescriptorPool();
            /*for (int i = 0; i < _entitiesToRender.Count; i++)
                _entitiesToRender[i].GetComponent<MeshComponent>().ReinstantiateDesriptorSets();*/

            CreateGlobalDescriptorSets();
            RecreateCommandBuffers();
        }

        internal override void AddLightToRenderQueue(Entity _l)
        {
            _lightsToRender.Add(_l);
            //CreateGlobalDescriptorSets();
        }

        internal override void MouseUpdate(double xPos, double yPos)
        {
            base.MouseUpdate(xPos, yPos);
        }

        private void RecreateSwapChain()
        {
            //cleanup
            _glWindow.UpdateWindowSize(ref _extent);
            _vulkan.DeviceWaitIdle(_logicalDevice);
            CleanUpSwapChain();
            //visuals
            _swapchain.DoSwapchainMethodSequence(ref _extent);
            CreateGraphicsPipeline();
            CreateFrameBuffers();
            //api calls
            CreateDescriptorPool();
            CreateShadowDescriptorPool();
            for (int i = 0; i < _entitiesToRender.Count; i++)
            {
                _entitiesToRender[i].GetComponent<MeshComponent>().CreateDescriptorSet();
            }
            RecreateCommandBuffers();

            _imagesInFlight = new Fence[_swapchain._swapchainImages.Length];
        }

        private void CreateGraphicsPipeline()
        {
            _pipeline = new GraphicsPipeline();
            _pipeline.CreateGraphicsPipeline("vulkan.vert.spv", "vulkan.frag.spv", _extent, ref _descriptorSetLayout);
            _pipeline.CreateShadwomapPipeline("Shadowmap.vert.spv", new Extent2D(2000, 2000), ref _descriptorSetLayoutShadow);
        }

        private void CreateFrameBuffers()
        {
            _frameBuffer = new Framebuffer[_swapchain!._imageViews.Length];
            for (int i = 0; i < _swapchain._imageViews.Length; i++)
            {
                var _attachment = new[] { _swapchain._imageViews[i], _swapchain._depthView };

                fixed (ImageView* _imAttachmentPtr = _attachment)
                {
                    FramebufferCreateInfo _framebufferInfo = new FramebufferCreateInfo()
                    {
                        SType = StructureType.FramebufferCreateInfo,
                        RenderPass = _swapchain._renderPass,
                        AttachmentCount = (uint)_attachment.Length,
                        PAttachments = _imAttachmentPtr,
                        Width = _extent.Width,
                        Height = _extent.Height,
                        Layers = 1
                    };
                    if (_vulkan.CreateFramebuffer(_logicalDevice, ref _framebufferInfo, null, out _frameBuffer[i]) != Result.Success)
                    {
                        throw new Exception("Failed to create frame buffer");
                    }
                }
            }
        }

        internal override void RecreateCommandBuffers()
        {
            base.RecreateCommandBuffers();
            CreateCommandBuffers();
        }

        internal override void CreateCommandBuffers()
        {
            base.CreateCommandBuffers();
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
                //normal render pass info
                RenderPassBeginInfo _renderPassInfo = new RenderPassBeginInfo()
                {
                    SType = StructureType.RenderPassBeginInfo,
                    RenderPass = _swapchain._renderPass,
                    Framebuffer = _frameBuffer[i],
                    RenderArea =
                    {
                        Offset = { X = 0, Y = 0 },
                        Extent = _extent
                    }
                };

                var _clearValues = new ClearValue[]
                {
                    new ClearValue()
                    {
                        Color = new ClearColorValue() { Float32_0 = 0.05f, Float32_1 = 0.05f, Float32_2 = 0.05f, Float32_3 = 1f },
                    },
                    new ClearValue()
                    {
                        DepthStencil = new ClearDepthStencilValue() { Depth = 1f, Stencil = 0 }
                    },
                };

                fixed (ClearValue* _clrValuesPtr = _clearValues)
                {
                    _renderPassInfo.ClearValueCount = (uint)_clearValues.Length;
                    _renderPassInfo.PClearValues = _clrValuesPtr;
                }
                //shadow render pass info
                var _shadowClearValues = new ClearValue[]
                {
                    new ClearValue()
                    {
                        DepthStencil = new ClearDepthStencilValue() { Depth = 1f, Stencil = 0 }
                    }
                };

                //render code
                Buffer[] _vertBuffers = new Buffer[_entitiesToRender.Count + _entitiesToRender.Count * _lightsToRender.Count];
                _vulkan.CmdBindPipeline(_commandBuffer[i], PipelineBindPoint.Graphics, _pipeline._shadowPipeline);
                for (int j = 0; j < _lightsToRender.Count; j++)
                {
                    RenderPassBeginInfo _shadowPassInfo = new RenderPassBeginInfo()
                    {
                        SType = StructureType.RenderPassBeginInfo,
                        RenderPass = _swapchain._shadowmapRenderPass,
                        Framebuffer = _lightsToRender[j].GetComponent<LightsourceComponent>()._shadowFramebuffer,
                        RenderArea =
                        {
                            Offset = { X = 0, Y = 0 },
                            Extent = new Extent2D(2000, 2000)
                        },
                    };
                    fixed (ClearValue* _clrValuesPtr = _shadowClearValues)
                    {
                        _shadowPassInfo.ClearValueCount = (uint)_shadowClearValues.Length;
                        _shadowPassInfo.PClearValues = _clrValuesPtr;

                    }
                    _vulkan.CmdBeginRenderPass(_commandBuffer[i], &_shadowPassInfo, SubpassContents.Inline);
                    //shadow maps
                    for (int e = 0; e < _entitiesToRender.Count; e++)
                    {
                        var _offset = new ulong[] { 0 };
                        ((MCRaster)_entitiesToRender[e].GetComponent<MeshComponent>()).EnqueuShadowDrawCommands(_offset, i, ref _commandBuffer[i], j);
                    }
                    _vulkan.CmdEndRenderPass(_commandBuffer[i]);
                }
                //player view
                _vulkan.CmdBindPipeline(_commandBuffer[i], PipelineBindPoint.Graphics, _pipeline._graphicsPipeline);
                _vulkan.CmdBeginRenderPass(_commandBuffer[i], &_renderPassInfo, SubpassContents.Inline);
                for (int e = 0; e < _entitiesToRender.Count; e++)
                {
                    var _offset = new ulong[] { 0 };
                    _entitiesToRender[e].GetComponent<MeshComponent>().EnqueueDrawCommands(ref _offset, i, ref _commandBuffer[i]);
                }

                //end of for loop
                _vulkan.CmdEndRenderPass(_commandBuffer[i]);
                //done rendering

                if (_vulkan.EndCommandBuffer(_commandBuffer[i]) != Result.Success)
                {
                    throw new Exception("Failed to record command buffer");
                }
            }
        }

        private void CreateRasterizerDescritorSetLayouts()
        {
            List<DescriptorType> _types1 = new List<DescriptorType> { DescriptorType.UniformBuffer, DescriptorType.CombinedImageSampler, DescriptorType.StorageBuffer, DescriptorType.CombinedImageSampler };
            List<ShaderStageFlags> _flags1 = new List<ShaderStageFlags> { ShaderStageFlags.VertexBit, ShaderStageFlags.FragmentBit, ShaderStageFlags.FragmentBit, ShaderStageFlags.FragmentBit };
            DescriptorBindingFlags[] _DBF = { DescriptorBindingFlags.None, DescriptorBindingFlags.None, DescriptorBindingFlags.VariableDescriptorCountBit , DescriptorBindingFlags.VariableDescriptorCountBit };

            uint _indexedCount = 50000;
            uint[] _descriptorCount = new uint[_DBF.Length];
            for (int i = 0; i < _DBF.Length; i++)
            {
                if (_DBF[i] == DescriptorBindingFlags.VariableDescriptorCountBit)
                {
                    _descriptorCount[i] = _indexedCount;
                }
                else
                {
                    _descriptorCount[i] = 1;
                }
            }

            CreateDescriptorSetLayout(_types1.Count, _types1, _flags1, ref _descriptorSetLayout, _DBF, _descriptorCount);

            List<DescriptorType> _types2 = new List<DescriptorType> { DescriptorType.UniformBuffer, DescriptorType.StorageBuffer };
            List<ShaderStageFlags> _flags2 = new List<ShaderStageFlags> { ShaderStageFlags.VertexBit, ShaderStageFlags.VertexBit };
            DescriptorBindingFlags[] _DBF2 = { DescriptorBindingFlags.None, DescriptorBindingFlags.VariableDescriptorCountBit};
            _descriptorCount = new uint[_DBF2.Length];
            for (int i = 0; i < _DBF2.Length; i++)
            {
                if (_DBF2[i] == DescriptorBindingFlags.VariableDescriptorCountBit)
                {
                    _descriptorCount[i] = _indexedCount;
                }
                else
                {
                    _descriptorCount[i] = 1;
                }
            }

            CreateDescriptorSetLayout(_types2.Count, _types2, _flags2, ref _descriptorSetLayoutShadow, _DBF2, _descriptorCount);
        }

        internal override void CreateGlobalDescriptorSets()
        {
            //base.CreateGlobalDescriptorSets();
            AllocateDescriptorSets(ref _descriptorSetLayout, ref _descriptorPool, ref _descriptorSets);
            CreateRasterDescriptorSets();

            AllocateDescriptorSets(ref _descriptorSetLayoutShadow, ref _descriptorPoolShadow, ref _descriptorSetsShadow);
            CreateShadowMapDescriptorSets();
        }

        private void CreateRasterDescriptorSets()
        {
            for (int i = 0; i < _swapimageCount; i++)
            {
                fixed (AccelerationStructureKHR* _accelStrPtr = &Pathtracing._TLAS._handle)
                {
                    DescriptorBufferInfo _bufferInfoMatrices = new DescriptorBufferInfo()
                    {
                        Buffer = _camera._cameraBuffer[i],
                        Offset = 0,
                        Range = (ulong)Unsafe.SizeOf<UBO>()
                    };
                    DescriptorImageInfo shadowMapInfo = new()
                    {
                        ImageLayout = Silk.NET.Vulkan.ImageLayout.ShaderReadOnlyOptimal,
                        ImageView = _lightsToRender[0].GetComponent<LightsourceComponent>()._depthImageView,
                        Sampler = _shadowmapSampler
                    };

                    DescriptorBufferInfo[] _transformUniformInfos = new DescriptorBufferInfo[_entitiesToRender.Count];
                    DescriptorImageInfo[] _textureImageInfos = new DescriptorImageInfo[_entitiesToRender.Count];
                    for (int k = 0; k < _entitiesToRender.Count; k++)
                    {
                        MCRaster component = _entitiesToRender[k].GetComponent<MCRaster>();
                        _transformUniformInfos[k] = new()
                        {
                            Buffer = component._transformsBuffer,
                            Offset = 0,
                            Range = Vk.WholeSize
                        };
                        _textureImageInfos[k] = new()
                        {
                            ImageLayout = Silk.NET.Vulkan.ImageLayout.ShaderReadOnlyOptimal,
                            ImageView = component._textureImageView,
                            Sampler = _textureSampler
                        };
                    }
                    fixed (DescriptorImageInfo* _textureImageInforPtr = _textureImageInfos)
                    fixed (DescriptorBufferInfo* _transforUniformInfoPtr = _transformUniformInfos)
                    {
                        var _writeDescriptorSets = new WriteDescriptorSet[]
                        {
                            new WriteDescriptorSet
                            {
                                SType = StructureType.WriteDescriptorSet,
                                DstSet = _descriptorSets[i],
                                DstBinding = 0,
                                DescriptorCount = 1,
                                DstArrayElement = 0,
                                DescriptorType = DescriptorType.UniformBuffer,
                                PBufferInfo = &_bufferInfoMatrices
                            },
                            new WriteDescriptorSet
                            {
                                SType = StructureType.WriteDescriptorSet,
                                DstSet = _descriptorSets[i],
                                DstBinding = 1,
                                DescriptorCount = 1,
                                DstArrayElement = 0,
                                DescriptorType = DescriptorType.CombinedImageSampler,
                                PImageInfo = &shadowMapInfo
                            },
                            // transform
                            new WriteDescriptorSet
                            {
                                SType = StructureType.WriteDescriptorSet,
                                DstSet = _descriptorSets[i],
                                DstBinding = 2,
                                DescriptorCount = (uint)_transformUniformInfos.Length,
                                DstArrayElement = 0,
                                DescriptorType = DescriptorType.StorageBuffer,
                                PBufferInfo = _transforUniformInfoPtr
                            },
                            new WriteDescriptorSet
                            {
                                SType = StructureType.WriteDescriptorSet,
                                DstSet = _descriptorSets[i],
                                DstBinding = 3,
                                DescriptorCount = (uint)_textureImageInfos.Length,
                                DstArrayElement = 0,
                                DescriptorType = DescriptorType.CombinedImageSampler,
                                PImageInfo = _textureImageInforPtr
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

        private void CreateShadowMapDescriptorSets()
        {
            for (int i = 0; i < Rasterizer._swapchain._swapchainImages.Length; i++)
            {
                DescriptorBufferInfo[] _lightDataInfo = new DescriptorBufferInfo[_lightsToRender.Count];
                for (int k = 0; k < _lightsToRender.Count; k++)
                {
                    _lightDataInfo[k] = new DescriptorBufferInfo()
                    {
                        Buffer = _lightsToRender[k].GetComponent<LightsourceComponent>()._lightDataBuffer,
                        Offset = 0,
                        Range = (ulong)sizeof(LightData)
                    };
                }

                DescriptorBufferInfo[] _transformUniformInfos = new DescriptorBufferInfo[_entitiesToRender.Count];
                for (int k = 0; k < _entitiesToRender.Count; k++)
                {
                    MCRaster component = _entitiesToRender[k].GetComponent<MCRaster>();
                    _transformUniformInfos[k] = new()
                    {
                        Buffer = component._transformsBuffer,
                        Offset = 0,
                        Range = Vk.WholeSize
                    };
                }

                fixed (DescriptorBufferInfo* _lightDataPtr = _lightDataInfo)
                fixed (DescriptorBufferInfo* _transforUniformInfoPtr = _transformUniformInfos)
                {
                    var _writeDescriptorSets = new WriteDescriptorSet[]
                    {
                        new WriteDescriptorSet()
                        {
                            SType = StructureType.WriteDescriptorSet,
                            DstSet = _descriptorSetsShadow[i],
                            DstBinding = 0,
                            DstArrayElement = 0,
                            DescriptorType = DescriptorType.UniformBuffer,
                            DescriptorCount = (uint)_lightDataInfo.Length,
                            PBufferInfo = _lightDataPtr
                        },
                        new WriteDescriptorSet()
                        {
                            SType = StructureType.WriteDescriptorSet,
                            DstSet = _descriptorSetsShadow[i],
                            DstBinding = 1,
                            DstArrayElement = 0,
                            DescriptorType = DescriptorType.StorageBuffer,
                            DescriptorCount = (uint)_transformUniformInfos.Length,
                            PBufferInfo = _transforUniformInfoPtr
                        }
                    };
                    fixed (WriteDescriptorSet* _descPtr = _writeDescriptorSets)
                    {
                        VulkanRenderer._vulkan!.UpdateDescriptorSets(VulkanRenderer._logicalDevice, (uint)_writeDescriptorSets.Length, _descPtr, 0, null);
                    }
                }
            }
        }

        private void AllocateDescriptorSets(ref DescriptorSetLayout _layout, ref DescriptorPool _pool, ref DescriptorSet[] _sets)
        {
            DescriptorSetLayout[] localLayout = new DescriptorSetLayout[_swapimageCount];
            Array.Fill(localLayout, _layout);

            fixed (DescriptorSetLayout* _layoutsPtr = localLayout)
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
                        DescriptorPool = _pool,
                        DescriptorSetCount = (uint)_swapimageCount,
                        PSetLayouts = _layoutsPtr,
                        PNext = &_variableDSCount
                    };

                    fixed (DescriptorSet* _descriptorSetsPtr = _sets)
                    {
                        Result r = _vulkan.AllocateDescriptorSets(_logicalDevice, ref _allocateInfo, _descriptorSetsPtr);
                        if (r != Result.Success)
                        {
                            throw new Exception("Failed to allocate descriptor set with error code: " + r);
                        }
                    }
                }
            }
        }

        internal override void CreateDescriptorPool()
        {
            var _poolSizes = new DescriptorPoolSize[]
            {
                new DescriptorPoolSize()
                {
                    Type = DescriptorType.UniformBuffer,
                    DescriptorCount = (uint)_swapimageCount * 10
                },
                new DescriptorPoolSize()
                {
                    Type = DescriptorType.StorageBuffer,
                    DescriptorCount = (uint)(_swapimageCount * _entitiesToRender.Count) + 1
                },
                new DescriptorPoolSize()
                {
                    Type = DescriptorType.CombinedImageSampler,
                    DescriptorCount = (uint)(_swapimageCount * _entitiesToRender.Count * 10) + 1
                }
            };

            fixed (DescriptorPoolSize* _poolSizesPtr = _poolSizes)
            fixed (DescriptorPool* _descPoolPtr = &_descriptorPool)
            {
                DescriptorPoolCreateInfo _poolInfo = new DescriptorPoolCreateInfo()
                {
                    SType = StructureType.DescriptorPoolCreateInfo,
                    PoolSizeCount = (uint)_poolSizes.Length,
                    PPoolSizes = _poolSizesPtr,
                    MaxSets = (uint)(_swapimageCount * _entitiesToRender.Count * 5 + 1),
                    Flags = DescriptorPoolCreateFlags.FreeDescriptorSetBit
                };
                if (_vulkan.CreateDescriptorPool(_logicalDevice, ref _poolInfo, null, _descPoolPtr) != Result.Success)
                {
                    throw new Exception("Failed to create descriptor pool");
                }
            }
        }

        private void CreateShadowDescriptorPool()
        {
            var _poolSizes = new DescriptorPoolSize[]
            {
                new DescriptorPoolSize()
                {
                    Type = DescriptorType.StorageBuffer,
                    DescriptorCount = (uint)(_swapchain!._swapchainImages.Length * _entitiesToRender.Count) + 1
                },
                new DescriptorPoolSize()
                {
                    Type = DescriptorType.UniformBuffer,
                    DescriptorCount = (uint)(_swapchain!._swapchainImages.Length * _entitiesToRender.Count) + 1
                }
            };

            fixed (DescriptorPoolSize* _poolSizesPtr = _poolSizes)
            fixed (DescriptorPool* _descPoolPtr = &_descriptorPoolShadow)
            {
                DescriptorPoolCreateInfo _poolInfo = new DescriptorPoolCreateInfo()
                {
                    SType = StructureType.DescriptorPoolCreateInfo,
                    PoolSizeCount = (uint)_poolSizes.Length,
                    PPoolSizes = _poolSizesPtr,
                    MaxSets = (uint)(_swapchain!._swapchainImages.Length * _entitiesToRender.Count * 2 + 1),
                    Flags = DescriptorPoolCreateFlags.FreeDescriptorSetBit
                };
                if (_vulkan.CreateDescriptorPool(_logicalDevice, ref _poolInfo, null, _descPoolPtr) != Result.Success)
                {
                    throw new Exception("Failed to create descriptor pool");
                }
            }
        }

        private void CreateImageSampler()
        {
            //textureSampler
            _vulkan.GetPhysicalDeviceProperties(_gpu, out PhysicalDeviceProperties _properties);
            SamplerCreateInfo _createInfo = new SamplerCreateInfo()
            {
                SType = StructureType.SamplerCreateInfo,
                MagFilter = Filter.Linear,
                MinFilter = Filter.Linear,
                AddressModeU = SamplerAddressMode.Repeat,
                AddressModeV = SamplerAddressMode.Repeat,
                AddressModeW = SamplerAddressMode.Repeat,
                AnisotropyEnable = true,
                MaxAnisotropy = _properties.Limits.MaxSamplerAnisotropy,
                BorderColor = BorderColor.IntOpaqueBlack,
                UnnormalizedCoordinates = false,
                CompareEnable = false,
                CompareOp = CompareOp.Always,
                MipmapMode = SamplerMipmapMode.Linear
            };

            fixed (Sampler* _textureSamplerPtr = &_textureSampler)
            {
                Result r = _vulkan.CreateSampler(_logicalDevice, ref _createInfo, null, _textureSamplerPtr);
                if (r != Result.Success)
                {
                    throw new Exception("Failed to create a texture sampler with error: " + r);
                }
            }
            //shadow map samplper
            SamplerCreateInfo _shadowInfo = new SamplerCreateInfo()
            {
                SType = StructureType.SamplerCreateInfo,
                MagFilter = Filter.Linear,
                MinFilter = Filter.Linear,
                AddressModeU = SamplerAddressMode.ClampToEdge,
                AddressModeV = SamplerAddressMode.ClampToEdge,
                AddressModeW = SamplerAddressMode.ClampToEdge,
                AnisotropyEnable = false,
                MaxAnisotropy = 1.0f,
                MipLodBias = 0.0f,
                MinLod = 0.0f,
                MaxLod = 1.0f,
                BorderColor = BorderColor.FloatOpaqueWhite,
                UnnormalizedCoordinates = false,
                CompareEnable = false,
                CompareOp = CompareOp.Less,
                MipmapMode = SamplerMipmapMode.Linear
            };

            fixed (Sampler* _textureSamplerPtr = &_shadowmapSampler)
            {
                Result r = _vulkan.CreateSampler(_logicalDevice, ref _shadowInfo, null, _textureSamplerPtr);
                if (r != Result.Success)
                {
                    throw new Exception("Failed to create a shadowmap sampler with error: " + r);
                }
            }
        }

        internal override void Draw()
        {
            _camera.ProcessKeyboard();
            _vulkan.WaitForFences(_logicalDevice, 1, ref _fencesInFlight[_currentFrame], true, ulong.MaxValue);
            uint _imageIndex = 0;
            Result r = _swapchain._driverSwapchain.AcquireNextImage(_logicalDevice, _swapchain._swapchainKHR, ulong.MaxValue, _imageAvailableSemaphores[_currentFrame], default, ref _imageIndex);

            if (r == Result.ErrorOutOfDateKhr)
            {
                RecreateSwapChain();
                return;
            }
            else if (r != Result.Success && r != Result.SuboptimalKhr)
            {
                throw new Exception("Failed to acquire swapchain image");
            }

            _camera.UpdateCameraMatrix(_extent, _imageIndex);
            int localEntityCount = 0;
            foreach (Entity e in _updateEntities)
            {
                e.GetComponent<MeshComponent>().UpdateMatrices();
                localEntityCount++;
            }
            _updateEntities.RemoveRange(0, localEntityCount);
            //uniforms done
            if (_imagesInFlight[_imageIndex].Handle != default)
            {
                _vulkan.WaitForFences(_logicalDevice, 1, ref _imagesInFlight[_imageIndex], true, ulong.MaxValue);
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
                throw new Exception("Failed to send command buffer to the GPU with error code:" + r);
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
            if (r == Result.ErrorOutOfDateKhr || r == Result.SuboptimalKhr || _glWindow.frameBufferResized)
            {
                _glWindow.frameBufferResized = false;
                RecreateSwapChain();
            }
            else if (r != Result.Success)
            {
                throw new Exception("Failed to present swap chain image");
            }

            _currentFrame = (_currentFrame + 1) % MAX_FRAMES_IN_FLIGHT;
        }

        private void CleanUpSwapChain()
        {
            foreach (var fb in _frameBuffer)
            {
                _vulkan.DestroyFramebuffer(_logicalDevice, fb, null);
            }
            fixed (CommandBuffer* CBPtr = _commandBuffer)
            {
                _vulkan.FreeCommandBuffers(_logicalDevice, _commandPool, (uint)_commandBuffer.Length, CBPtr);
            }

            _pipeline.DestroyPipeline();
            _vulkan.DestroyRenderPass(_logicalDevice, _swapchain._renderPass, null);
            _vulkan.DestroyRenderPass(_logicalDevice, _swapchain._shadowmapRenderPass, null);
            _swapchain.DestroySwapchain();
        }
    }
}