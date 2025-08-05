using ArctisAurora.EngineWork.AssetRegistry;
using ArctisAurora.EngineWork.ECS.RenderingComponents.Vulkan;
using ArctisAurora.EngineWork.EngineEntity;
using ArctisAurora.EngineWork.Rendering.Helpers;
using ArctisAurora.EngineWork.Rendering.MeshSubComponents;
using Silk.NET.Core.Native;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using System.Runtime.CompilerServices;
using ImageLayout = Silk.NET.Vulkan.ImageLayout;

namespace ArctisAurora.EngineWork.Rendering.Modules
{
    internal unsafe class UIModule : RenderingModule
    {
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
            DescriptorBindingUniformTexelBufferUpdateAfterBind = true
        };

        internal override List<DescriptorType> descriptorTypes => new List<DescriptorType> {
            DescriptorType.UniformBuffer, DescriptorType.StorageBuffer,
            DescriptorType.StorageBuffer, DescriptorType.CombinedImageSampler
        };

        internal override List<ShaderStageFlags> shaderStages => new List<ShaderStageFlags> {
            ShaderStageFlags.VertexBit, ShaderStageFlags.VertexBit,
            ShaderStageFlags.VertexBit, ShaderStageFlags.FragmentBit
        };

        internal override DescriptorBindingFlags[] descriptorBindingFlags => new DescriptorBindingFlags[]{
            DescriptorBindingFlags.None, DescriptorBindingFlags.VariableDescriptorCountBit | DescriptorBindingFlags.UpdateAfterBindBit,
            DescriptorBindingFlags.VariableDescriptorCountBit, DescriptorBindingFlags.VariableDescriptorCountBit
        };

        internal override ERendererTypes rendererType => ERendererTypes.UITemp;

        public UIModule()
        {

        }

        internal override void CreateRenderPass(ref Vk vk, ref Device logicalDevice, ref PhysicalDevice gpu, ref SurfaceFormatKHR format, ref Extent2D extent2D)
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
                Format = AVulkanHelper.GetDepthFormat(ref vk, ref gpu),
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

