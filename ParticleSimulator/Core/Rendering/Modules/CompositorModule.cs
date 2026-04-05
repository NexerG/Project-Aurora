using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.EngineWork;
using ArctisAurora.EngineWork.Rendering;
using ArctisAurora.EngineWork.Rendering.Modules;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using ImageLayout = Silk.NET.Vulkan.ImageLayout;

namespace ArctisAurora.Core.Rendering.Modules
{
    public unsafe class CompositorModule : RenderingModule
    {
        internal override ERendererTypes rendererType => ERendererTypes.UITemp;
        internal override ERendererStage RendererStage => ERendererStage.PostProcessing;
        internal override uint[][] descriptorMaxCounts => new uint[][] {
            new uint[] { 16 }    // set 0: sampler array, max 16 module outputs
        };
        internal override uint GetVariableDescriptorCount(int set)
        {
            return (uint)_moduleCount;
        }
        internal override PhysicalDeviceFeatures features => new();
        internal override PhysicalDeviceVulkan12Features features12 => new()
        {
            SType = StructureType.PhysicalDeviceVulkan12Features
        };

        internal override List<List<DescriptorType>> descriptorTypes => new List<List<DescriptorType>>
        {
            //new List<DescriptorType> { },
            new List<DescriptorType> { DescriptorType.CombinedImageSampler }
        };
        internal override List<List<ShaderStageFlags>> shaderStages => new List<List<ShaderStageFlags>>
        {
            //new List<ShaderStageFlags> { ShaderStageFlags.VertexBit },
            new List<ShaderStageFlags> { ShaderStageFlags.FragmentBit }
        };
        internal override DescriptorBindingFlags[][] descriptorBindingFlags => new DescriptorBindingFlags[][]
        {
            //new DescriptorBindingFlags[] { },
            new DescriptorBindingFlags[] { DescriptorBindingFlags.VariableDescriptorCountBit | DescriptorBindingFlags.PartiallyBoundBit }
        };
        internal override int variableSetCount => 1;

        private RenderingModule[] _sourceModules;
        private Sampler _sampler;
        private int _moduleCount;

