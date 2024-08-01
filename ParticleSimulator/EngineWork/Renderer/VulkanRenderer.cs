using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using System.Runtime.InteropServices;
using ArctisAurora.EngineWork.ECS.RenderingComponents.Vulkan;
using ArctisAurora.GameObject;
using Silk.NET.Maths;
using Silk.NET.Vulkan.Extensions.EXT;
using Buffer = Silk.NET.Vulkan.Buffer;
using Semaphore = Silk.NET.Vulkan.Semaphore;
using ArctisAurora.EngineWork.Renderer.Helpers;

namespace ArctisAurora.EngineWork.Renderer
{
    internal interface IRecreateCommandBuffer
    {
        internal void RecreateCommandBuffers();
    }

    struct LightData
    {
        internal Vector3D<float> _pos;
        internal Vector4D<float> _color;
    }

    internal unsafe class VulkanRenderer : RendererBaseClass, IRecreateCommandBuffer
    {
        string[] requiredExtensions = { "VK_KHR_swapchain" };
        //whole rendering pipeline variables
        internal static AVulkanSwapchain _swapchain;
        internal static AVulkanGraphicsPipeline _pipeline;
        //buffers
        private Framebuffer[] _framebuffer;
        private CommandBuffer[] _commandBuffer;
        internal static Buffer _lightBuffer;
        internal static DeviceMemory _lightBufferMemory;
        internal static Buffer[] _lightUBO;
        internal static DeviceMemory[] _lightUBOMemory;
        //descriptors
        internal static DescriptorSetLayout _descriptorSetLayout;
        internal static DescriptorPool _descriptorPool;
        internal static DescriptorSetLayout _descriptorSetLayoutShadow;
        internal static DescriptorPool _descriptorPoolShadow;
        //cpu - gpu sync variables
        private int MAX_FRAMES_IN_FLIGHT = 2;
        private int _currentFrame = 0;
        private Semaphore[] _imageAvailableSemaphores;
        private Semaphore[] _renderFinishedSemaphores;
        private Fence[] _fencesInFlight;
        private Fence[] _imagesInFlight;
        //-------------------------------------
        internal static Sampler _textureSampler;
        internal static Sampler _shadowmapSampler;
        //
        internal static List<Entity> _entitiesToRender = new List<Entity>();
        internal static List<Entity> _lightsToRender = new List<Entity>();
        internal static AVulkanCamera _camera;              //camera

