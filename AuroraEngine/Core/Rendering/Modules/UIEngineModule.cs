using ArctisAurora.Core.Data;
using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.UI;
using ArctisAurora.EngineWork.Registry;
using ArctisAurora.EngineWork.Rendering.Helpers;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using System.Runtime.CompilerServices;
using ImageLayout = Silk.NET.Vulkan.ImageLayout;
using VulkanControl = ArctisAurora.Core.UI.VulkanControl;

namespace ArctisAurora.EngineWork.Rendering.Modules
{
    // Draws the VulkanControls pool. Two mirrors, one per column, so an arrange and a paint change
    // upload independently.
    public unsafe class UIEngineModule : RenderingModule
    {
        internal override ERendererTypes rendererType => ERendererTypes.UIEngine;

        internal override ERendererStage RendererStage => ERendererStage.UI;

        internal override uint[][] descriptorMaxCounts => new uint[][] {
            new uint[] { 1, 1, 1 }   // set 1: camera UBO, geometry SSBO, control SSBO
        };

        internal override PhysicalDeviceFeatures features => new();

        internal override PhysicalDeviceVulkan12Features features12 => new()
        {
            SType = StructureType.PhysicalDeviceVulkan12Features,
            ScalarBlockLayout = true
        };

        internal override List<List<DescriptorType>> descriptorTypes => new()
        {
            new List<DescriptorType> {
                DescriptorType.UniformBuffer, DescriptorType.StorageBuffer, DescriptorType.StorageBuffer
            }
        };
        internal override List<List<ShaderStageFlags>> shaderStages => new()
        {
            new List<ShaderStageFlags>{
                ShaderStageFlags.VertexBit, ShaderStageFlags.VertexBit, ShaderStageFlags.VertexBit
            }
        };
        internal override DescriptorBindingFlags[][] descriptorBindingFlags => [
            [
                DescriptorBindingFlags.None, DescriptorBindingFlags.None, DescriptorBindingFlags.None
            ]
        ];
        internal override int variableSetCount => 1;

        internal override IReadOnlyList<Entity> renderEntities { get; set; } = Array.Empty<Entity>();

        private AVulkanMesh _quad = null!;

        // Per-image mirrors of the two GPU columns, patched in place through the mapped pointer and
        // recreated only when the pool grows.
        private Silk.NET.Vulkan.Buffer[] _geometryBuffers = null!;
        private DeviceMemory[] _geometryMemories = null!;
        private nint[] _geometryMapped = null!;
        private Silk.NET.Vulkan.Buffer[] _controlBuffers = null!;
        private DeviceMemory[] _controlMemories = null!;
        private nint[] _controlMapped = null!;
        private int _mirrorCapacity = -1;

        private PoolCursor[] _cursors = null!;
        private int[] _frameBuiltCapacity = null!;

        private DataPool ControlPool => UIEngine.Controls;

        private WindowRoot _uiRoot;

        // The tree this module draws, and its slice of the shared draw pool in dense order. The
        // range is published by UIEngine.RefreshWindowRanges; assigning tears the outgoing tree down.
        public WindowRoot uiRoot
        {
            get => _uiRoot;
            set
            {
                _uiRoot?.Destroy();
                _uiRoot = value;
                UIEngine.InvalidateWindowRanges();
                value?.FitTo(window.os.windowSize);
            }
        }

        internal int firstInstance;
        internal int instanceCount;

        private PoolCursor Cursor(int frame)
        {
            _cursors ??= new PoolCursor[window.imageCount];
            return _cursors[frame] ??= new PoolCursor(ControlPool);
        }

        internal override bool HasPendingWork(int frame) => Cursor(frame).HasPending;

        // Composited over UIModule while both stacks run.
        public UIEngineModule()
        {
            compositorOrder = 10;
        }

        internal override void RebindImageCount(RenderWindow window)
        {
            base.RebindImageCount(window);

            frameResources = new FrameResources[window.imageCount];
            _frameBuiltCapacity = new int[window.imageCount];
            Array.Fill(_frameBuiltCapacity, -1);

            _cursors = null;
        }