        internal override IReadOnlyList<Entity> renderEntities { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public void Init(RenderingModule[] modules, ImageView[] swapchainImageViews)
        {
            _sourceModules = modules;
            _moduleCount = modules.Length;

            // sort by compositorOrder so blend stack is deterministic
            Array.Sort(_sourceModules, (a, b) => a.compositorOrder.CompareTo(b.compositorOrder));

            CreateSampler();
            CreateDescriptorSetLayout();
            frameResources = new FrameResources[Renderer.swapchainImageCount];
            for (int i = 0; i < Renderer.swapchainImageCount; i++)
            {
                CreateDescriptorPool(i, 0);
                AllocateDescriptorSets(i);
                UpdateDescriptorSets(i, 0);
            }
            CreatePipeline();
        }

        private void CreateSampler()
        {
            SamplerCreateInfo info = new SamplerCreateInfo()
            {
                SType = StructureType.SamplerCreateInfo,
                MagFilter = Filter.Linear,
                MinFilter = Filter.Linear,
                AddressModeU = SamplerAddressMode.ClampToEdge,
                AddressModeV = SamplerAddressMode.ClampToEdge,
                AddressModeW = SamplerAddressMode.ClampToEdge,
                AnisotropyEnable = false,
                BorderColor = BorderColor.IntOpaqueBlack,
                UnnormalizedCoordinates = false,
                CompareEnable = false,
                MipmapMode = SamplerMipmapMode.Linear
            };
            fixed (Sampler* ptr = &_sampler)
            {
                if (Renderer.vk.CreateSampler(Renderer.logicalDevice, ref info, null, ptr) != Result.Success)
                    throw new Exception("Failed to create compositor sampler");
            }
        }

        internal override void CreateDescriptorPool(int currentFrame, int entityCount)
        {
            if (frameResources[currentFrame] == null)
                frameResources[currentFrame] = new FrameResources();

            if (frameResources[currentFrame].pool.Handle != default)
                Renderer.vk.DestroyDescriptorPool(Renderer.logicalDevice, frameResources[currentFrame].pool, null);

            CreateDescriptorPoolSizes(1);
            fixed (DescriptorPoolSize* sizesPtr = descriptorPoolSizes)
            {
                DescriptorPoolCreateInfo info = new DescriptorPoolCreateInfo()
                {
                    SType = StructureType.DescriptorPoolCreateInfo,
                    PoolSizeCount = (uint)descriptorPoolSizes.Length,
                    PPoolSizes = sizesPtr,
                    MaxSets = (uint)variableSetCount,
                    Flags = DescriptorPoolCreateFlags.None
                };
                if (Renderer.vk.CreateDescriptorPool(Renderer.logicalDevice, ref info, null, out frameResources[currentFrame].pool) != Result.Success)
                    throw new Exception("Failed to create compositor descriptor pool");
            }
        }

        internal override void CreateDescriptorPoolSizes(uint swapchainImageCount)
        {
            descriptorPoolSizes = new DescriptorPoolSize[]
            {
                new DescriptorPoolSize()
                {
                    Type = DescriptorType.CombinedImageSampler,
                    DescriptorCount = (uint)(swapchainImageCount * _moduleCount) + 1
                }
            };
        }

        internal override void CreateModuleFrameBuffers()
        {
            frameBuffers = new Framebuffer[Renderer.swapchainImageCount];
            for (int i = 0; i < Renderer.swapchainImageCount; i++)
            {
                fixed (ImageView* attachPtr = new[] { Renderer.renderer.swapchainImageViews[i] })
                {
                    FramebufferCreateInfo info = new FramebufferCreateInfo()
                    {
                        SType = StructureType.FramebufferCreateInfo,
                        RenderPass = renderPass,
                        AttachmentCount = 1,
                        PAttachments = attachPtr,
                        Width = Engine.window.windowSize.Width,
                        Height = Engine.window.windowSize.Height,
                        Layers = 1
                    };
                    if (Renderer.vk.CreateFramebuffer(Renderer.logicalDevice, ref info, null, out frameBuffers[i]) != Result.Success)
                        throw new Exception("Failed to create compositor framebuffer");
                }
            }
        }

        internal override void CreatePipeline()
        {
            byte[] vertCode = ReadFile("../../../Shaders/Modules/Compositor/compositor.vert.spv");
            byte[] fragCode = ReadFile("../../../Shaders/Modules/Compositor/compositor.frag.spv");

            ShaderModule vertShader = CreateShaderModule(ref Renderer.vk, ref Renderer.logicalDevice, vertCode);
            ShaderModule fragShader = CreateShaderModule(ref Renderer.vk, ref Renderer.logicalDevice, fragCode);

            PipelineShaderStageCreateInfo vertStage = new PipelineShaderStageCreateInfo()
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.VertexBit,
                Module = vertShader,
                PName = (byte*)SilkMarshal.StringToPtr("main")
            };
            PipelineShaderStageCreateInfo fragStage = new PipelineShaderStageCreateInfo()
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.FragmentBit,
                Module = fragShader,
                PName = (byte*)SilkMarshal.StringToPtr("main")
            };

            var stages = stackalloc[] { vertStage, fragStage };

            // no vertex input — fullscreen triangle generated in vertex shader
            PipelineVertexInputStateCreateInfo vertexInput = new PipelineVertexInputStateCreateInfo()
            {
                SType = StructureType.PipelineVertexInputStateCreateInfo,
                VertexBindingDescriptionCount = 0,
                VertexAttributeDescriptionCount = 0
            };
            PipelineInputAssemblyStateCreateInfo inputAssembly = new PipelineInputAssemblyStateCreateInfo()
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
                PScissors = &scissor
            };
            PipelineRasterizationStateCreateInfo rasterizer = new PipelineRasterizationStateCreateInfo()
            {
                SType = StructureType.PipelineRasterizationStateCreateInfo,
                PolygonMode = PolygonMode.Fill,
                CullMode = CullModeFlags.None,
                FrontFace = FrontFace.Clockwise,
                LineWidth = 1
            };
            PipelineMultisampleStateCreateInfo multisampling = new PipelineMultisampleStateCreateInfo()
            {
                SType = StructureType.PipelineMultisampleStateCreateInfo,
                RasterizationSamples = SampleCountFlags.Count1Bit
            };
            PipelineColorBlendAttachmentState blendAttachment = new PipelineColorBlendAttachmentState()
            {
                ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit | ColorComponentFlags.BBit | ColorComponentFlags.ABit,
                BlendEnable = false   // compositor does blending manually in the shader
            };
            PipelineColorBlendStateCreateInfo colorBlend = new PipelineColorBlendStateCreateInfo()
            {
                SType = StructureType.PipelineColorBlendStateCreateInfo,
                LogicOpEnable = false,
                AttachmentCount = 1,
                PAttachments = &blendAttachment
            };
            PipelineDepthStencilStateCreateInfo depthStencil = new PipelineDepthStencilStateCreateInfo()
            {
                SType = StructureType.PipelineDepthStencilStateCreateInfo,
                DepthTestEnable = false,
                DepthWriteEnable = false
            };

