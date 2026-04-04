using ArctisAurora.Core.UISystem.Controls;
using ArctisAurora.EngineWork.Rendering.Helpers;
using ArctisAurora.EngineWork.Rendering.MeshSubComponents;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using System.Runtime.CompilerServices;
using static ArctisAurora.Core.UISystem.Controls.VulkanControl;
using ImageLayout = Silk.NET.Vulkan.ImageLayout;

namespace ArctisAurora.EngineWork.Rendering.Modules
{
    public unsafe class UIModule : RenderingModule
    {
        internal override ERendererTypes rendererType => ERendererTypes.UITemp;

        internal override ERendererStage RendererStage => ERendererStage.UI;

        internal override uint[][] descriptorMaxCounts => new uint[][] {
            new uint[] { 1, 1, 50000 },       // set 0: UBO ×1, SSBO ×1, SSBO array ×50k
            new uint[] { 50000 }               // set 1: sampler array ×50k
        };

        internal override uint GetVariableDescriptorCount(int set)
        {
            return (uint)EntityManager.controls.Count;
        }

        internal override PhysicalDeviceFeatures features => new()
        {
            SamplerAnisotropy = true,
        };

        internal override PhysicalDeviceVulkan12Features features12 => new()
        {
            SType = StructureType.PhysicalDeviceVulkan12Features,
            BufferDeviceAddress = true,
            RuntimeDescriptorArray = true,
            DescriptorBindingVariableDescriptorCount = true,
            DescriptorIndexing = true,
            DescriptorBindingPartiallyBound = true,
            DescriptorBindingStorageBufferUpdateAfterBind = true,
            DescriptorBindingUniformBufferUpdateAfterBind = true,
            DescriptorBindingSampledImageUpdateAfterBind = true,
            DescriptorBindingStorageImageUpdateAfterBind = true,
            DescriptorBindingStorageTexelBufferUpdateAfterBind = true,
            DescriptorBindingUniformTexelBufferUpdateAfterBind = true,
            ShaderStorageBufferArrayNonUniformIndexing = true,
            ShaderSampledImageArrayNonUniformIndexing = true
        };

        internal override List<List<DescriptorType>> descriptorTypes => new()
        {
            new List<DescriptorType> {
                DescriptorType.UniformBuffer, DescriptorType.StorageBuffer,
                DescriptorType.StorageBuffer
            },
            new List<DescriptorType> {
                DescriptorType.CombinedImageSampler
            }
        };
        internal override List<List<ShaderStageFlags>> shaderStages => new()
        {
            new List<ShaderStageFlags>{
                ShaderStageFlags.VertexBit, ShaderStageFlags.VertexBit,
                ShaderStageFlags.VertexBit
            },
            new List<ShaderStageFlags>{
                ShaderStageFlags.FragmentBit
            },
        };
        internal override DescriptorBindingFlags[][] descriptorBindingFlags => [
            [
                DescriptorBindingFlags.None, DescriptorBindingFlags.None,
                DescriptorBindingFlags.VariableDescriptorCountBit | DescriptorBindingFlags.PartiallyBoundBit
            ],
            [
                DescriptorBindingFlags.VariableDescriptorCountBit | DescriptorBindingFlags.PartiallyBoundBit
            ]
        ];
        internal override int variableSetCount => 2;


        internal static MCUI meshComponent;


        public UIModule()
        {}

        internal override void UpdateModule(int currentFrame)
        {
            meshComponent.MakeInstanced();
            if (frameResources == null)
            {
                frameResources = new FrameResources[Renderer.swapchainImageCount];
                for (int i = 0; i < Renderer.swapchainImageCount; i++)
                {
                    CreateDescriptorPool(i);
                    AllocateDescriptorSets(i);
                    UpdateDescriptorSets(i);
                }
            }
            else
            {
                CreateDescriptorPool(currentFrame);
                AllocateDescriptorSets(currentFrame);
                UpdateDescriptorSets(currentFrame);
            }
            WriteCommandBuffers(currentFrame);
        }

        internal override void PrepareObjects()
        {
            Renderer.renderer.CreateCommandPool((uint)Renderer.queueAllocator.GetFamilyIndex(QueueFlags.GraphicsBit), out moduleCommandPool, CommandPoolCreateFlags.ResetCommandBufferBit);
            RegisterVulkanQueue(Renderer.queueAllocator, Renderer.vk, ref Renderer.logicalDevice);
            meshComponent = new MCUI();
            PrepareCamera();
        }