        internal override void PrepareObjects()
        {
            Renderer.renderer.CreateCommandPool((uint)Renderer.queueAllocator.GetFamilyIndex(QueueFlags.GraphicsBit), out moduleCommandPool, CommandPoolCreateFlags.ResetCommandBufferBit);
            RegisterVulkanQueue(Renderer.queueAllocator, Renderer.vk, ref Renderer.logicalDevice);
            _quad = AssetRegistries.GetRegistryByValueType<string, AVulkanMesh>(typeof(AVulkanMesh))["uidefault"];
            PrepareCamera();
        }

        internal override void PrepareCamera()
        {
            camera = new AuroraCamera(this);
        }

        internal override void UpdateFrameData(int imageIndex)
        {
            camera.UpdateCameraMatrix(window.swapchainExtent, (uint)imageIndex);
        }

        internal override void UpdateModule(int currentFrame)
        {
            DataPool pool = ControlPool;
            PoolCursor cursor = Cursor(currentFrame);

            cursor.TryConsumeStructural();
            bool content = cursor.TryConsumeContent(out int dirtyMin, out int dirtyMax);

            MirrorPool(currentFrame, content ? dirtyMin : 0, content ? dirtyMax : -1);

            if (pool.Count > 0 && _frameBuiltCapacity[currentFrame] != _mirrorCapacity)
            {
                CreateDescriptorPool(currentFrame, 0);
                AllocateDescriptorSets(currentFrame);
                UpdateDescriptorSets(currentFrame, pool.Count);
                _frameBuiltCapacity[currentFrame] = _mirrorCapacity;
            }

            WriteCommandBuffers(currentFrame);
        }

        // Copies the dirty dense range of both columns into this image's mirrors. A capacity change
        // recreates them from the whole columns and ignores the range.
        private void MirrorPool(int currentFrame, int dirtyMin, int dirtyMax)
        {
            DataPool pool = ControlPool;
            int live = pool.Count;
            if (live == 0) return;

            ControlGeometry[] geometry = pool.Backing<ControlGeometry>();
            VulkanControl[] controls = pool.Backing<VulkanControl>();

            if (_mirrorCapacity != pool.Capacity)
            {
                Renderer.vk.DeviceWaitIdle(Renderer.logicalDevice);
                DestroyMirrors();

                int images = (int)window.imageCount;
                _geometryBuffers = new Silk.NET.Vulkan.Buffer[images];
                _geometryMemories = new DeviceMemory[images];
                _geometryMapped = new nint[images];
                _controlBuffers = new Silk.NET.Vulkan.Buffer[images];
                _controlMemories = new DeviceMemory[images];
                _controlMapped = new nint[images];

                ulong geometrySize = (ulong)(sizeof(ControlGeometry) * geometry.Length);
                ulong controlSize = (ulong)(sizeof(VulkanControl) * controls.Length);
                for (int i = 0; i < images; i++)
                {
                    AVulkanBufferHandler.CreateMappedBuffer(geometrySize, ref _geometryBuffers[i], ref _geometryMemories[i], out _geometryMapped[i], AVulkanBufferHandler.storageBufferFlags);
                    AVulkanBufferHandler.CreateMappedBuffer(controlSize, ref _controlBuffers[i], ref _controlMemories[i], out _controlMapped[i], AVulkanBufferHandler.storageBufferFlags);
                    AVulkanBufferHandler.WriteMappedRange(_geometryMapped[i], geometry, 0, geometry.Length);
                    AVulkanBufferHandler.WriteMappedRange(_controlMapped[i], controls, 0, controls.Length);
                }
                _mirrorCapacity = pool.Capacity;
                return;
            }

            if (dirtyMax >= live) dirtyMax = live - 1;
            if (dirtyMin < 0) dirtyMin = 0;
            if (dirtyMax < dirtyMin) return;

            int count = dirtyMax - dirtyMin + 1;
            AVulkanBufferHandler.WriteMappedRange(_geometryMapped[currentFrame], geometry, dirtyMin, count);
            AVulkanBufferHandler.WriteMappedRange(_controlMapped[currentFrame], controls, dirtyMin, count);
        }