        public VulkanRenderer()
        {
            //some engine specific rendering prerequisites
            _rendererInstance = this;
            //end of prerequisites

            CreateLogicalDevice(requiredExtensions);        //abstract the gpu so we can communicate

            //getting the render queues ready
            int _graphicsQFamilyIndex = AVulkanHelper.FindQueueFamilyIndex(ref _gpu, ref _qfm, QueueFlags.GraphicsBit);
            uint _presentSupportIndex = AVulkanHelper.FindPresentSupportIndex(ref _qfm, ref _glWindow._driverSurface, ref _glWindow._surface);
            _graphicsQueue = _vulkan.GetDeviceQueue(_logicalDevice, (uint)_graphicsQFamilyIndex, 0);
            _presentQueue = _vulkan.GetDeviceQueue(_logicalDevice, _presentSupportIndex, 0);

            //create the swapchain
            _swapchain = new AVulkanSwapchain(ref _glWindow._driverSurface, ref _glWindow._surface);
            _swapchain.DoSwapchainMethodSequence(ref _extent);        //swapchain methods for simplicity sake
            _swapimageCount = _swapchain._swapchainImages.Length;     //engine related thing
            _camera = new AVulkanCamera();

            //initiate the draw command pipeline
            CreateDescriptorSetLayout();                    //
            CreateShadowDescriptorSetLayout();              //
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

        internal override void AddEntityToRenderQueue(Entity _m)
        {
            _entitiesToRender.Add(_m);
            for (int i = 0; i < _entitiesToRender.Count; i++)
                _entitiesToRender[i].GetComponent<MeshComponent>().FreeDescriptorSets();
            if (_descriptorPool.Handle != 0)
                _vulkan.DestroyDescriptorPool(_logicalDevice, _descriptorPool, null);
            if (_descriptorPoolShadow.Handle != 0)
                _vulkan.DestroyDescriptorPool(_logicalDevice, _descriptorPoolShadow, null);
            CreateDescriptorPool();
            CreateShadowDescriptorPool();
            for (int i = 0; i < _entitiesToRender.Count; i++)
                _entitiesToRender[i].GetComponent<MeshComponent>().ReinstantiateDesriptorSets();

            RecreateCommandBuffers();
        }

        internal override void AddLightToRenderQueue(Entity _l)
        {
            _lightsToRender.Add(_l);
            if (_lightsToRender.Count == 1)
            {
                AVulkanBufferHandler.CreateLightsBuffer(ref _lightsToRender, ref _lightBuffer, ref _lightBufferMemory);
                AVulkanBufferHandler.CreateLightUBO(ref _lightUBO, ref _lightUBOMemory, 1);
            }
            else
            {
                _vulkan.FreeMemory(_logicalDevice, _lightBufferMemory, null);
                AVulkanBufferHandler.RecreateLightsBuffer(ref _lightsToRender, ref _lightBuffer, ref _lightBufferMemory);
                foreach (Buffer b in _lightUBO)
                    _vulkan.DestroyBuffer(_logicalDevice, b, null);
                AVulkanBufferHandler.CreateLightUBO(ref _lightUBO, ref _lightUBOMemory, _lightsToRender.Count);
                foreach (Entity _e in _entitiesToRender)
                {
                    _e.GetComponent<MeshComponent>().FreeDescriptorSets();
                    _e.GetComponent<MeshComponent>().ReinstantiateDesriptorSets();
                }
            }
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
                _entitiesToRender[i].GetComponent<MeshComponent>().CreateShadowDescriptorSet();
            }
            RecreateCommandBuffers();

            _imagesInFlight = new Fence[_swapchain._swapchainImages.Length];
        }

        private void CreateGraphicsPipeline()
        {
            _pipeline = new AVulkanGraphicsPipeline();
            _pipeline.CreateGraphicsPipeline("vulkan.vert.spv", "vulkan.frag.spv", _extent, ref _descriptorSetLayout);
            _pipeline.CreateShadwomapPipeline("Shadowmap.vert.spv", new Extent2D(2000, 2000), ref _descriptorSetLayoutShadow);
        }