        internal override void CreateRenderPass()
        {
            AttachmentDescription _colorAttachment = new AttachmentDescription()
            {
                Format = Renderer.renderer.surfaceFormat.Format,
                Samples = SampleCountFlags.Count1Bit,
                LoadOp = AttachmentLoadOp.Clear,
                StoreOp = AttachmentStoreOp.Store,
                StencilLoadOp = AttachmentLoadOp.DontCare,
                InitialLayout = ImageLayout.Undefined,
                FinalLayout = ImageLayout.ShaderReadOnlyOptimal,
            };

            AttachmentReference _colorAttachmentRef = new AttachmentReference()
            {
                Attachment = 0,
                Layout = ImageLayout.ColorAttachmentOptimal,
            };

            /*AttachmentDescription _depthAttachment = new AttachmentDescription()
            {
                Format = AVulkanHelper.GetDepthFormat(),
                Samples = SampleCountFlags.Count1Bit,
                LoadOp = AttachmentLoadOp.Clear,
                StoreOp = AttachmentStoreOp.DontCare,
                StencilLoadOp = AttachmentLoadOp.DontCare,
                StencilStoreOp = AttachmentStoreOp.DontCare,
                InitialLayout = ImageLayout.Undefined,
                FinalLayout = ImageLayout.DepthStencilAttachmentOptimal
            };*/

            /*AttachmentReference _depthAttachmentRef = new AttachmentReference()
            {
                Attachment = 1,
                Layout = ImageLayout.DepthStencilAttachmentOptimal
            };*/

            SubpassDescription _subpass = new SubpassDescription()
            {
                PipelineBindPoint = PipelineBindPoint.Graphics,
                ColorAttachmentCount = 1,
                PColorAttachments = &_colorAttachmentRef,
                //PDepthStencilAttachment = &_depthAttachmentRef
            };

            SubpassDependency _writeDependency = new SubpassDependency()
            {
                SrcSubpass = Vk.SubpassExternal,
                DstSubpass = 0,
                SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit /*| PipelineStageFlags.EarlyFragmentTestsBit*/,
                SrcAccessMask = 0,
                DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit /*| PipelineStageFlags.EarlyFragmentTestsBit*/,
                DstAccessMask = AccessFlags.ColorAttachmentWriteBit //| AccessFlags.DepthStencilAttachmentWriteBit
            };

            SubpassDependency _readDependency = new SubpassDependency()
            {
                SrcSubpass = 0,
                DstSubpass = Vk.SubpassExternal,
                SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit /*| PipelineStageFlags.EarlyFragmentTestsBit*/,
                SrcAccessMask = AccessFlags.ColorAttachmentWriteBit /*| AccessFlags.DepthStencilAttachmentWriteBit*/,
                DstStageMask = PipelineStageFlags.FragmentShaderBit,
                DstAccessMask = AccessFlags.ShaderReadBit
            };

            var _dependencies = new[] { _writeDependency, _readDependency };
            var _attachments = new[] { _colorAttachment/*, _depthAttachment*/ };
            fixed (SubpassDependency* _dependencyPtr = _dependencies)
            fixed (AttachmentDescription* _attachmentPtr = _attachments)
            {
                RenderPassCreateInfo _renderPassInfo = new RenderPassCreateInfo()
                {
                    SType = StructureType.RenderPassCreateInfo,
                    AttachmentCount = (uint)_attachments.Length,
                    PAttachments = _attachmentPtr,
                    SubpassCount = 1,
                    PSubpasses = &_subpass,
                    DependencyCount = 1,
                    PDependencies = _dependencyPtr
                    };

                if (Renderer.vk.CreateRenderPass(Renderer.logicalDevice, ref _renderPassInfo, null, out renderPass) != Result.Success)
                {
                    throw new Exception("failed to create render pass!");
                }
            }
        }

        internal override void CreateDescriptorPoolSizes(uint swapchainImageCount)
        {
            uint controlCount = (uint)EntityManager.controls.Count;
            descriptorPoolSizes =
            [
                new DescriptorPoolSize()
                {
                    Type = DescriptorType.UniformBuffer,
                    DescriptorCount = swapchainImageCount + 1
                },
                new DescriptorPoolSize()
                {
                    Type = DescriptorType.StorageBuffer,
                    DescriptorCount = swapchainImageCount * controlCount + 2
                },
                new DescriptorPoolSize()
                {
                    Type = DescriptorType.CombinedImageSampler,
                    DescriptorCount = swapchainImageCount * controlCount + 1
                }
            ];
        }