            fixed (DescriptorSetLayout* layoutsPtr = descriptorSetLayouts)
            {
                PipelineLayoutCreateInfo layoutInfo = new PipelineLayoutCreateInfo()
                {
                    SType = StructureType.PipelineLayoutCreateInfo,
                    SetLayoutCount = 1,
                    PSetLayouts = layoutsPtr
                };
                if (Renderer.vk.CreatePipelineLayout(Renderer.logicalDevice, ref layoutInfo, null, out pipelineLayout) != Result.Success)
                    throw new Exception("Failed to create compositor pipeline layout");

                int moduleCount = _moduleCount;
                SpecializationMapEntry specEntry = new SpecializationMapEntry()
                {
                    ConstantID = 0,
                    Offset = 0,
                    Size = (nuint)sizeof(int)
                };
                SpecializationInfo specInfo = new SpecializationInfo()
                {
                    MapEntryCount = 1,
                    PMapEntries = &specEntry,
                    DataSize = (nuint)sizeof(int),
                    PData = &moduleCount
                };
                // then on fragStage:
                fragStage.PSpecializationInfo = &specInfo;

                GraphicsPipelineCreateInfo pipelineInfo = new GraphicsPipelineCreateInfo()
                {
                    SType = StructureType.GraphicsPipelineCreateInfo,
                    StageCount = 2,
                    PStages = stages,
                    PVertexInputState = &vertexInput,
                    PInputAssemblyState = &inputAssembly,
                    PViewportState = &viewportState,
                    PRasterizationState = &rasterizer,
                    PMultisampleState = &multisampling,
                    PDepthStencilState = &depthStencil,
                    PColorBlendState = &colorBlend,
                    Layout = pipelineLayout,
                    RenderPass = renderPass,
                    Subpass = 0
                };
                if (Renderer.vk.CreateGraphicsPipelines(Renderer.logicalDevice, default, 1, ref pipelineInfo, null, out pipeline) != Result.Success)
                    throw new Exception("Failed to create compositor pipeline");
            }