        private void CreateFrameBuffers()
        {
            _framebuffer = new Framebuffer[_swapchain!._imageViews.Length];
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
                    if (_vulkan.CreateFramebuffer(_logicalDevice, _framebufferInfo, null, out _framebuffer[i]) != Result.Success)
                    {
                        throw new Exception("Failed to create frame buffer");
                    }
                }
            }
        }

        public void RecreateCommandBuffers()
        {
            fixed (CommandBuffer* CBPtr = _commandBuffer)
            {
                _vulkan.FreeCommandBuffers(_logicalDevice, _commandPool, (uint)_commandBuffer.Length, CBPtr);
            }
            CreateCommandBuffers();
        }

        private void CreateCommandBuffers()
        {
            _commandBuffer = new CommandBuffer[_framebuffer.Length];

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
                //normal render pass info
                RenderPassBeginInfo _renderPassInfo = new RenderPassBeginInfo()
                {
                    SType = StructureType.RenderPassBeginInfo,
                    RenderPass = _swapchain._renderPass,
                    Framebuffer = _framebuffer[i],
                    RenderArea =
                    {
                        Offset = { X = 0, Y=0 },
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
                        _entitiesToRender[e].GetComponent<MeshComponent>().EnqueuShadowDrawCommands(_offset, i, ref _commandBuffer[i], j);
                    }
                    _vulkan.CmdEndRenderPass(_commandBuffer[i]);
                }
                //player view
                _vulkan.CmdBindPipeline(_commandBuffer[i], PipelineBindPoint.Graphics, _pipeline._graphicsPipeline);
                _vulkan.CmdBeginRenderPass(_commandBuffer[i], &_renderPassInfo, SubpassContents.Inline);
                for (int e = 0; e < _entitiesToRender.Count; e++)
                {
                    var _offset = new ulong[] { 0 };
                    _entitiesToRender[e].GetComponent<MeshComponent>().EnqueueDrawCommands(_offset, i, ref _commandBuffer[i]);
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

        private void CreateDescriptorSetLayout()
        {
            DescriptorSetLayoutBinding _uboLayoutBinding = new DescriptorSetLayoutBinding()
            {
                Binding = 0,
                DescriptorCount = 1,
                DescriptorType = DescriptorType.UniformBuffer,
                PImmutableSamplers = null,
                StageFlags = ShaderStageFlags.VertexBit
            };

            DescriptorSetLayoutBinding _matrixLayoutBinding = new DescriptorSetLayoutBinding()
            {
                Binding = 1,
                DescriptorCount = 1,
                DescriptorType = DescriptorType.StorageBuffer,
                PImmutableSamplers = null,
                StageFlags = ShaderStageFlags.VertexBit
            };

            DescriptorSetLayoutBinding _lightLayoutBinding = new DescriptorSetLayoutBinding()
            {
                Binding = 2,
                DescriptorCount = 1,
                DescriptorType = DescriptorType.StorageBuffer,
                PImmutableSamplers = null,
                StageFlags = ShaderStageFlags.FragmentBit
            };

            DescriptorSetLayoutBinding _samplerLayoutBinding = new DescriptorSetLayoutBinding()
            {
                Binding = 3,
                DescriptorCount = 1,
                DescriptorType = DescriptorType.CombinedImageSampler,
                PImmutableSamplers = null,
                StageFlags = ShaderStageFlags.FragmentBit
            };
            DescriptorSetLayoutBinding _shadowSamplerLayoutBinding = new DescriptorSetLayoutBinding()
            {
                Binding = 4,
                DescriptorCount = 1,
                DescriptorType = DescriptorType.CombinedImageSampler,
                PImmutableSamplers = null,
                StageFlags = ShaderStageFlags.FragmentBit
            };

            var _bindings = new DescriptorSetLayoutBinding[] { _uboLayoutBinding, _matrixLayoutBinding, _lightLayoutBinding, _samplerLayoutBinding, _shadowSamplerLayoutBinding };
            fixed (DescriptorSetLayoutBinding* _bindingsPtr = _bindings)
            fixed (DescriptorSetLayout* _descSetLayoutPtr = &_descriptorSetLayout)
            {
                DescriptorSetLayoutCreateInfo _layoutCreateInfo = new DescriptorSetLayoutCreateInfo()
                {
                    SType = StructureType.DescriptorSetLayoutCreateInfo,
                    BindingCount = (uint)_bindings.Length,
                    PBindings = _bindingsPtr,
                };
                if (_vulkan.CreateDescriptorSetLayout(_logicalDevice, _layoutCreateInfo, null, _descSetLayoutPtr) != Result.Success)
                {
                    throw new Exception("Failed to create descriptor set layout");
                }
            }
        }

        private void CreateShadowDescriptorSetLayout()
        {
            DescriptorSetLayoutBinding _uboLayoutBinding = new DescriptorSetLayoutBinding()
            {
                Binding = 0,
                DescriptorCount = 1,
                DescriptorType = DescriptorType.StorageBuffer,
                PImmutableSamplers = null,
                StageFlags = ShaderStageFlags.VertexBit
            };

            DescriptorSetLayoutBinding _matrixLayoutBinding = new DescriptorSetLayoutBinding()
            {
                Binding = 1,
                DescriptorCount = 1,
                DescriptorType = DescriptorType.StorageBuffer,
                PImmutableSamplers = null,
                StageFlags = ShaderStageFlags.VertexBit
            };

            var _bindings = new DescriptorSetLayoutBinding[] { _uboLayoutBinding, _matrixLayoutBinding };
            fixed (DescriptorSetLayoutBinding* _bindingsPtr = _bindings)
            fixed (DescriptorSetLayout* _descSetLayoutPtr = &_descriptorSetLayoutShadow)
            {
                DescriptorSetLayoutCreateInfo _layoutCreateInfo = new DescriptorSetLayoutCreateInfo()
                {
                    SType = StructureType.DescriptorSetLayoutCreateInfo,
                    BindingCount = (uint)_bindings.Length,
                    PBindings = _bindingsPtr,
                };
                if (_vulkan.CreateDescriptorSetLayout(_logicalDevice, _layoutCreateInfo, null, _descSetLayoutPtr) != Result.Success)
                {
                    throw new Exception("Failed to create descriptor set layout");
                }
            }
        }

        private void CreateDescriptorPool()
        {
            var _poolSizes = new DescriptorPoolSize[]
            {
                new DescriptorPoolSize()
                {
                    Type = DescriptorType.UniformBuffer,
                    DescriptorCount = (uint)(_swapchain._swapchainImages.Length * _entitiesToRender.Count) +1
                },
                new DescriptorPoolSize()
                {
                    Type = DescriptorType.StorageBuffer,
                    DescriptorCount = (uint)(_swapchain._swapchainImages.Length * _entitiesToRender.Count * 2) + 1
                },
                new DescriptorPoolSize()
                {
                    Type = DescriptorType.CombinedImageSampler,
                    DescriptorCount = (uint)(_swapchain._swapchainImages.Length * _entitiesToRender.Count * 2) + 1
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
                    MaxSets = (uint)(_swapchain._swapchainImages.Length * _entitiesToRender.Count * 5 + 1),
                    Flags = DescriptorPoolCreateFlags.FreeDescriptorSetBit
                };
                if (_vulkan.CreateDescriptorPool(_logicalDevice, _poolInfo, null, _descPoolPtr) != Result.Success)
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
                    DescriptorCount = (uint)(_swapchain!._swapchainImages.Length * _entitiesToRender.Count * 2) + 1
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
                if (_vulkan.CreateDescriptorPool(_logicalDevice, _poolInfo, null, _descPoolPtr) != Result.Success)
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
                Result r = _vulkan.CreateSampler(_logicalDevice, _createInfo, null, _textureSamplerPtr);
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
                Result r = _vulkan.CreateSampler(_logicalDevice, _shadowInfo, null, _textureSamplerPtr);
                if (r != Result.Success)
                {
                    throw new Exception("Failed to create a shadowmap sampler with error: " + r);
                }
            }
        }

        internal override void Draw()
        {
            _camera.ProcessKeyboard();
            _vulkan.WaitForFences(_logicalDevice, 1, _fencesInFlight[_currentFrame], true, ulong.MaxValue);
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
            if (_lightsToRender.Count > 0)
            {
                AVulkanBufferHandler.UpdateLightsBuffer(ref _lightsToRender, ref _lightBufferMemory);
                AVulkanBufferHandler.UpdateLightUniforms(ref _lightsToRender, _imageIndex, ref _lightUBOMemory);
            }
            //update uniforms
            foreach (Entity e in _lightsToRender)
            {
                e.GetComponent<LightsourceComponent>().UpdateVPMatrices(_imageIndex);
            }
            foreach (Entity e in _entitiesToRender)
            {
                e.GetComponent<MeshComponent>().UpdateMatrices();
            }

            if (_imagesInFlight[_imageIndex].Handle != default)
            {
                _vulkan.WaitForFences(_logicalDevice, 1, _imagesInFlight[_imageIndex], true, ulong.MaxValue);
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

            _vulkan.ResetFences(_logicalDevice, 1, _fencesInFlight[_currentFrame]);
            r = _vulkan.QueueSubmit(_graphicsQueue, 1, _submitInfo, _fencesInFlight[_currentFrame]);
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
            r = _swapchain._driverSwapchain.QueuePresent(_presentQueue, _presentInfo);
            if (r == Result.ErrorOutOfDateKhr || r == Result.SuboptimalKhr || _glWindow._frameBufferResized)
            {
                _glWindow._frameBufferResized = false;
                RecreateSwapChain();
            }
            else if (r != Result.Success)
            {
                throw new Exception("Failed to present swap chain image");
            }

            _currentFrame = (_currentFrame + 1) % MAX_FRAMES_IN_FLIGHT;
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

        private void CleanUpSwapChain()
        {
            foreach (var fb in _framebuffer)
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