        internal override void CreateDescriptorPool(int currentFrame)
        {
            if (frameResources[currentFrame] == null)
                frameResources[currentFrame] = new FrameResources();

            if (frameResources[currentFrame].pool.Handle != default)
                Renderer.vk.DestroyDescriptorPool(Renderer.logicalDevice, frameResources[currentFrame].pool, null);

            CreateDescriptorPoolSizes(1);
            fixed (DescriptorPoolSize* sizesPtr = descriptorPoolSizes)
            {
                DescriptorPoolCreateInfo _createInfo = new DescriptorPoolCreateInfo()
                {
                    SType = StructureType.DescriptorPoolCreateInfo,
                    PoolSizeCount = (uint)descriptorPoolSizes.Length,
                    PPoolSizes = sizesPtr,
                    MaxSets = (uint)variableSetCount,
                    Flags = DescriptorPoolCreateFlags.None
                };
                if (Renderer.vk.CreateDescriptorPool(Renderer.logicalDevice, ref _createInfo, null, out frameResources[currentFrame].pool) != Result.Success)
                {
                    throw new Exception("Failed to create descriptor pool");
                }
            }
        }


        internal override void UpdateDescriptorSets(int currentFrame)
        {
            UpdateFirstDescriptorSets(currentFrame);
            UpdateSecondDescriptorSets(currentFrame);
        }

        private void UpdateFirstDescriptorSets(int currentFrame)
        {
            //for (int i = 0; i < Renderer.swapchainImageCount; i++)
            //{
                DescriptorBufferInfo cameraInfo = new DescriptorBufferInfo()
                {
                    Buffer = camera._cameraBuffer[0],
                    Offset = 0,
                    Range = (ulong)Unsafe.SizeOf<UBO>()
                };
                DescriptorBufferInfo transformInfo = new DescriptorBufferInfo()
                {
                    Buffer = meshComponent.transformsBuffer,
                    Offset = 0,
                    Range = (ulong)(sizeof(float) * 16 * EntityManager.controls.Count)
                };

                DescriptorBufferInfo[] controlDataInfos = new DescriptorBufferInfo[EntityManager.controls.Count];
                for (int j = 0; j < EntityManager.controls.Count; j++)
                {
                    VulkanControl control = EntityManager.controls[j];
                    controlDataInfos[j] = new()
                    {
                        Buffer = control.controlDataBuffer,
                        Offset = 0,
                        Range = (ulong)Unsafe.SizeOf<ControlData>()
                    };
                }
                fixed (DescriptorBufferInfo* controlDataInfosPtr = controlDataInfos)
                {
                    var writeDescriptorSets = new WriteDescriptorSet[]
                    {
                        new WriteDescriptorSet
                        {
                            SType = StructureType.WriteDescriptorSet,
                            DstSet = frameResources[currentFrame].sets[0],
                            DstBinding = 0,
                            DescriptorCount = 1,
                            DstArrayElement = 0,
                            DescriptorType = DescriptorType.UniformBuffer,
                            PBufferInfo = &cameraInfo
                        },
                        new WriteDescriptorSet
                        {
                            SType = StructureType.WriteDescriptorSet,
                            DstSet = frameResources[currentFrame].sets[0],
                            DstBinding = 1,
                            DescriptorCount = 1,
                            DstArrayElement = 0,
                            DescriptorType = DescriptorType.StorageBuffer,
                            PBufferInfo = &transformInfo
                        },
                        new WriteDescriptorSet
                        {
                            SType = StructureType.WriteDescriptorSet,
                            DstSet = frameResources[currentFrame].sets[0],
                            DstBinding = 2,
                            DescriptorCount = (uint)EntityManager.controls.Count,
                            DstArrayElement = 0,
                            DescriptorType = DescriptorType.StorageBuffer,
                            PBufferInfo = controlDataInfosPtr
                        }
                    };
                    fixed (WriteDescriptorSet* descPtr = writeDescriptorSets)
                    {
                        Renderer.vk!.UpdateDescriptorSets(Renderer.logicalDevice, (uint)writeDescriptorSets.Length, descPtr, 0, null);
                    }
                }
            //}
        }

