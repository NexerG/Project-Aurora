using ArctisAurora.EngineWork.Rendering.Helpers;
using ArctisAurora.EngineWork.Rendering.MeshSubComponents;
using ArctisAurora.EngineWork.Rendering.UI.Controls;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using System.Runtime.CompilerServices;
using static ArctisAurora.EngineWork.Rendering.UI.Controls.VulkanControl;
using ImageLayout = Silk.NET.Vulkan.ImageLayout;

namespace ArctisAurora.EngineWork.Rendering.Modules
{
    public unsafe class UIModule : RenderingModule
    {
        internal override ERendererTypes rendererType => ERendererTypes.UITemp;

        internal override ERendererStage RendererStage => ERendererStage.UI;

        internal override uint MAX_STORAGE_BUFFERS => 50000;
        internal override uint MAX_TEXTURES => 50000;
        internal override uint MAX_UNIFORMS_BUFFERS => 50000;

        internal override PhysicalDeviceFeatures features => new PhysicalDeviceFeatures()
        {
            SamplerAnisotropy = true,
        };

        internal override PhysicalDeviceVulkan12Features features12 => new PhysicalDeviceVulkan12Features()
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

        internal override List<List<DescriptorType>> descriptorTypes => new List<List<DescriptorType>> {
            new List<DescriptorType> {
                DescriptorType.UniformBuffer, DescriptorType.StorageBuffer,
                DescriptorType.StorageBuffer
            },
            new List<DescriptorType> {
                DescriptorType.CombinedImageSampler
            }
        };
        internal override List<List<ShaderStageFlags>> shaderStages => new List<List<ShaderStageFlags>> {
            new List<ShaderStageFlags>{
                ShaderStageFlags.VertexBit, ShaderStageFlags.VertexBit,
                ShaderStageFlags.VertexBit
            },
            new List<ShaderStageFlags>{
                ShaderStageFlags.FragmentBit
            },
        };
        internal override DescriptorBindingFlags[][] descriptorBindingFlags => new DescriptorBindingFlags[][]{
            new DescriptorBindingFlags[]{
                DescriptorBindingFlags.None, DescriptorBindingFlags.None,
                DescriptorBindingFlags.VariableDescriptorCountBit | DescriptorBindingFlags.PartiallyBoundBit
            },
            new DescriptorBindingFlags[]{
                DescriptorBindingFlags.VariableDescriptorCountBit | DescriptorBindingFlags.PartiallyBoundBit
            }
        };
        internal override int variableSetCount => 2;


        internal static bool updateCommandBuffers = false;
        internal static MCUI meshComponent;


        public UIModule()
        { }

        internal override void UpdateModule()
        {
            meshComponent.MakeInstanced();
            CreateDescriptorPool();
            AllocateDescriptorSets();
            UpdateDescriptorSets();
            WriteCommandBuffers();
        }

        internal override void PrepareObjects()
        {
            meshComponent = new MCUI();
            PrepareCamera();
        }

        internal override void CreateRenderPass(ref SurfaceFormatKHR format)
        {
            AttachmentDescription _colorAttachment = new AttachmentDescription()
            {
                Format = format.Format,
                Samples = SampleCountFlags.Count1Bit,
                LoadOp = AttachmentLoadOp.Clear,
                StoreOp = AttachmentStoreOp.Store,
                StencilLoadOp = AttachmentLoadOp.DontCare,
                InitialLayout = ImageLayout.Undefined,
                FinalLayout = ImageLayout.PresentSrcKhr,
            };

            AttachmentReference _colorAttachmentRef = new AttachmentReference()
            {
                Attachment = 0,
                Layout = ImageLayout.ColorAttachmentOptimal,
            };

            AttachmentDescription _depthAttachment = new AttachmentDescription()
            {
                Format = AVulkanHelper.GetDepthFormat(),
                Samples = SampleCountFlags.Count1Bit,
                LoadOp = AttachmentLoadOp.Clear,
                StoreOp = AttachmentStoreOp.DontCare,
                StencilLoadOp = AttachmentLoadOp.DontCare,
                StencilStoreOp = AttachmentStoreOp.DontCare,
                InitialLayout = ImageLayout.Undefined,
                FinalLayout = ImageLayout.DepthStencilAttachmentOptimal
            };

            AttachmentReference _depthAttachmentRef = new AttachmentReference()
            {
                Attachment = 1,
                Layout = ImageLayout.DepthStencilAttachmentOptimal
            };

            SubpassDescription _subpass = new SubpassDescription()
            {
                PipelineBindPoint = PipelineBindPoint.Graphics,
                ColorAttachmentCount = 1,
                PColorAttachments = &_colorAttachmentRef,
                PDepthStencilAttachment = &_depthAttachmentRef
            };

            SubpassDependency _subDepend = new SubpassDependency()
            {
                SrcSubpass = Vk.SubpassExternal,
                DstSubpass = 0,
                SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.EarlyFragmentTestsBit,
                SrcAccessMask = 0,
                DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.EarlyFragmentTestsBit,
                DstAccessMask = AccessFlags.ColorAttachmentWriteBit | AccessFlags.DepthStencilAttachmentWriteBit
            };

            var _attachments = new[] { _colorAttachment, _depthAttachment };
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
                    PDependencies = &_subDepend
                };