                if (vk.CreateRenderPass(logicalDevice, ref _renderPassInfo, null, out renderPass) != Result.Success)
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
                    DescriptorCount = (uint)(swapchainImageCount * EntityManager.controls.Count) + 1 
                },
                new DescriptorPoolSize()
                {
                    Type = DescriptorType.StorageBuffer,
                    DescriptorCount = (uint)(swapchainImageCount * EntityManager.controls.Count * 2) + 1
                },
                new DescriptorPoolSize()
                {
                    Type = DescriptorType.CombinedImageSampler,
                    DescriptorCount = (uint)(swapchainImageCount * EntityManager.controls.Count)
                }
            ];
        }

        internal override void UpdateDescriptorSets()
        {
            DescriptorSetLayout[] localLayout = new DescriptorSetLayout[Renderer.swapchainImageCount];
            Array.Fill(localLayout, descriptorSetLayout);

            fixed (DescriptorSetLayout* layoutsPtr = localLayout)
            {
                uint bufferCount = (uint)EntityManager.controls.Count;
                uint[] entriesPer = { bufferCount, bufferCount, bufferCount };
                fixed (uint* entriesPtr = entriesPer)
                {
                    DescriptorSetVariableDescriptorCountAllocateInfo _variableDSCount = new()
                    {
                        SType = StructureType.DescriptorSetVariableDescriptorCountAllocateInfo,
                        DescriptorSetCount = Renderer.swapchainImageCount, // total amount of descriptor sets
                        PDescriptorCounts = entriesPtr                  // how many descriptor sets are variable
                    };

                    DescriptorSetAllocateInfo _allocateInfo = new DescriptorSetAllocateInfo()
                    {
                        SType = StructureType.DescriptorSetAllocateInfo,
                        DescriptorPool = Renderer.descriptorPool,
                        DescriptorSetCount = Renderer.swapchainImageCount,
                        PSetLayouts = layoutsPtr,
                        PNext = &_variableDSCount
                    };

                    descriptorSets = new DescriptorSet[Renderer.swapchainImageCount];
                    fixed (DescriptorSet* _descriptorSetsPtr = descriptorSets)
                    {
                        Result r = Renderer.vk.AllocateDescriptorSets(Renderer.logicalDevice, ref _allocateInfo, _descriptorSetsPtr);
                        if (r != Result.Success)
                        {
                            throw new Exception("Failed to allocate descriptor set with error code: " + r);
                        }
                    }
                }
            }
            for (int i = 0; i < Renderer.swapchainImageCount; i++)
            {
                DescriptorBufferInfo cameraInfo = new DescriptorBufferInfo()
                {
                    Buffer = camera._cameraBuffer[i],
                    Offset = 0,
                    Range = (ulong)Unsafe.SizeOf<UBO>()
                };
                DescriptorBufferInfo[] transformUniformInfos = new DescriptorBufferInfo[EntityManager.controls.Count];
                DescriptorBufferInfo[] uvBufferInfos = new DescriptorBufferInfo[EntityManager.controls.Count];
                DescriptorImageInfo[] textureImageInfos = new DescriptorImageInfo[EntityManager.controls.Count];
                for (int j = 0; j < EntityManager.controls.Count; j++)
                {
                    MCUI component = EntityManager.controls[j].GetComponent<MCUI>();
                    textureImageInfos[j] = new()
                    {
                        ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
                        ImageView = component.fontAsset.image._textureImageView,
                        Sampler = component.textureSampler
                    };
                    transformUniformInfos[j] = new()
                    {
                        Buffer = component._transformsBuffer,
                        Offset = 0,
                        Range = sizeof(float) * 16
                    };
                    uvBufferInfos[j] = new()
                    {
                        Buffer = component.uvBuffer,
                        Offset = 0,
                        Range = (ulong)Unsafe.SizeOf<Vector2D<float>>() * 4
                    };
                }
                fixed (DescriptorBufferInfo* uvBufferInfosPtr = uvBufferInfos)
                fixed (DescriptorBufferInfo* transformInforPtr = transformUniformInfos)
                fixed (DescriptorImageInfo* textureImageInforPtr = textureImageInfos)
                {
                    var writeDescriptorSets = new WriteDescriptorSet[]
                    {
                        new WriteDescriptorSet
                        {
                            SType = StructureType.WriteDescriptorSet,
                            DstSet = descriptorSets[i],
                            DstBinding = 0,
                            DescriptorCount = 1,
                            DstArrayElement = 0,
                            DescriptorType = DescriptorType.UniformBuffer,
                            PBufferInfo = &cameraInfo
                        },
                        new WriteDescriptorSet
                        {
                            SType = StructureType.WriteDescriptorSet,
                            DstSet = descriptorSets[i],
                            DstBinding = 1,
                            DescriptorCount = (uint)EntityManager.controls.Count,
                            DstArrayElement = 0,
                            DescriptorType = DescriptorType.StorageBuffer,
                            PBufferInfo = transformInforPtr
                        },
                        new WriteDescriptorSet
                        {
                            SType = StructureType.WriteDescriptorSet,
                            DstSet = descriptorSets[i],
                            DstBinding = 2,
                            DescriptorCount = (uint)EntityManager.controls.Count,
                            DstArrayElement = 0,
                            DescriptorType = DescriptorType.StorageBuffer,
                            PBufferInfo = uvBufferInfosPtr
                        },
                        new WriteDescriptorSet
                        {
                            SType = StructureType.WriteDescriptorSet,
                            DstSet = descriptorSets[i],
                            DstBinding = 3,
                            DescriptorCount = (uint)EntityManager.controls.Count,
                            DstArrayElement = 0,
                            DescriptorType = DescriptorType.CombinedImageSampler,
                            PImageInfo = textureImageInforPtr
                        }
                    };
                    fixed (WriteDescriptorSet* descPtr = writeDescriptorSets)
                    {
                        Renderer.vk!.UpdateDescriptorSets(Renderer.logicalDevice, (uint)writeDescriptorSets.Length, descPtr, 0, null);
                    }
                }
            }
        }

        internal override void CreatePipeline(ref Vk vk, ref Device logicalDevice, ref Extent2D extent2D)
        {
            byte[] vertexCode = ReadFile("../../../Shaders/" + "UIRasterizer/UI.vert.spv");
            byte[] fragmentCode = ReadFile("../../../Shaders/" + "UIRasterizer/UI.frag.spv");

            ShaderModule vertexShader = CreateShaderModule(ref vk, ref logicalDevice, vertexCode);
            ShaderModule fragmentShader = CreateShaderModule(ref vk, ref logicalDevice, fragmentCode);

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
            fixed (DescriptorSetLayout* descriptorSetLayoutPtr = &descriptorSetLayout)
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
                    Width = extent2D.Width,
                    Height = extent2D.Height,
                    MinDepth = 0,
                    MaxDepth = 1
                };
                Rect2D scissor = new Rect2D()
                {
                    Offset = { X = 0, Y = 0 },
                    Extent = extent2D
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
                    DepthWriteEnable = true,
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
                    SetLayoutCount = 1,
                    PushConstantRangeCount = 0,
                    PSetLayouts = descriptorSetLayoutPtr
                };

                if (vk.CreatePipelineLayout(logicalDevice, ref pipelineLayoutInfo, null, out pipelineLayout) != Result.Success)
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

                Result r = vk.CreateGraphicsPipelines(logicalDevice, default, 1, ref graphicsPipelineInfo, null, out pipeline);
                if (r != Result.Success)
                {
                    throw new Exception("Failed to create graphics pipeline " + r);
                }
            }

            vk.DestroyShaderModule(logicalDevice, vertexShader, null);
            vk.DestroyShaderModule(logicalDevice, fragmentShader, null);
            SilkMarshal.Free((nint)vertexShaderStageInfo.PName);
            SilkMarshal.Free((nint)fragmentShaderStageInfo.PName);
        }

        internal override void CreateFrameBuffers(ref Vk vk, ref Device logicalDevice, ImageView[] swapchainImageViews, ImageView[] swapchainImageViewsDepth, uint swapchainImageCount, ref Extent2D extent)
        {
            frameBuffers = new Framebuffer[swapchainImageCount];
            depthFrameBuffers = new Framebuffer[swapchainImageCount];
            for (int i = 0; i < swapchainImageCount; i++)
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
                        Width = extent.Width,
                        Height = extent.Height,
                        Layers = 1
                    };
                    if (vk.CreateFramebuffer(logicalDevice, ref _framebufferInfo, null, out frameBuffers[i]) != Result.Success)
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

        internal override void WriteCommandBuffers(ref Vk vk, ref Device logicalDevice, Extent2D extent, CommandBuffer[] commandBuffers, int index)
        {
            RenderPassBeginInfo _renderPassInfo = new RenderPassBeginInfo()
            {
                SType = StructureType.RenderPassBeginInfo,
                RenderPass = renderPass,
                Framebuffer = frameBuffers[index],
                RenderArea =
                    {
                        Offset = { X = 0, Y = 0 },
                        Extent = extent
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
            vk.CmdBindPipeline(commandBuffers[index], PipelineBindPoint.Graphics, pipeline);
            vk.CmdBeginRenderPass(commandBuffers[index], &_renderPassInfo, SubpassContents.Inline);

            IReadOnlyList<Entity> entities = EntityManager.controls;
            for (int e = 0; e < entities.Count; e++)
            {
                var _offset = new ulong[] { 0 };
                entities[e].GetComponent<MCUI>().EnqueueDrawCommands(ref _offset, index, e, ref commandBuffers[index], ref pipelineLayout, ref descriptorSets[index]);
            }
            vk.CmdEndRenderPass(commandBuffers[index]);
        }
    }
}