        private void UpdateSecondDescriptorSets(int currentFrame)
        {
            for (int i = 0; i < Renderer.swapchainImageCount; i++)
            {
                DescriptorImageInfo[] samplersInfos = new DescriptorImageInfo[EntityManager.controls.Count];
                for (int j = 0; j < EntityManager.controls.Count; j++)
                {
                    VulkanControl control = EntityManager.controls[j];
                    samplersInfos[j] = new()
                    {
                        ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
                        ImageView = control.maskAsset.textureImageView,
                        Sampler = control.maskSampler
                    };
                }
                fixed (DescriptorImageInfo* samplerInfosPtr = samplersInfos)
                {
                    var writeDescriptorSets = new WriteDescriptorSet[]
                    {
                        new WriteDescriptorSet
                        {
                            SType = StructureType.WriteDescriptorSet,
                            DstSet = frameResources[currentFrame].sets[1],
                            DstBinding = 0,
                            DescriptorCount = (uint)EntityManager.controls.Count,
                            DstArrayElement = 0,
                            DescriptorType = DescriptorType.CombinedImageSampler,
                            PImageInfo = samplerInfosPtr
                        }
                    };
                    fixed (WriteDescriptorSet* descPtr = writeDescriptorSets)
                    {
                        Renderer.vk!.UpdateDescriptorSets(Renderer.logicalDevice, (uint)writeDescriptorSets.Length, descPtr, 0, null);
                    }
                }
            }
        }

        internal override void CreatePipeline()
        {
            byte[] vertexCode = ReadFile("../../../Shaders/" + "UIRasterizer/UI.vert.spv");
            byte[] fragmentCode = ReadFile("../../../Shaders/" + "UIRasterizer/UI.frag.spv");

            ShaderModule vertexShader = CreateShaderModule(ref Renderer.vk, ref Renderer.logicalDevice, vertexCode);
            ShaderModule fragmentShader = CreateShaderModule(ref Renderer.vk, ref Renderer.logicalDevice, fragmentCode);

            PipelineShaderStageCreateInfo vertexShaderStageInfo = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.VertexBit,
                Module = vertexShader,
                PName = (byte*)SilkMarshal.StringToPtr("main")
            };
            PipelineShaderStageCreateInfo fragmentShaderStageInfo = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.FragmentBit,
                Module = fragmentShader,
                PName = (byte*)SilkMarshal.StringToPtr("main")
            };

            var stages = stackalloc[]
            {
                vertexShaderStageInfo,
                fragmentShaderStageInfo
            };

            VertexInputBindingDescription bindingDesc = Vertex.GetBindingDescription();
            VertexInputAttributeDescription[] attribDesc = Vertex.GetVertexInputAttributeDescriptions();