                if (Renderer.vk.CreateRenderPass(Renderer.logicalDevice, ref _renderPassInfo, null, out renderPass) != Result.Success)
                {
                    throw new Exception("failed to create render pass!");
                }
            }
        }

        internal override void CreateDescriptorPoolSizes(uint swapchainImageCount)
        {
            descriptorPoolSizes =
            [
                new DescriptorPoolSize()
                {
                    Type = DescriptorType.UniformBuffer,
                    DescriptorCount = (uint)(swapchainImageCount * EntityManager.controls.Count * variableSetCount) + 1 
                },
                new DescriptorPoolSize()
                {
                    Type = DescriptorType.StorageBuffer,
                    DescriptorCount = (uint)(swapchainImageCount * EntityManager.controls.Count * variableSetCount) + 1
                },
                new DescriptorPoolSize()
                {
                    Type = DescriptorType.CombinedImageSampler,
                    DescriptorCount = (uint)(swapchainImageCount * EntityManager.controls.Count * variableSetCount)
                }
            ];
        }

        internal override void CreateDescriptorPool()
        {
            CreateDescriptorPoolSizes(Renderer.swapchainImageCount);
            fixed(DescriptorPoolSize* sizesPtr = descriptorPoolSizes)
            {
                DescriptorPoolCreateInfo _createInfo = new DescriptorPoolCreateInfo()
                {
                    SType = StructureType.DescriptorPoolCreateInfo,
                    PoolSizeCount = (uint)descriptorPoolSizes.Length,
                    PPoolSizes = sizesPtr,
                    MaxSets = (uint)(Renderer.swapchainImageCount * variableSetCount),
                    Flags = DescriptorPoolCreateFlags.FreeDescriptorSetBit
                };
                if (Renderer.vk.CreateDescriptorPool(Renderer.logicalDevice, ref _createInfo, null, out descriptorPool) != Result.Success)
                {
                    throw new Exception("Failed to create descriptor pool");
                }
            }
        }

        internal override void AllocateDescriptorSets()
        {
            descriptorSets = new DescriptorSet[variableSetCount][];
            for (int set = 0; set < variableSetCount; ++set)
            {
                DescriptorSetLayout[] localLayout = new DescriptorSetLayout[Renderer.swapchainImageCount];
                Array.Fill(localLayout, descriptorSetLayouts[set]);

                fixed (DescriptorSetLayout* layoutsPtr = localLayout)
                {
                    uint bufferCount = (uint)EntityManager.controls.Count;
                    uint[] entriesPer = { bufferCount, bufferCount, bufferCount };
                    descriptorSets[set] = new DescriptorSet[Renderer.swapchainImageCount];

                    fixed (uint* entriesPtr = entriesPer)
                    {
                        DescriptorSetVariableDescriptorCountAllocateInfo _variableDSCount = new()
                        {
                            SType = StructureType.DescriptorSetVariableDescriptorCountAllocateInfo,
                            DescriptorSetCount = Renderer.swapchainImageCount,      // total amount of variable descriptor SETS (shader "set = 0...")
                            PDescriptorCounts = entriesPtr                          // how many descriptors we have per set
                        };

                        DescriptorSetAllocateInfo _allocateInfo = new DescriptorSetAllocateInfo()
                        {
                            SType = StructureType.DescriptorSetAllocateInfo,
                            DescriptorPool = descriptorPool,
                            DescriptorSetCount = Renderer.swapchainImageCount,
                            PSetLayouts = layoutsPtr,
                            PNext = &_variableDSCount
                        };
                        fixed (DescriptorSet* _descriptorSetsPtr = descriptorSets[set])
                        {
                            Result r = Renderer.vk.AllocateDescriptorSets(Renderer.logicalDevice, ref _allocateInfo, _descriptorSetsPtr);
                            if (r != Result.Success)
                            {
                                throw new Exception("Failed to allocate descriptor set with error code: " + r);
                            }
                        }
                    }
                }
            }
        }

        internal override void UpdateDescriptorSets()
        {
            UpdateFirstDescriptorSets();
            UpdateSecondDescriptorSets();
        }

        private void UpdateFirstDescriptorSets()
        {
            for (int i = 0; i < Renderer.swapchainImageCount; i++)
            {
                DescriptorBufferInfo cameraInfo = new DescriptorBufferInfo()
                {
                    Buffer = camera._cameraBuffer[i],
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
                            DstSet = descriptorSets[0][i],
                            DstBinding = 0,
                            DescriptorCount = 1,
                            DstArrayElement = 0,
                            DescriptorType = DescriptorType.UniformBuffer,
                            PBufferInfo = &cameraInfo
                        },
                        new WriteDescriptorSet
                        {
                            SType = StructureType.WriteDescriptorSet,
                            DstSet = descriptorSets[0][i],
                            DstBinding = 1,
                            DescriptorCount = 1,
                            DstArrayElement = 0,
                            DescriptorType = DescriptorType.StorageBuffer,
                            PBufferInfo = &transformInfo
                        },
                        new WriteDescriptorSet
                        {
                            SType = StructureType.WriteDescriptorSet,
                            DstSet = descriptorSets[0][i],
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
            }
        }

        private void UpdateSecondDescriptorSets()
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
                            DstSet = descriptorSets[1][i],
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

        internal override void CreateFrameBuffers(ImageView[] swapchainImageViews, ImageView[] swapchainImageViewsDepth)
        {
            frameBuffers = new Framebuffer[Renderer.swapchainImageCount];
            depthFrameBuffers = new Framebuffer[Renderer.swapchainImageCount];
            for (int i = 0; i < Renderer.swapchainImageCount; i++)
            {
                var _attachment = new[] { swapchainImageViews[i], swapchainImageViewsDepth[i] };

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

        internal override void WriteCommandBuffers()
        {
            if (commandBuffers != null)
            {
                fixed (CommandBuffer* CBPtr = commandBuffers)
                {
                    Renderer.vk.FreeCommandBuffers(Renderer.logicalDevice, Renderer.commandPool, (uint)commandBuffers.Length, CBPtr);
                }
            }

            commandBuffers = new CommandBuffer[Renderer.swapchainImageCount];

            CommandBufferAllocateInfo _allocInfo = new CommandBufferAllocateInfo()
            {
                SType = StructureType.CommandBufferAllocateInfo,
                CommandPool = Renderer.commandPool,
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

            for (int index=0;index< commandBuffers.Length; index++)
            {
                RenderPassBeginInfo _renderPassInfo = new RenderPassBeginInfo()
                {
                    SType = StructureType.RenderPassBeginInfo,
                    RenderPass = renderPass,
                    Framebuffer = frameBuffers[index],
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

                if (Renderer.vk.BeginCommandBuffer(commandBuffers[index], ref _beginInfo) != Result.Success)
                {
                    throw new Exception("Failed to create BEGIN command buffer at index " + index);
                }


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
                Renderer.vk.CmdBindPipeline(commandBuffers[index], PipelineBindPoint.Graphics, pipeline);
                Renderer.vk.CmdBeginRenderPass(commandBuffers[index], &_renderPassInfo, SubpassContents.Inline);

                //IReadOnlyList<Entity> entities = EntityManager.controls;
                var _offset = new ulong[] { 0 };
                if (meshComponent.render == true)
                {
                    meshComponent.EnqueueDrawCommands(ref _offset, index, 0, ref commandBuffers[index], ref pipelineLayout, ref descriptorSets);
                }
                Renderer.vk.CmdEndRenderPass(commandBuffers[index]);

                if (Renderer.vk.EndCommandBuffer(commandBuffers[index]) != Result.Success)
                {
                    throw new Exception("Failed to record command buffer");
                }
            }
        }
    }
}