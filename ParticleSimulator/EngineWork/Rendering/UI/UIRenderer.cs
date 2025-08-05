using System.Runtime.CompilerServices;
using ArctisAurora.EngineWork.ECS.RenderingComponents.Vulkan;
using ArctisAurora.EngineWork.EngineEntity;
using ArctisAurora.EngineWork.Rendering.Helpers;
using ArctisAurora.EngineWork.Rendering.MeshSubComponents;
using Silk.NET.Maths;
using Silk.NET.Vulkan;

namespace ArctisAurora.EngineWork.Rendering.UI
{
    internal unsafe class UIRenderer : VulkanRenderer
    {
        string[] requiredExtensions =
        {
            "VK_KHR_swapchain",
            "VK_EXT_descriptor_indexing",
        };
        private Framebuffer[] _frameBuffer;
        //-------------------------------------
        internal static Vector2D<float> unitsPerPixel;
        internal static MCUI meshComponent;

        public UIRenderer()
        {
            Setup();
            // calc world units per pixel   
            unitsPerPixel = AuroraCamera.GetPixelSizeInWorldSpace(-1, 1, -1, 1, (int)_extent.Width, (int)_extent.Height);

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
            _camera = new AuroraCamera();

            CreateUIDescriptorSetLayouts();
            CreateGraphicsPipeline();
            CreateFrameBuffers();
            CreateCommandPool();

            CreateDescriptorPool();
            CreateCommandBuffers();
            CreateSyncObjects();
        }

        private void Setup()
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

        internal override void AddEntityToRenderQueue(Entity _m)
        {
            base.AddEntityToRenderQueue(_m);
            CreateDescriptorPool();
            for (int i = 0; i < _entitiesToRender.Count; i++)
            {
                _entitiesToRender[i].GetComponent<MeshComponent>().ReinstantiateDesriptorSets();
            }
            CreateGlobalDescriptorSets();
            RecreateCommandBuffers();
        }

        private void CreateUIDescriptorSetLayouts()
        {
            List<DescriptorType> _types1 = new List<DescriptorType> { DescriptorType.UniformBuffer, DescriptorType.StorageBuffer, DescriptorType.StorageBuffer, DescriptorType.CombinedImageSampler };
            List<ShaderStageFlags> _flags1 = new List<ShaderStageFlags> { ShaderStageFlags.VertexBit, ShaderStageFlags.VertexBit, ShaderStageFlags.VertexBit, ShaderStageFlags.FragmentBit };
            DescriptorBindingFlags[] _DBF = { DescriptorBindingFlags.None, DescriptorBindingFlags.VariableDescriptorCountBit, DescriptorBindingFlags.VariableDescriptorCountBit, DescriptorBindingFlags.VariableDescriptorCountBit };

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
        }

        internal override void CreateDescriptorPool()
        {
            var _poolSizes = new DescriptorPoolSize[]
            {
                new DescriptorPoolSize()
                {
                    Type = DescriptorType.CombinedImageSampler,
                    DescriptorCount = (uint)(_swapimageCount * _entitiesToRender.Count * 10) + 1
                },
                new DescriptorPoolSize()
                {
                    Type = DescriptorType.UniformBuffer,
                    DescriptorCount = (uint)(_swapimageCount * _entitiesToRender.Count) + 1
                },
                new DescriptorPoolSize()
                {
                    Type = DescriptorType.StorageBuffer,
                    DescriptorCount = (uint)(_swapimageCount * _entitiesToRender.Count * 2) + 1
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
                    Flags = DescriptorPoolCreateFlags.FreeDescriptorSetBit | DescriptorPoolCreateFlags.UpdateAfterBindBit
                };
                if (_vulkan.CreateDescriptorPool(_logicalDevice, ref _poolInfo, null, _descPoolPtr) != Result.Success)
                {
                    throw new Exception("Failed to create descriptor pool");
                }
            }
        }