            fixed (VertexInputAttributeDescription* attribDescPtr = attribDesc)
            fixed (DescriptorSetLayout* descriptorSetLayoutsPtr = descriptorSetLayouts)
            {
                PipelineVertexInputStateCreateInfo vertexInputInfo = new PipelineVertexInputStateCreateInfo
                {
                    SType = StructureType.PipelineVertexInputStateCreateInfo,
                    VertexBindingDescriptionCount = 1,
                    VertexAttributeDescriptionCount = (uint)attribDesc.Length,
                    PVertexBindingDescriptions = &bindingDesc,
                    PVertexAttributeDescriptions = attribDescPtr
                };
                PipelineInputAssemblyStateCreateInfo inputAssembly = new PipelineInputAssemblyStateCreateInfo
                {
                    SType = StructureType.PipelineInputAssemblyStateCreateInfo,
                    Topology = PrimitiveTopology.TriangleList,
                    PrimitiveRestartEnable = false
                };

                Viewport viewport = new Viewport()
                {
                    X = 0,
                    Y = 0,
                    Width = Engine.window.windowSize.Width,
                    Height = Engine.window.windowSize.Height,
                    MinDepth = 0,
                    MaxDepth = 1
                };
                Rect2D scissor = new Rect2D()
                {
                    Offset = { X = 0, Y = 0 },
                    Extent = Engine.window.windowSize
                };
                PipelineViewportStateCreateInfo viewportState = new PipelineViewportStateCreateInfo()
                {
                    SType = StructureType.PipelineViewportStateCreateInfo,
                    ViewportCount = 1,
                    PViewports = &viewport,
                    ScissorCount = 1,
                    PScissors = &scissor,
                };
                PipelineRasterizationStateCreateInfo rasterizer = new PipelineRasterizationStateCreateInfo()
                {
                    SType = StructureType.PipelineRasterizationStateCreateInfo,
                    DepthClampEnable = false,
                    RasterizerDiscardEnable = false,
                    PolygonMode = PolygonMode.Fill,
                    LineWidth = 1,
                    CullMode = CullModeFlags.None,
                    FrontFace = FrontFace.Clockwise,
                    DepthBiasEnable = false
                };
                PipelineMultisampleStateCreateInfo multisampling = new PipelineMultisampleStateCreateInfo()
                {
                    SType = StructureType.PipelineMultisampleStateCreateInfo,
                    SampleShadingEnable = false,
                    RasterizationSamples = SampleCountFlags.Count1Bit
                };
                PipelineDepthStencilStateCreateInfo depthCreateInfo = new PipelineDepthStencilStateCreateInfo()
                {
                    SType = StructureType.PipelineDepthStencilStateCreateInfo,
                    DepthTestEnable = false,
                    DepthWriteEnable = false,
                    DepthCompareOp = CompareOp.Less,
                    DepthBoundsTestEnable = false,
                    StencilTestEnable = false
                };
                PipelineColorBlendAttachmentState colorBlendAttachment = new PipelineColorBlendAttachmentState()
                {
                    ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit | ColorComponentFlags.BBit | ColorComponentFlags.ABit,
                    BlendEnable = true,
                    SrcAlphaBlendFactor = BlendFactor.SrcAlpha,
                    DstColorBlendFactor = BlendFactor.OneMinusSrcAlpha,

                    ColorBlendOp = BlendOp.Add,
                    SrcColorBlendFactor = BlendFactor.SrcAlpha,
                    DstAlphaBlendFactor = BlendFactor.OneMinusSrcAlpha,

                    AlphaBlendOp = BlendOp.Add
                };
                PipelineColorBlendStateCreateInfo colorBlending = new PipelineColorBlendStateCreateInfo()
                {
                    SType = StructureType.PipelineColorBlendStateCreateInfo,
                    LogicOpEnable = false,
                    LogicOp = LogicOp.Copy,
                    AttachmentCount = 1,
                    PAttachments = &colorBlendAttachment
                };

                colorBlending.BlendConstants[0] = 0;
                colorBlending.BlendConstants[1] = 0;
                colorBlending.BlendConstants[2] = 0;
                colorBlending.BlendConstants[3] = 0;

                PipelineLayoutCreateInfo pipelineLayoutInfo = new PipelineLayoutCreateInfo()
                {
                    SType = StructureType.PipelineLayoutCreateInfo,
                    SetLayoutCount = (uint)variableSetCount,
                    PushConstantRangeCount = 0,
                    PSetLayouts = descriptorSetLayoutsPtr
                };

                if (Renderer.vk.CreatePipelineLayout(Renderer.logicalDevice, ref pipelineLayoutInfo, null, out pipelineLayout) != Result.Success)
                {
                    throw new Exception("Failed to create pipeline layout");
                }

                GraphicsPipelineCreateInfo graphicsPipelineInfo = new GraphicsPipelineCreateInfo()
                {
                    SType = StructureType.GraphicsPipelineCreateInfo,
                    StageCount = 2,
                    PStages = stages,
                    PVertexInputState = &vertexInputInfo,
                    PInputAssemblyState = &inputAssembly,
                    PViewportState = &viewportState,
                    PRasterizationState = &rasterizer,
                    PMultisampleState = &multisampling,
                    PDepthStencilState = &depthCreateInfo,
                    PColorBlendState = &colorBlending,
                    Layout = pipelineLayout,
                    RenderPass = renderPass,
                    Subpass = 0,
                    BasePipelineHandle = default
                };

                Result r = Renderer.vk.CreateGraphicsPipelines(Renderer.logicalDevice, default, 1, ref graphicsPipelineInfo, null, out pipeline);
                if (r != Result.Success)
                {
                    throw new Exception("Failed to create graphics pipeline " + r);
                }
            }

