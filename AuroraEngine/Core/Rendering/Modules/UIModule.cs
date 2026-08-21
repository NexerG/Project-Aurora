using ArctisAurora.Core.Data;
using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Registry.Assets;
using ArctisAurora.Core.UISystem;
using ArctisAurora.Core.UISystem.Controls;
using ArctisAurora.EngineWork.Registry;
using ArctisAurora.EngineWork.Rendering.Helpers;
using ArctisAurora.EngineWork.Rendering.MeshSubComponents;
using Silk.NET.Core.Native;
using Silk.NET.Maths;
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
            new uint[] { 1, 1, 1 },                    // set 0: camera UBO, transforms SSBO, control-data SSBO
            new uint[] { TextureAsset.MaxTextures }    // set 1: one sampler per distinct texture
        };

        internal override uint GetVariableDescriptorCount(int set)
        {
            return TextureAsset.MaxTextures;
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
            ScalarBlockLayout = true,
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
                DescriptorBindingFlags.None
            ],
            [
                DescriptorBindingFlags.VariableDescriptorCountBit | DescriptorBindingFlags.PartiallyBoundBit
            ]
        ];
        internal override int variableSetCount => 2;


        internal static MCUI meshComponent = null!;
        internal override IReadOnlyList<Entity> renderEntities { get => field; set { field = value; } }

        private WindowControl _uiRoot = null!;

        // The tree this module draws, and its slice of the UIControls pool in dense order. The pool
        // is shared by every window, so the range is published by UILayout.RefreshWindowRanges.
        public WindowControl uiRoot
        {
            get => _uiRoot;
            set
            {
                _uiRoot = value;
                UILayout.InvalidateWindowRanges();
                value?.FitTo(window.os.windowSize);
            }
        }
        internal int firstInstance;
        internal int instanceCount;

        // Set on a ghost window's module: the control whose subtree this draws instead of a tree of
        // its own. The camera frames it and the range is that subtree's, not a slice of the pool.
        internal VulkanControl? rangeRoot;

        // Window pixels to the units this module's tree is laid out in.
        public Vector2D<float> ToDesignSpace(Vector2D<float> windowPoint) =>
            _uiRoot == null ? windowPoint : _uiRoot.ToDesignSpace(windowPoint, window.os.windowSize);

        internal struct DeferredResources
        {
            internal Silk.NET.Vulkan.Buffer buffer;
            internal DeviceMemory memory;
            internal DescriptorPool pool;
        }
        internal List<DeferredResources>[] deferredDeletions = null!;

        // UIControls data pool — transforms live here; the renderer mirrors it to the GPU.
        private DataPool? _controlPool;
        internal DataPool ControlPool => _controlPool ??= DataManager.Get("UIControls");

        private int[] _frameBuiltCapacity = null!;
        private int[] _frameTableVersion = null!;

        // This image's position in the control pool's history. One per image, not per module: each
        // image provisions its own sets and advances independently.
        //
        // Lazy because pools are parsed in the same bootstrap phase that constructs this module and
        // intra-phase order is undefined, so the pool may not exist yet at construction.
        private PoolCursor[] _cursors = null!;

        private PoolCursor Cursor(int frame)
        {
            _cursors ??= new PoolCursor[window.imageCount];
            return _cursors[frame] ??= new PoolCursor(ControlPool);
        }

        internal override bool HasPendingWork(int frame) => Cursor(frame).HasPending;

        private int _buildCapacity = -1;
        private int BuildCapacity => _buildCapacity < 0 ? ControlPool.Capacity : _buildCapacity;


        // The module is constructed before its window has a swapchain, so everything indexed by
        // swapchain image is sized in RebindImageCount instead of here.
        public UIModule()
        {
        }

        internal override void RebindImageCount(RenderWindow window)
        {
            base.RebindImageCount(window);

            deferredDeletions = new List<DeferredResources>[window.imageCount];
            for (int i = 0; i < window.imageCount; i++)
                deferredDeletions[i] = new List<DeferredResources>();

            frameResources = new FrameResources[window.imageCount];
            _frameBuiltCapacity = new int[window.imageCount];
            _frameTableVersion = new int[window.imageCount];
            Array.Fill(_frameBuiltCapacity, -1);
            Array.Fill(_frameTableVersion, -1);

            _cursors = null;
        }

        // The static meshComponent is deliberately untouched — its sampler and both SSBO mirrors are
        // shared by every window and outlive any one of them.
        internal override void DestroyGpuResources()
        {
            if (deferredDeletions != null)
            {
                for (int i = 0; i < deferredDeletions.Length; i++)
                {
                    foreach (DeferredResources d in deferredDeletions[i])
                    {
                        if (d.buffer.Handle != 0)
                        {
                            Renderer.vk.DestroyBuffer(Renderer.logicalDevice, d.buffer, null);
                            Renderer.vk.FreeMemory(Renderer.logicalDevice, d.memory, null);
                        }
                        if (d.pool.Handle != 0)
                            Renderer.vk.DestroyDescriptorPool(Renderer.logicalDevice, d.pool, null);
                    }
                    deferredDeletions[i].Clear();
                }
            }

            base.DestroyGpuResources();
        }

        internal override void UpdateModule(int currentFrame)
        {
            foreach (var d in deferredDeletions[currentFrame])
            {
                if (d.buffer.Handle != 0)
                {
                    Renderer.vk.DestroyBuffer(Renderer.logicalDevice, d.buffer, null);
                    Renderer.vk.FreeMemory(Renderer.logicalDevice, d.memory, null);
                }
                if (d.pool.Handle != 0)
                    Renderer.vk.DestroyDescriptorPool(Renderer.logicalDevice, d.pool, null);
            }
            deferredDeletions[currentFrame].Clear();

            DataPool pool = ControlPool;
            PoolCursor cursor = Cursor(currentFrame);

            // Ask the pool what moved since this image last looked. Both are consumed even if only
            // one is acted on — an unconsumed generation looks pending forever.
            cursor.TryConsumeStructural();
            bool content = cursor.TryConsumeContent(out int dirtyMin, out int dirtyMax);

            int live = pool.Count;
            _buildCapacity = pool.Capacity;

            // Copy across only the rows that actually changed. A capacity change is handled inside:
            // the buffer is recreated from the whole column and the range is ignored.
            meshComponent.MakeInstanced(this, currentFrame, content ? dirtyMin : 0, content ? dirtyMax : -1);

            // Nothing to bind yet — record an empty (clear-only) command buffer and wait.
            if (live > 0)
            {
                if (_frameBuiltCapacity[currentFrame] != _buildCapacity)
                {
                    CreateDescriptorPool(currentFrame, 0);
                    AllocateDescriptorSets(currentFrame);
                    UpdateDescriptorSets(currentFrame, live);
                    _frameBuiltCapacity[currentFrame] = _buildCapacity;
                    _frameTableVersion[currentFrame] = TextureAsset.TableVersion;
                }
                else if (_frameTableVersion[currentFrame] != TextureAsset.TableVersion)
                {
                    WriteTextureTable(currentFrame);
                    _frameTableVersion[currentFrame] = TextureAsset.TableVersion;
                }
            }

            WriteCommandBuffers(currentFrame);
        }

        internal override void PrepareObjects()
        {
            Renderer.renderer.CreateCommandPool((uint)Renderer.queueAllocator.GetFamilyIndex(QueueFlags.GraphicsBit), out moduleCommandPool, CommandPoolCreateFlags.ResetCommandBufferBit);
            RegisterVulkanQueue(Renderer.queueAllocator, Renderer.vk, ref Renderer.logicalDevice);
            meshComponent ??= new MCUI();
            PrepareCamera();

            EntityGroup controls = EntityRegistry.GetGroup("Controls");
            controls.onChanged += OnControlsChanged;
            renderEntities = controls.As<VulkanControl>();
        }

        private void OnControlsChanged()
        {
            for (int i = 0; i < isDirty.Length; i++)
                isDirty[i] = true;
        }

        internal override void CreateDescriptorPoolSizes(uint swapchainImageCount)
        {
            // one pool per image, holding the two sets sized to the full pool capacity:
            // camera UBO ×1, transforms + control-data SSBOs ×2, sampler array ×cap
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
                },
                new DescriptorPoolSize()
                {
                    Type = DescriptorType.CombinedImageSampler,
                    DescriptorCount = TextureAsset.MaxTextures
                }
            ];
        }

        internal override void CreateDescriptorPool(int currentFrame, int entityCount)
        {
            if (frameResources[currentFrame] == null)
                frameResources[currentFrame] = new FrameResources();

            if (frameResources[currentFrame].pool.Handle != default)
            {
                //Renderer.vk.DestroyDescriptorPool(Renderer.logicalDevice, frameResources[currentFrame].pool, null);
                deferredDeletions[currentFrame].Add(new DeferredResources { pool = frameResources[currentFrame].pool });
            }

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


        // Full (re)write of a frame's sets — used on the structural rebuild path only.
        internal override void UpdateDescriptorSets(int currentFrame, int entityCount)
        {
            WriteStaticDescriptors(currentFrame);
            WriteTextureTable(currentFrame);
        }

        // The per-frame-constant bindings: camera UBO (set0/b0), the transforms SSBO (set0/b1) and
        // the control-data SSBO (set0/b2). All three are single descriptors pointing at whole-pool
        // mirrors, so they only need rewriting when the set is rebuilt (i.e. those buffers moved).
        private void WriteStaticDescriptors(int currentFrame)
        {
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
                Range = (ulong)(sizeof(float) * 16 * BuildCapacity)
            };
            DescriptorBufferInfo controlDataInfo = new DescriptorBufferInfo()
            {
                Buffer = meshComponent.controlDataBuffer,
                Offset = 0,
                Range = (ulong)(Unsafe.SizeOf<ControlData>() * BuildCapacity)
            };
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
                    DescriptorCount = 1,
                    DstArrayElement = 0,
                    DescriptorType = DescriptorType.StorageBuffer,
                    PBufferInfo = &controlDataInfo
                }
            };
            fixed (WriteDescriptorSet* descPtr = writeDescriptorSets)
            {
                Renderer.vk!.UpdateDescriptorSets(Renderer.logicalDevice, (uint)writeDescriptorSets.Length, descPtr, 0, null);
            }
        }

        // Write the texture table (set1/b0), indexed by TextureAsset.textureIndex.
        private void WriteTextureTable(int currentFrame)
        {
            IReadOnlyList<TextureAsset> table = TextureAsset.Table;
            if (table.Count == 0) return;

            Sampler sampler = AssetRegistries
                .GetRegistryByValueType<string, SamplerAsset>(typeof(SamplerAsset))["ControlSampler"].handle;

            DescriptorImageInfo[] samplersInfos = new DescriptorImageInfo[table.Count];
            for (int k = 0; k < table.Count; k++)
            {
                samplersInfos[k] = new()
                {
                    ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
                    ImageView = table[k].textureImageView,
                    Sampler = sampler
                };
            }
            fixed (DescriptorImageInfo* samplerInfosPtr = samplersInfos)
            {
                WriteDescriptorSet write = new WriteDescriptorSet
                {
                    SType = StructureType.WriteDescriptorSet,
                    DstSet = frameResources[currentFrame].sets[1],
                    DstBinding = 0,
                    DstArrayElement = 0,
                    DescriptorCount = (uint)table.Count,
                    DescriptorType = DescriptorType.CombinedImageSampler,
                    PImageInfo = samplerInfosPtr
                };
                Renderer.vk!.UpdateDescriptorSets(Renderer.logicalDevice, 1, &write, 0, null);
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
                    SetLayoutCount = (uint)variableSetCount,
                    PushConstantRangeCount = 0,
                    PSetLayouts = descriptorSetLayoutsPtr
                };

                if (Renderer.vk.CreatePipelineLayout(Renderer.logicalDevice, ref pipelineLayoutInfo, null, out pipelineLayout) != Result.Success)
                {
                    throw new Exception("Failed to create pipeline layout");
                }

                // viewport + scissor are dynamic so the pipeline survives window resize
                DynamicState* dynamicStatesPtr = stackalloc DynamicState[] { DynamicState.Viewport, DynamicState.Scissor };
                PipelineDynamicStateCreateInfo dynamicStateInfo = new PipelineDynamicStateCreateInfo()
                {
                    SType = StructureType.PipelineDynamicStateCreateInfo,
                    DynamicStateCount = 2,
                    PDynamicStates = dynamicStatesPtr
                };

                // Replaces the render pass handle: the pipeline is now compatible with any rendering
                // instance whose attachment formats match these, rather than with one VkRenderPass object.
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
                {
                    throw new Exception("Failed to create graphics pipeline " + r);
                }
            }

            Renderer.vk.DestroyShaderModule(Renderer.logicalDevice, vertexShader, null);
            Renderer.vk.DestroyShaderModule(Renderer.logicalDevice, fragmentShader, null);
            SilkMarshal.Free((nint)vertexShaderStageInfo.PName);
            SilkMarshal.Free((nint)fragmentShaderStageInfo.PName);
        }

        internal override void PrepareCamera()
        {
            camera = new AuroraCamera(this);
        }

        internal override void WriteCommandBuffers(int currentFrame)
        {
            if (commandBuffers == null)
            {
                commandBuffers = new CommandBuffer[window.imageCount];

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
            }
            else
            {
                Renderer.vk.ResetCommandBuffer(commandBuffers[currentFrame], CommandBufferResetFlags.None);
            }
            // Record only this image; the other images each record on their own first dirty pass
            // (all start dirty), by which point their descriptor sets are built.
            WriteCommandBuffer(currentFrame);
            isDirty[currentFrame] = false;
        }

        private void WriteCommandBuffer(int currentFrame)
        {
            CommandBufferBeginInfo _beginInfo = new CommandBufferBeginInfo()
            {
                SType = StructureType.CommandBufferBeginInfo
            };

            if (Renderer.vk.BeginCommandBuffer(commandBuffers[currentFrame], ref _beginInfo) != Result.Success)
            {
                throw new Exception("Failed to create BEGIN command buffer at index " + currentFrame);
            }

            // Was the render pass's InitialLayout=Undefined plus its EXTERNAL->0 dependency. Undefined
            // discards the previous contents, which is what we want — the attachment is cleared on load.
            ImageBarrier(commandBuffers[currentFrame], outputImages[currentFrame],
                ImageLayout.Undefined, ImageLayout.ColorAttachmentOptimal,
                PipelineStageFlags.TopOfPipeBit, PipelineStageFlags.ColorAttachmentOutputBit,
                AccessFlags.None, AccessFlags.ColorAttachmentWriteBit);

            RenderingAttachmentInfo _colorAttachment = new RenderingAttachmentInfo()
            {
                SType = StructureType.RenderingAttachmentInfo,
                ImageView = outputImageViews[currentFrame],
                ImageLayout = ImageLayout.ColorAttachmentOptimal,
                LoadOp = AttachmentLoadOp.Clear,
                StoreOp = AttachmentStoreOp.Store,
                ClearValue = new ClearValue()
                {
                    Color = new ClearColorValue() { Float32_0 = 0.05f, Float32_1 = 0.05f, Float32_2 = 0.05f, Float32_3 = 1f }
                }
            };

            RenderingInfo _renderingInfo = new RenderingInfo()
            {
                SType = StructureType.RenderingInfo,
                RenderArea = new Rect2D() { Offset = { X = 0, Y = 0 }, Extent = window.swapchainExtent },
                LayerCount = 1,
                ColorAttachmentCount = 1,
                PColorAttachments = &_colorAttachment
            };

            //player view
            Renderer.vk.CmdBeginRendering(commandBuffers[currentFrame], &_renderingInfo);
            Renderer.vk.CmdBindPipeline(commandBuffers[currentFrame], PipelineBindPoint.Graphics, pipeline);

            Viewport _viewport = new Viewport() { X = 0, Y = 0, Width = window.swapchainExtent.Width, Height = window.swapchainExtent.Height, MinDepth = 0, MaxDepth = 1 };
            Rect2D _scissor = new Rect2D() { Offset = { X = 0, Y = 0 }, Extent = window.swapchainExtent };
            Renderer.vk.CmdSetViewport(commandBuffers[currentFrame], 0, 1, &_viewport);
            Renderer.vk.CmdSetScissor(commandBuffers[currentFrame], 0, 1, &_scissor);

            //IReadOnlyList<Entity> entities = EntityManager.controls;
            var _offset = new ulong[] { 0 };
            if (meshComponent.render == true)
            {
                DescriptorSet[] frameSets = frameResources[currentFrame].sets;
                meshComponent.EnqueueDrawCommands(ref _offset, currentFrame, firstInstance, instanceCount, ref commandBuffers[currentFrame], ref pipelineLayout, ref frameSets);
            }
            Renderer.vk.CmdEndRendering(commandBuffers[currentFrame]);

            // Was the render pass's FinalLayout=ShaderReadOnlyOptimal. The compositor samples this
            // image, so the transition and the write->read visibility both have to happen here.
            ImageBarrier(commandBuffers[currentFrame], outputImages[currentFrame],
                ImageLayout.ColorAttachmentOptimal, ImageLayout.ShaderReadOnlyOptimal,
                PipelineStageFlags.ColorAttachmentOutputBit, PipelineStageFlags.FragmentShaderBit,
                AccessFlags.ColorAttachmentWriteBit, AccessFlags.ShaderReadBit);

            if (Renderer.vk.EndCommandBuffer(commandBuffers[currentFrame]) != Result.Success)
            {
                throw new Exception("Failed to record command buffer");
            }
        }
    }
}