        private void CreateGraphicsPipeline()
        {
            _pipeline = new GraphicsPipeline();
            _pipeline.CreateGraphicsPipeline("UIRasterizer/UI.vert.spv", "UIRasterizer/UI.frag.spv", _extent, ref _descriptorSetLayout);
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

        internal override void CreateGlobalDescriptorSets()
        {
            //base.CreateGlobalDescriptorSets();
            AllocateDescriptorSets(ref _descriptorSetLayout, ref _descriptorPool, ref _descriptorSets);
            for (int i = 0; i < _swapimageCount; i++)
            {
                DescriptorBufferInfo _cameraInfo = new DescriptorBufferInfo()
                {
                    Buffer = _camera._cameraBuffer[i],
                    Offset = 0,
                    Range = (ulong)Unsafe.SizeOf<UBO>()
                };

                DescriptorBufferInfo[] _transformUniformInfos = new DescriptorBufferInfo[_entitiesToRender.Count];
                DescriptorBufferInfo[] _uvBufferInfos = new DescriptorBufferInfo[_entitiesToRender.Count];
                DescriptorImageInfo[] _textureImageInfos = new DescriptorImageInfo[_entitiesToRender.Count];
                for (int k = 0; k < _entitiesToRender.Count; k++)
                {
                    MCUI component = _entitiesToRender[k].GetComponent<MCUI>();
                    _textureImageInfos[k] = new()
                    {
                        ImageLayout = Silk.NET.Vulkan.ImageLayout.ShaderReadOnlyOptimal,
                        ImageView = component.fontAsset.image._textureImageView,
                        Sampler = component.textureSampler
                    };
                    _transformUniformInfos[k] = new()
                    {
                        Buffer = component._transformsBuffer,
                        Offset = 0,
                        Range = sizeof(float) * 16
                    };
                    _uvBufferInfos[k] = new()
                    {
                        Buffer = component.uvBuffer,
                        Offset = 0,
                        Range = (ulong)Unsafe.SizeOf<Vector2D<float>>() * 4
                    };
                }
                fixed (DescriptorBufferInfo* _uvBufferInfosPtr = _uvBufferInfos)
                fixed (DescriptorBufferInfo* _transformInforPtr = _transformUniformInfos)
                fixed (DescriptorImageInfo* _textureImageInforPtr = _textureImageInfos)
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
                            PBufferInfo = &_cameraInfo
                        },
                        new WriteDescriptorSet
                        {
                            SType = StructureType.WriteDescriptorSet,
                            DstSet = _descriptorSets[i],
                            DstBinding = 1,
                            DescriptorCount = (uint)_entitiesToRender.Count,
                            DstArrayElement = 0,
                            DescriptorType = DescriptorType.StorageBuffer,
                            PBufferInfo = _transformInforPtr
                        },
                        new WriteDescriptorSet
                        {
                            SType = StructureType.WriteDescriptorSet,
                            DstSet = _descriptorSets[i],
                            DstBinding = 2,
                            DescriptorCount = (uint)_entitiesToRender.Count,
                            DstArrayElement = 0,
                            DescriptorType = DescriptorType.StorageBuffer,
                            PBufferInfo = _uvBufferInfosPtr
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
                //player view
                _vulkan.CmdBindPipeline(_commandBuffer[i], PipelineBindPoint.Graphics, _pipeline._graphicsPipeline);
                _vulkan.CmdBeginRenderPass(_commandBuffer[i], &_renderPassInfo, SubpassContents.Inline);
                for (int e = 0; e < _entitiesToRender.Count; e++)
                {
                    var _offset = new ulong[] { 0 };
                    _entitiesToRender[e].GetComponent<MeshComponent>().EnqueueDrawCommands(ref _offset, i, e, ref _commandBuffer[i]);
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

        internal override void Draw()
        {
            //_camera.ProcessKeyboard();
            _vulkan.WaitForFences(_logicalDevice, 1, ref _fencesInFlight[_currentFrame], true, ulong.MaxValue);
            uint _imageIndex = 0;
            Result r = _swapchain._driverSwapchain.AcquireNextImage(_logicalDevice, _swapchain._swapchainKHR, ulong.MaxValue, _imageAvailableSemaphores[_currentFrame], default, ref _imageIndex);

            if (r == Result.ErrorOutOfDateKhr)
            {
                //RecreateSwapChain();
                return;
            }
            else if (r != Result.Success && r != Result.SuboptimalKhr)
            {
                throw new Exception("Failed to acquire swapchain image");
            }

            //_camera.UpdateCameraMatrix(_extent, _imageIndex);
            int localEntityCount = 0;
            foreach (Entity e in _updateEntities)
            {
                e.transform._changed = false;
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
                //RecreateSwapChain();
            }
            else if (r != Result.Success)
            {
                throw new Exception("Failed to present swap chain image");
            }

            _currentFrame = (_currentFrame + 1) % MAX_FRAMES_IN_FLIGHT;
        }
    }
}