        private void DestroyMirrors()
        {
            if (_geometryBuffers == null) return;

            for (int i = 0; i < _geometryBuffers.Length; i++)
            {
                Renderer.vk.UnmapMemory(Renderer.logicalDevice, _geometryMemories[i]);
                Renderer.vk.DestroyBuffer(Renderer.logicalDevice, _geometryBuffers[i], null);
                Renderer.vk.FreeMemory(Renderer.logicalDevice, _geometryMemories[i], null);

                Renderer.vk.UnmapMemory(Renderer.logicalDevice, _controlMemories[i]);
                Renderer.vk.DestroyBuffer(Renderer.logicalDevice, _controlBuffers[i], null);
                Renderer.vk.FreeMemory(Renderer.logicalDevice, _controlMemories[i], null);
            }
            _geometryBuffers = null!;
            _mirrorCapacity = -1;
        }

        internal override void DestroyGpuResources()
        {
            DestroyMirrors();
            base.DestroyGpuResources();
        }

        internal override void CreateDescriptorPoolSizes(uint swapchainImageCount)
        {
            descriptorPoolSizes =
            [
                new DescriptorPoolSize()
                {
                    Type = DescriptorType.UniformBuffer,
                    DescriptorCount = 1
                },
                new DescriptorPoolSize()
                {
                    Type = DescriptorType.StorageBuffer,
                    DescriptorCount = 2
                }
            ];
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
                DescriptorPoolCreateInfo createInfo = new DescriptorPoolCreateInfo()
                {
                    SType = StructureType.DescriptorPoolCreateInfo,
                    PoolSizeCount = (uint)descriptorPoolSizes.Length,
                    PPoolSizes = sizesPtr,
                    MaxSets = (uint)variableSetCount,
                    Flags = DescriptorPoolCreateFlags.None
                };
                if (Renderer.vk.CreateDescriptorPool(Renderer.logicalDevice, ref createInfo, null, out frameResources[currentFrame].pool) != Result.Success)
                    throw new Exception("Failed to create descriptor pool");
            }
        }