            Renderer.vk.DestroyShaderModule(Renderer.logicalDevice, vertexShader, null);
            Renderer.vk.DestroyShaderModule(Renderer.logicalDevice, fragmentShader, null);
            SilkMarshal.Free((nint)vertexShaderStageInfo.PName);
            SilkMarshal.Free((nint)fragmentShaderStageInfo.PName);
        }

        internal override void CreateModuleFrameBuffers()
        {
            frameBuffers = new Framebuffer[Renderer.swapchainImageCount];
            for (int i = 0; i < Renderer.swapchainImageCount; i++)
            {
                var _attachment = new[] { outputImageViews[i] };

                fixed (ImageView* _imAttachmentPtr = _attachment)
                {
                    FramebufferCreateInfo _framebufferInfo = new FramebufferCreateInfo()
                    {
                        SType = StructureType.FramebufferCreateInfo,
                        RenderPass = renderPass,
                        AttachmentCount = (uint)_attachment.Length,
                        PAttachments = _imAttachmentPtr,
                        Width = Engine.window.windowSize.Width,
                        Height = Engine.window.windowSize.Height,
                        Layers = 1
                    };
                    if (Renderer.vk.CreateFramebuffer(Renderer.logicalDevice, ref _framebufferInfo, null, out frameBuffers[i]) != Result.Success)
                    {
                        throw new Exception("Failed to create frame buffer");
                    }
                }
            }
        }

        internal override void PrepareCamera()
        {
            camera = new AuroraCamera();
        }

        internal override void WriteCommandBuffers(int currentFrame)
        {
            if (commandBuffers != null)
            {
                Renderer.vk.ResetCommandBuffer(commandBuffers[currentFrame], CommandBufferResetFlags.None);
                WriteCommandBuffer(currentFrame);
            }
            else
            {
                commandBuffers = new CommandBuffer[Renderer.swapchainImageCount];

                CommandBufferAllocateInfo _allocInfo = new CommandBufferAllocateInfo()
                {
                    SType = StructureType.CommandBufferAllocateInfo,
                    CommandPool = moduleCommandPool,
                    //CommandPool = Renderer.compositeCommandPool,
                    Level = CommandBufferLevel.Primary,
                    CommandBufferCount = (uint)commandBuffers.Length
                };
                fixed (CommandBuffer* _commandBufferPtr = commandBuffers)
                {
                    Result r = Renderer.vk.AllocateCommandBuffers(Renderer.logicalDevice, ref _allocInfo, _commandBufferPtr);
                    if (r != Result.Success)
                    {
                        throw new Exception("Failed to allocate command buffer with error " + r);
                    }
                }
                for(int i=0; i < commandBuffers.Length; i++)
                {
                    WriteCommandBuffer(i);
                }
            }
            isDirty[currentFrame] = false;
        }

        private void WriteCommandBuffer(int currentFrame)
        {
            RenderPassBeginInfo _renderPassInfo = new RenderPassBeginInfo()
            {
                SType = StructureType.RenderPassBeginInfo,
                RenderPass = renderPass,
                Framebuffer = frameBuffers[currentFrame],
                RenderArea =
                    {
                        Offset = { X = 0, Y = 0 },
                        Extent = Engine.window.windowSize
                    }
            };

            CommandBufferBeginInfo _beginInfo = new CommandBufferBeginInfo()
            {
                SType = StructureType.CommandBufferBeginInfo
            };

            if (Renderer.vk.BeginCommandBuffer(commandBuffers[currentFrame], ref _beginInfo) != Result.Success)
            {
                throw new Exception("Failed to create BEGIN command buffer at index " + currentFrame);
            }


            var _clearValues = new ClearValue[]
            {
                new ClearValue()
                {
                    Color = new ClearColorValue() { Float32_0 = 0.05f, Float32_1 = 0.05f, Float32_2 = 0.05f, Float32_3 = 1f },
                }/*,
                new ClearValue()
                {
                    DepthStencil = new ClearDepthStencilValue() { Depth = 1f, Stencil = 0 }
                },*/
            };

            fixed (ClearValue* _clrValuesPtr = _clearValues)
            {
                _renderPassInfo.ClearValueCount = (uint)_clearValues.Length;
                _renderPassInfo.PClearValues = _clrValuesPtr;
            }
            //player view
            Renderer.vk.CmdBindPipeline(commandBuffers[currentFrame], PipelineBindPoint.Graphics, pipeline);
            Renderer.vk.CmdBeginRenderPass(commandBuffers[currentFrame], &_renderPassInfo, SubpassContents.Inline);

            //IReadOnlyList<Entity> entities = EntityManager.controls;
            var _offset = new ulong[] { 0 };
            if (meshComponent.render == true)
            {
                DescriptorSet[] frameSets = frameResources[currentFrame].sets;
                meshComponent.EnqueueDrawCommands(ref _offset, currentFrame, 0, ref commandBuffers[currentFrame], ref pipelineLayout, ref frameSets);
            }
            Renderer.vk.CmdEndRenderPass(commandBuffers[currentFrame]);

            if (Renderer.vk.EndCommandBuffer(commandBuffers[currentFrame]) != Result.Success)
            {
                throw new Exception("Failed to record command buffer");
            }
        }
    }
}