            Renderer.vk.DestroyShaderModule(Renderer.logicalDevice, vertShader, null);
            Renderer.vk.DestroyShaderModule(Renderer.logicalDevice, fragShader, null);
            SilkMarshal.Free((nint)vertStage.PName);
            SilkMarshal.Free((nint)fragStage.PName);
        }
        
        internal override void CreateRenderPass()
        {
            AttachmentDescription color = new AttachmentDescription()
            {
                Format = Renderer.renderer.surfaceFormat.Format,
                Samples = SampleCountFlags.Count1Bit,
                LoadOp = AttachmentLoadOp.Clear,
                StoreOp = AttachmentStoreOp.Store,
                InitialLayout = ImageLayout.Undefined,
                FinalLayout = ImageLayout.PresentSrcKhr
            };
            AttachmentReference colorRef = new AttachmentReference()
            {
                Attachment = 0,
                Layout = ImageLayout.ColorAttachmentOptimal
            };
            SubpassDescription subpass = new SubpassDescription()
            {
                PipelineBindPoint = PipelineBindPoint.Graphics,
                ColorAttachmentCount = 1,
                PColorAttachments = &colorRef
            };
            SubpassDependency dep = new SubpassDependency()
            {
                SrcSubpass = Vk.SubpassExternal,
                DstSubpass = 0,
                SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
                SrcAccessMask = 0,
                DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
                DstAccessMask = AccessFlags.ColorAttachmentWriteBit
            };
            fixed (AttachmentDescription* attachPtr = new[] { color })
            {
                RenderPassCreateInfo info = new RenderPassCreateInfo()
                {
                    SType = StructureType.RenderPassCreateInfo,
                    AttachmentCount = 1,
                    PAttachments = attachPtr,
                    SubpassCount = 1,
                    PSubpasses = &subpass,
                    DependencyCount = 1,
                    PDependencies = &dep
                };
                if (Renderer.vk.CreateRenderPass(Renderer.logicalDevice, ref info, null, out renderPass) != Result.Success)
                    throw new Exception("Failed to create compositor render pass");
            }
        }

        internal override void PrepareCamera()
        {}

        internal override void PrepareObjects()
        {}

        internal override void CreateOutputImages()
        {}

        internal override void UpdateDescriptorSets(int currentFrame, int entityCount)
        {
            DescriptorImageInfo[] imageInfos = new DescriptorImageInfo[_moduleCount];
            for (int m = 0; m < _moduleCount; m++)
            {
                imageInfos[m] = new DescriptorImageInfo()
                {
                    ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
                    ImageView = _sourceModules[m].outputImageViews[currentFrame],
                    Sampler = _sampler
                };
            }
            fixed (DescriptorImageInfo* imageInfosPtr = imageInfos)
            {
                WriteDescriptorSet write = new WriteDescriptorSet()
                {
                    SType = StructureType.WriteDescriptorSet,
                    DstSet = frameResources[currentFrame].sets[0],
                    DstBinding = 0,
                    DstArrayElement = 0,
                    DescriptorCount = (uint)_moduleCount,
                    DescriptorType = DescriptorType.CombinedImageSampler,
                    PImageInfo = imageInfosPtr
                };
                Renderer.vk.UpdateDescriptorSets(Renderer.logicalDevice, 1, ref write, 0, null);
            }
        }

        internal override void UpdateModule(int currentFrame)
        {
            UpdateDescriptorSets(currentFrame, 0);
            WriteCommandBuffers(currentFrame);
        }

        internal override void WriteCommandBuffers(int currentFrame)
        {
            if (commandBuffers == null)
            {
                commandBuffers = new CommandBuffer[Renderer.swapchainImageCount];
                CommandBufferAllocateInfo allocInfo = new CommandBufferAllocateInfo()
                {
                    SType = StructureType.CommandBufferAllocateInfo,
                    CommandPool = Renderer.compositeCommandPool,
                    Level = CommandBufferLevel.Primary,
                    CommandBufferCount = (uint)commandBuffers.Length
                };
                fixed (CommandBuffer* ptr = commandBuffers)
                {
                    if (Renderer.vk.AllocateCommandBuffers(Renderer.logicalDevice, ref allocInfo, ptr) != Result.Success)
                        throw new Exception("Failed to allocate compositor command buffers");
                }
                for (int i = 0; i < commandBuffers.Length; i++)
                    WriteCommandBuffer(i);
            }
            else
            {
                Renderer.vk.ResetCommandBuffer(commandBuffers[currentFrame], CommandBufferResetFlags.None);
                WriteCommandBuffer(currentFrame);
            }
            isDirty[currentFrame] = false;
        }

        private void WriteCommandBuffer(int index)
        {
            CommandBufferBeginInfo beginInfo = new CommandBufferBeginInfo()
            {
                SType = StructureType.CommandBufferBeginInfo
            };
            if (Renderer.vk.BeginCommandBuffer(commandBuffers[index], ref beginInfo) != Result.Success)
                throw new Exception("Failed to begin compositor command buffer");

            ClearValue clearValue = new ClearValue()
            {
                Color = new ClearColorValue() { Float32_0 = 0f, Float32_1 = 0f, Float32_2 = 0f, Float32_3 = 1f }
            };
            RenderPassBeginInfo renderPassInfo = new RenderPassBeginInfo()
            {
                SType = StructureType.RenderPassBeginInfo,
                RenderPass = renderPass,
                Framebuffer = frameBuffers[index],
                RenderArea = { Offset = { X = 0, Y = 0 }, Extent = Engine.window.windowSize },
                ClearValueCount = 1,
                PClearValues = &clearValue
            };

            Renderer.vk.CmdBeginRenderPass(commandBuffers[index], &renderPassInfo, SubpassContents.Inline);
            Renderer.vk.CmdBindPipeline(commandBuffers[index], PipelineBindPoint.Graphics, pipeline);

            DescriptorSet set = frameResources[index].sets[0];
            Renderer.vk.CmdBindDescriptorSets(commandBuffers[index], PipelineBindPoint.Graphics,
                pipelineLayout, 0, 1, &set, 0, null);

            // fullscreen triangle — 3 vertices, no vertex buffer
            Renderer.vk.CmdDraw(commandBuffers[index], 3, 1, 0, 0);
            Renderer.vk.CmdEndRenderPass(commandBuffers[index]);

            if (Renderer.vk.EndCommandBuffer(commandBuffers[index]) != Result.Success)
                throw new Exception("Failed to record compositor command buffer");
        }
    }
}