        internal override void UpdateDescriptorSets(int currentFrame, int entityCount)
        {
            if (_geometryBuffers == null) return;

            DescriptorBufferInfo cameraInfo = new DescriptorBufferInfo()
            {
                Buffer = camera._cameraBuffer[currentFrame],
                Offset = 0,
                Range = (ulong)Unsafe.SizeOf<UBO>()
            };
            DescriptorBufferInfo geometryInfo = new DescriptorBufferInfo()
            {
                Buffer = _geometryBuffers[currentFrame],
                Offset = 0,
                Range = (ulong)(Unsafe.SizeOf<ControlGeometry>() * _mirrorCapacity)
            };
            DescriptorBufferInfo controlInfo = new DescriptorBufferInfo()
            {
                Buffer = _controlBuffers[currentFrame],
                Offset = 0,
                Range = (ulong)(Unsafe.SizeOf<VulkanControl>() * _mirrorCapacity)
            };
            WriteDescriptorSet[] writes = new WriteDescriptorSet[]
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
                    PBufferInfo = &geometryInfo
                },
                new WriteDescriptorSet
                {
                    SType = StructureType.WriteDescriptorSet,
                    DstSet = frameResources[currentFrame].sets[0],
                    DstBinding = 2,
                    DescriptorCount = 1,
                    DstArrayElement = 0,
                    DescriptorType = DescriptorType.StorageBuffer,
                    PBufferInfo = &controlInfo
                }
            };
            fixed (WriteDescriptorSet* writesPtr = writes)
            {
                Renderer.vk!.UpdateDescriptorSets(Renderer.logicalDevice, (uint)writes.Length, writesPtr, 0, null);
            }
        }

        internal override void CreatePipeline()
        {
            byte[] vertexCode = ReadFile("../../../Shaders/" + "UIEngine/UIEngine.vert.spv");
            byte[] fragmentCode = ReadFile("../../../Shaders/" + "UIEngine/UIEngine.frag.spv");

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

            // set 0 is the renderer's global set; this module's own sets follow it
            DescriptorSetLayout[] setLayouts = [Renderer.globalSetLayout, .. descriptorSetLayouts];

            fixed (VertexInputAttributeDescription* attribDescPtr = attribDesc)
            fixed (DescriptorSetLayout* setLayoutsPtr = setLayouts)
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
                    Width = window.swapchainExtent.Width,
                    Height = window.swapchainExtent.Height,
                    MinDepth = 0,
                    MaxDepth = 1
                };
                Rect2D scissor = new Rect2D()
                {
                    Offset = { X = 0, Y = 0 },
                    Extent = window.swapchainExtent
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
                    SetLayoutCount = (uint)setLayouts.Length,
                    PushConstantRangeCount = 0,
                    PSetLayouts = setLayoutsPtr
                };

                if (Renderer.vk.CreatePipelineLayout(Renderer.logicalDevice, ref pipelineLayoutInfo, null, out pipelineLayout) != Result.Success)
                    throw new Exception("Failed to create pipeline layout");

                DynamicState* dynamicStatesPtr = stackalloc DynamicState[] { DynamicState.Viewport, DynamicState.Scissor };
                PipelineDynamicStateCreateInfo dynamicStateInfo = new PipelineDynamicStateCreateInfo()
                {
                    SType = StructureType.PipelineDynamicStateCreateInfo,
                    DynamicStateCount = 2,
                    PDynamicStates = dynamicStatesPtr
                };

                Format colorFormat = outputFormat;
                PipelineRenderingCreateInfo renderingCreateInfo = new PipelineRenderingCreateInfo()
                {
                    SType = StructureType.PipelineRenderingCreateInfo,
                    ColorAttachmentCount = 1,
                    PColorAttachmentFormats = &colorFormat,
                    DepthAttachmentFormat = Format.Undefined,
                    StencilAttachmentFormat = Format.Undefined
                };

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
                    PDynamicState = &dynamicStateInfo,
                    Layout = pipelineLayout,
                    RenderPass = default,
                    Subpass = 0,
                    BasePipelineHandle = default,
                    PNext = &renderingCreateInfo
                };

                Result r = Renderer.vk.CreateGraphicsPipelines(Renderer.logicalDevice, default, 1, ref graphicsPipelineInfo, null, out pipeline);
                if (r != Result.Success)
                    throw new Exception("Failed to create graphics pipeline " + r);
            }

            Renderer.vk.DestroyShaderModule(Renderer.logicalDevice, vertexShader, null);
            Renderer.vk.DestroyShaderModule(Renderer.logicalDevice, fragmentShader, null);
            SilkMarshal.Free((nint)vertexShaderStageInfo.PName);
            SilkMarshal.Free((nint)fragmentShaderStageInfo.PName);
        }

        internal override void WriteCommandBuffers(int currentFrame)
        {
            if (commandBuffers == null)
            {
                commandBuffers = new CommandBuffer[window.imageCount];

                CommandBufferAllocateInfo allocInfo = new CommandBufferAllocateInfo()
                {
                    SType = StructureType.CommandBufferAllocateInfo,
                    CommandPool = moduleCommandPool,
                    Level = CommandBufferLevel.Primary,
                    CommandBufferCount = (uint)commandBuffers.Length
                };
                fixed (CommandBuffer* commandBufferPtr = commandBuffers)
                {
                    Result r = Renderer.vk.AllocateCommandBuffers(Renderer.logicalDevice, ref allocInfo, commandBufferPtr);
                    if (r != Result.Success)
                        throw new Exception("Failed to allocate command buffer with error " + r);
                }
            }
            else
            {
                Renderer.vk.ResetCommandBuffer(commandBuffers[currentFrame], CommandBufferResetFlags.None);
            }
            WriteCommandBuffer(currentFrame);
            isDirty[currentFrame] = false;
        }

        private void WriteCommandBuffer(int currentFrame)
        {
            CommandBufferBeginInfo beginInfo = new CommandBufferBeginInfo()
            {
                SType = StructureType.CommandBufferBeginInfo
            };

            if (Renderer.vk.BeginCommandBuffer(commandBuffers[currentFrame], ref beginInfo) != Result.Success)
                throw new Exception("Failed to create BEGIN command buffer at index " + currentFrame);

            ImageBarrier(commandBuffers[currentFrame], outputImages[currentFrame],
                ImageLayout.Undefined, ImageLayout.ColorAttachmentOptimal,
                PipelineStageFlags.TopOfPipeBit, PipelineStageFlags.ColorAttachmentOutputBit,
                AccessFlags.None, AccessFlags.ColorAttachmentWriteBit);

            // cleared transparent, so the compositor keeps whatever UIModule drew underneath
            RenderingAttachmentInfo colorAttachment = new RenderingAttachmentInfo()
            {
                SType = StructureType.RenderingAttachmentInfo,
                ImageView = outputImageViews[currentFrame],
                ImageLayout = ImageLayout.ColorAttachmentOptimal,
                LoadOp = AttachmentLoadOp.Clear,
                StoreOp = AttachmentStoreOp.Store,
                ClearValue = new ClearValue()
                {
                    Color = new ClearColorValue() { Float32_0 = 0f, Float32_1 = 0f, Float32_2 = 0f, Float32_3 = 0f }
                }
            };

            RenderingInfo renderingInfo = new RenderingInfo()
            {
                SType = StructureType.RenderingInfo,
                RenderArea = new Rect2D() { Offset = { X = 0, Y = 0 }, Extent = window.swapchainExtent },
                LayerCount = 1,
                ColorAttachmentCount = 1,
                PColorAttachments = &colorAttachment
            };

            Renderer.vk.CmdBeginRendering(commandBuffers[currentFrame], &renderingInfo);

            if (instanceCount > 0 && frameResources[currentFrame] != null && frameResources[currentFrame].sets != null)
            {
                Renderer.vk.CmdBindPipeline(commandBuffers[currentFrame], PipelineBindPoint.Graphics, pipeline);
                Renderer.vk.CmdBindDescriptorSets(commandBuffers[currentFrame], PipelineBindPoint.Graphics, pipelineLayout, 0, 1, Renderer.globalSets[currentFrame], 0, null);

                Viewport viewport = new Viewport() { X = 0, Y = 0, Width = window.swapchainExtent.Width, Height = window.swapchainExtent.Height, MinDepth = 0, MaxDepth = 1 };
                Rect2D scissor = new Rect2D() { Offset = { X = 0, Y = 0 }, Extent = window.swapchainExtent };
                Renderer.vk.CmdSetViewport(commandBuffers[currentFrame], 0, 1, &viewport);
                Renderer.vk.CmdSetScissor(commandBuffers[currentFrame], 0, 1, &scissor);

                ulong[] offsets = new ulong[] { 0 };
                fixed (ulong* offsetsPtr = offsets)
                {
                    Renderer.vk.CmdBindVertexBuffers(commandBuffers[currentFrame], 0, 1, ref _quad.vertexBuffer, offsetsPtr);
                }
                Renderer.vk.CmdBindIndexBuffer(commandBuffers[currentFrame], _quad.indexBuffer, 0, IndexType.Uint32);
                Renderer.vk.CmdBindDescriptorSets(commandBuffers[currentFrame], PipelineBindPoint.Graphics, pipelineLayout, 1, 1, frameResources[currentFrame].sets[0], 0, null);
                Renderer.vk.CmdDrawIndexed(commandBuffers[currentFrame], (uint)_quad.indices.Length, (uint)instanceCount, 0, 0, (uint)firstInstance);
            }

            Renderer.vk.CmdEndRendering(commandBuffers[currentFrame]);

            ImageBarrier(commandBuffers[currentFrame], outputImages[currentFrame],
                ImageLayout.ColorAttachmentOptimal, ImageLayout.ShaderReadOnlyOptimal,
                PipelineStageFlags.ColorAttachmentOutputBit, PipelineStageFlags.FragmentShaderBit,
                AccessFlags.ColorAttachmentWriteBit, AccessFlags.ShaderReadBit);

            if (Renderer.vk.EndCommandBuffer(commandBuffers[currentFrame]) != Result.Success)
                throw new Exception("Failed to record command buffer");
        }
    }
}
