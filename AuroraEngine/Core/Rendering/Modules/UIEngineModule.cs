using ArctisAurora.Core.Data;
using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Registry.Assets;
using ArctisAurora.Core.UI;
using ArctisAurora.EngineWork.Registry;
using ArctisAurora.EngineWork.Rendering.Helpers;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using System.Numerics;
using System.Runtime.CompilerServices;
using ImageLayout = Silk.NET.Vulkan.ImageLayout;
using VulkanControl = ArctisAurora.Core.UI.VulkanControl;

namespace ArctisAurora.EngineWork.Rendering.Modules
{
    // Draws one window's UIQuads range, which the UI engine rebuilds from its tree each frame. Two
    // mirrors, one per column, because the shader reads the two as separate buffers.
    public unsafe class UIEngineModule : RenderingModule
    {
        internal override ERendererTypes rendererType => ERendererTypes.UIEngine;

        internal override ERendererStage RendererStage => ERendererStage.UI;

        internal override uint[][] descriptorMaxCounts => new uint[][] {
            new uint[] { 1, 1, 1, 1, 1, 1 },          // set 1: camera UBO, geometry SSBO, control SSBO, gradient SSBO, paint SSBO, effect SSBO
            new uint[] { TextureAsset.MaxTextures }   // set 2: one sampler per distinct texture
        };

        internal override uint GetVariableDescriptorCount(int set) => TextureAsset.MaxTextures;

        internal override PhysicalDeviceFeatures features => new()
        {
            SamplerAnisotropy = true
        };

        internal override PhysicalDeviceVulkan12Features features12 => new()
        {
            SType = StructureType.PhysicalDeviceVulkan12Features,
            ScalarBlockLayout = true,
            RuntimeDescriptorArray = true,
            DescriptorIndexing = true,
            DescriptorBindingVariableDescriptorCount = true,
            DescriptorBindingPartiallyBound = true,
            ShaderSampledImageArrayNonUniformIndexing = true
        };

        internal override List<List<DescriptorType>> descriptorTypes => new()
        {
            new List<DescriptorType> {
                DescriptorType.UniformBuffer, DescriptorType.StorageBuffer, DescriptorType.StorageBuffer,
                DescriptorType.StorageBuffer, DescriptorType.StorageBuffer, DescriptorType.StorageBuffer
            },
            new List<DescriptorType> {
                DescriptorType.CombinedImageSampler
            }
        };
        internal override List<List<ShaderStageFlags>> shaderStages => new()
        {
            new List<ShaderStageFlags>{
                ShaderStageFlags.VertexBit, ShaderStageFlags.VertexBit, ShaderStageFlags.VertexBit,
                ShaderStageFlags.FragmentBit, ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit, ShaderStageFlags.VertexBit
            },
            new List<ShaderStageFlags>{
                ShaderStageFlags.FragmentBit
            }
        };
        internal override DescriptorBindingFlags[][] descriptorBindingFlags => [
            [
                DescriptorBindingFlags.None, DescriptorBindingFlags.None, DescriptorBindingFlags.None,
                DescriptorBindingFlags.None, DescriptorBindingFlags.None, DescriptorBindingFlags.None
            ],
            [
                DescriptorBindingFlags.VariableDescriptorCountBit | DescriptorBindingFlags.PartiallyBoundBit
            ]
        ];
        internal override int variableSetCount => 2;

        internal override IReadOnlyList<Entity> renderEntities { get; set; } = Array.Empty<Entity>();

        private AVulkanMesh _quad = null!;

        // Per-image mirrors of this window's range of the two GPU columns and its indirect draw,
        // recreated only when the range outgrows them.
        private Silk.NET.Vulkan.Buffer[] _geometryBuffers = null!;
        private DeviceMemory[] _geometryMemories = null!;
        private nint[] _geometryMapped = null!;
        private Silk.NET.Vulkan.Buffer[] _controlBuffers = null!;
        private DeviceMemory[] _controlMemories = null!;
        private nint[] _controlMapped = null!;
        private Silk.NET.Vulkan.Buffer[] _indirectBuffers = null!;
        private DeviceMemory[] _indirectMemories = null!;
        private nint[] _indirectMapped = null!;
        private int[] _mirrorCapacity = null!;
        private const int minMirrorRows = 256;

        // per-image mirrors of the paint and gradient pools
        private readonly TableMirror<GpuPaint> _paints = new TableMirror<GpuPaint>();
        private readonly TableMirror<GpuGradient> _gradients = new TableMirror<GpuGradient>();
        private readonly TableMirror<GpuEffect> _effects = new TableMirror<GpuEffect>();

        private int[] _frameBuiltCapacity = null!;
        private int[] _frameTableVersion = null!;

        // This window's rows in UIEngine.Quads as first << 32 | count, published by UIEngine.BuildDrawLists.
        private long _quadRange;

        // A drag preview's control, drawn in place of a tree; it stays in its own window's tree. The
        // rect is its box, built on the main thread for the camera.
        internal Control? rangeRoot;
        internal LayoutRect? rangeRect;

        // What the last MirrorDrawList copied.
        private int _drawCount;

        private WindowRoot _uiRoot;

        // The tree this module draws. Assigning tears the outgoing tree down.
        public WindowRoot uiRoot
        {
            get => _uiRoot;
            set
            {
                _uiRoot?.Destroy();
                _uiRoot = value;
                value?.FitTo(window.os.windowSize);
            }
        }

        // Pending when this image's sets predate its mirrors or the texture table.
        internal override bool HasPendingWork(int frame)
            => _frameBuiltCapacity[frame] != _mirrorCapacity[frame] || _frameTableVersion[frame] != TextureAsset.TableVersion;

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
            _frameTableVersion = new int[window.imageCount];
            Array.Fill(_frameTableVersion, -1);

            DestroyMirrors();
            _geometryBuffers = new Silk.NET.Vulkan.Buffer[window.imageCount];
            _geometryMemories = new DeviceMemory[window.imageCount];
            _geometryMapped = new nint[window.imageCount];
            _controlBuffers = new Silk.NET.Vulkan.Buffer[window.imageCount];
            _controlMemories = new DeviceMemory[window.imageCount];
            _controlMapped = new nint[window.imageCount];
            _indirectBuffers = new Silk.NET.Vulkan.Buffer[window.imageCount];
            _indirectMemories = new DeviceMemory[window.imageCount];
            _indirectMapped = new nint[window.imageCount];
            _mirrorCapacity = new int[window.imageCount];
            Array.Fill(_mirrorCapacity, -1);
            _paints.Resize((int)window.imageCount);
            _gradients.Resize((int)window.imageCount);
            _effects.Resize((int)window.imageCount);
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
            MirrorDrawList(imageIndex);
            if (_paints.Sync(Palettes.Paints, imageIndex)) _frameBuiltCapacity[imageIndex] = -1;
            if (_gradients.Sync(Gradients.Pool, imageIndex)) _frameBuiltCapacity[imageIndex] = -1;
            if (_effects.Sync(Effects.Pool, imageIndex)) _frameBuiltCapacity[imageIndex] = -1;
        }

        internal override void UpdateModule(int currentFrame)
        {
            if (_frameBuiltCapacity[currentFrame] != _mirrorCapacity[currentFrame])
            {
                CreateDescriptorPool(currentFrame, 0);
                AllocateDescriptorSets(currentFrame);
                UpdateDescriptorSets(currentFrame, _drawCount);
                _frameBuiltCapacity[currentFrame] = _mirrorCapacity[currentFrame];
                _frameTableVersion[currentFrame] = TextureAsset.TableVersion;
            }
            else if (_frameTableVersion[currentFrame] != TextureAsset.TableVersion)
            {
                WriteTextureTable(currentFrame);
                _frameTableVersion[currentFrame] = TextureAsset.TableVersion;
            }

            WriteCommandBuffers(currentFrame);
        }

        // Publishes this window's rows as one write.
        internal void PublishQuadRange(int first, int count)
            => Volatile.Write(ref _quadRange, ((long)first << 32) | (uint)count);

        // Copies this window's UIQuads range into the image's mirrors. Both arrays and the range are read
        // once: the main thread rebuilds the pool while this runs, and a growth swaps the arrays out
        // from under a second read.
        private void MirrorDrawList(int currentFrame)
        {
            DataPool quads = UIEngine.Quads;
            ControlGeometry[] geometry = quads.Backing<ControlGeometry>();
            VulkanControl[] controls = quads.Backing<VulkanControl>();
            long range = Volatile.Read(ref _quadRange);
            int first = (int)(range >> 32);
            _drawCount = Math.Clamp((int)range, 0, Math.Max(0, geometry.Length - first));

            if (_drawCount > _mirrorCapacity[currentFrame])
            {
                DestroyMirror(currentFrame);

                int capacity = Math.Max(minMirrorRows, (int)BitOperations.RoundUpToPowerOf2((uint)_drawCount));
                ulong geometrySize = (ulong)(sizeof(ControlGeometry) * capacity);
                ulong controlSize = (ulong)(sizeof(VulkanControl) * capacity);
                AVulkanBufferHandler.CreateMappedBuffer(geometrySize, ref _geometryBuffers[currentFrame], ref _geometryMemories[currentFrame], out _geometryMapped[currentFrame], AVulkanBufferHandler.storageBufferFlags);
                AVulkanBufferHandler.CreateMappedBuffer(controlSize, ref _controlBuffers[currentFrame], ref _controlMemories[currentFrame], out _controlMapped[currentFrame], AVulkanBufferHandler.storageBufferFlags);
                AVulkanBufferHandler.CreateMappedBuffer((ulong)sizeof(DrawIndexedIndirectCommand), ref _indirectBuffers[currentFrame], ref _indirectMemories[currentFrame], out _indirectMapped[currentFrame], BufferUsageFlags.IndirectBufferBit);
                _mirrorCapacity[currentFrame] = capacity;
            }

            AVulkanBufferHandler.WriteMappedRange(_geometryMapped[currentFrame], 0, geometry, first, _drawCount);
            AVulkanBufferHandler.WriteMappedRange(_controlMapped[currentFrame], 0, controls, first, _drawCount);
            Unsafe.Write((void*)_indirectMapped[currentFrame], new DrawIndexedIndirectCommand
            {
                IndexCount = (uint)_quad.indices.Length,
                InstanceCount = (uint)_drawCount
            });
        }

        private void DestroyMirrors()
        {
            _paints.DestroyAll();
            _gradients.DestroyAll();
            _effects.DestroyAll();
            if (_geometryBuffers == null) return;

            for (int i = 0; i < _geometryBuffers.Length; i++)
                DestroyMirror(i);
        }

        // Frees one image's mirrors and its indirect draw.
        private void DestroyMirror(int image)
        {
            if (_geometryBuffers[image].Handle == default) return;

            Renderer.vk.UnmapMemory(Renderer.logicalDevice, _geometryMemories[image]);
            Renderer.vk.DestroyBuffer(Renderer.logicalDevice, _geometryBuffers[image], null);
            Renderer.vk.FreeMemory(Renderer.logicalDevice, _geometryMemories[image], null);

            Renderer.vk.UnmapMemory(Renderer.logicalDevice, _controlMemories[image]);
            Renderer.vk.DestroyBuffer(Renderer.logicalDevice, _controlBuffers[image], null);
            Renderer.vk.FreeMemory(Renderer.logicalDevice, _controlMemories[image], null);

            Renderer.vk.UnmapMemory(Renderer.logicalDevice, _indirectMemories[image]);
            Renderer.vk.DestroyBuffer(Renderer.logicalDevice, _indirectBuffers[image], null);
            Renderer.vk.FreeMemory(Renderer.logicalDevice, _indirectMemories[image], null);

            _geometryBuffers[image] = default;
            _geometryMemories[image] = default;
            _geometryMapped[image] = 0;
            _controlBuffers[image] = default;
            _controlMemories[image] = default;
            _controlMapped[image] = 0;
            _indirectBuffers[image] = default;
            _indirectMemories[image] = default;
            _indirectMapped[image] = 0;
            _mirrorCapacity[image] = -1;
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
                    DescriptorCount = 5
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
            if (_mirrorCapacity[currentFrame] < 0 || _paints.CapacityAt(currentFrame) < 0 || _gradients.CapacityAt(currentFrame) < 0
                || _effects.CapacityAt(currentFrame) < 0) return;

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
                Range = (ulong)(Unsafe.SizeOf<ControlGeometry>() * _mirrorCapacity[currentFrame])
            };
            DescriptorBufferInfo controlInfo = new DescriptorBufferInfo()
            {
                Buffer = _controlBuffers[currentFrame],
                Offset = 0,
                Range = (ulong)(Unsafe.SizeOf<VulkanControl>() * _mirrorCapacity[currentFrame])
            };
            DescriptorBufferInfo gradientInfo = new DescriptorBufferInfo()
            {
                Buffer = _gradients.BufferAt(currentFrame),
                Offset = 0,
                Range = (ulong)(Unsafe.SizeOf<GpuGradient>() * _gradients.CapacityAt(currentFrame))
            };
            DescriptorBufferInfo paintInfo = new DescriptorBufferInfo()
            {
                Buffer = _paints.BufferAt(currentFrame),
                Offset = 0,
                Range = (ulong)(Unsafe.SizeOf<GpuPaint>() * _paints.CapacityAt(currentFrame))
            };
            DescriptorBufferInfo effectInfo = new DescriptorBufferInfo()
            {
                Buffer = _effects.BufferAt(currentFrame),
                Offset = 0,
                Range = (ulong)(Unsafe.SizeOf<GpuEffect>() * _effects.CapacityAt(currentFrame))
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
                },
                new WriteDescriptorSet
                {
                    SType = StructureType.WriteDescriptorSet,
                    DstSet = frameResources[currentFrame].sets[0],
                    DstBinding = 3,
                    DescriptorCount = 1,
                    DstArrayElement = 0,
                    DescriptorType = DescriptorType.StorageBuffer,
                    PBufferInfo = &gradientInfo
                },
                new WriteDescriptorSet
                {
                    SType = StructureType.WriteDescriptorSet,
                    DstSet = frameResources[currentFrame].sets[0],
                    DstBinding = 4,
                    DescriptorCount = 1,
                    DstArrayElement = 0,
                    DescriptorType = DescriptorType.StorageBuffer,
                    PBufferInfo = &paintInfo
                },
                new WriteDescriptorSet
                {
                    SType = StructureType.WriteDescriptorSet,
                    DstSet = frameResources[currentFrame].sets[0],
                    DstBinding = 5,
                    DescriptorCount = 1,
                    DstArrayElement = 0,
                    DescriptorType = DescriptorType.StorageBuffer,
                    PBufferInfo = &effectInfo
                }
            };
            fixed (WriteDescriptorSet* writesPtr = writes)
            {
                Renderer.vk!.UpdateDescriptorSets(Renderer.logicalDevice, (uint)writes.Length, writesPtr, 0, null);
            }

            WriteTextureTable(currentFrame);
        }

        // The texture table (set 2, binding 0), indexed by TextureAsset.textureIndex — the same
        // index a glyph row carries.
        private void WriteTextureTable(int currentFrame)
        {
            IReadOnlyList<TextureAsset> table = TextureAsset.Table;
            if (table.Count == 0) return;

            Sampler sampler = AssetRegistries
                .GetRegistryByValueType<string, SamplerAsset>(typeof(SamplerAsset))["ControlSampler"].handle;

            DescriptorImageInfo[] samplerInfos = new DescriptorImageInfo[table.Count];
            for (int k = 0; k < table.Count; k++)
            {
                samplerInfos[k] = new()
                {
                    ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
                    ImageView = table[k].textureImageView,
                    Sampler = sampler
                };
            }
            fixed (DescriptorImageInfo* samplerInfosPtr = samplerInfos)
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
                    SrcAlphaBlendFactor = BlendFactor.One,
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

            if (frameResources[currentFrame] != null && frameResources[currentFrame].sets != null)
            {
                Renderer.vk.CmdBindPipeline(commandBuffers[currentFrame], PipelineBindPoint.Graphics, pipeline);
                Renderer.vk.CmdBindDescriptorSets(commandBuffers[currentFrame], PipelineBindPoint.Graphics, pipelineLayout, 0, 1, Renderer.globalSets[currentFrame], 0, null);

                Viewport viewport = new Viewport() { X = 0, Y = 0, Width = window.swapchainExtent.Width, Height = window.swapchainExtent.Height, MinDepth = 0, MaxDepth = 1 };
                Rect2D scissor = new Rect2D() { Offset = { X = 0, Y = 0 }, Extent = window.swapchainExtent };
                Renderer.vk.CmdSetViewport(commandBuffers[currentFrame], 0, 1, &viewport);
                Renderer.vk.CmdSetScissor(commandBuffers[currentFrame], 0, 1, &scissor);

                ulong offset = 0;
                Renderer.vk.CmdBindVertexBuffers(commandBuffers[currentFrame], 0, 1, ref _quad.vertexBuffer, &offset);
                Renderer.vk.CmdBindIndexBuffer(commandBuffers[currentFrame], _quad.indexBuffer, 0, IndexType.Uint32);
                Renderer.vk.CmdBindDescriptorSets(commandBuffers[currentFrame], PipelineBindPoint.Graphics, pipelineLayout, 1, 1, frameResources[currentFrame].sets[0], 0, null);
                Renderer.vk.CmdBindDescriptorSets(commandBuffers[currentFrame], PipelineBindPoint.Graphics, pipelineLayout, 2, 1, frameResources[currentFrame].sets[1], 0, null);
                Renderer.vk.CmdDrawIndexedIndirect(commandBuffers[currentFrame], _indirectBuffers[currentFrame], 0, 1, (uint)sizeof(DrawIndexedIndirectCommand));
            }

            Renderer.vk.CmdEndRendering(commandBuffers[currentFrame]);

            ImageBarrier(commandBuffers[currentFrame], outputImages[currentFrame],
                ImageLayout.ColorAttachmentOptimal, ImageLayout.ShaderReadOnlyOptimal,
                PipelineStageFlags.ColorAttachmentOutputBit, PipelineStageFlags.FragmentShaderBit,
                AccessFlags.ColorAttachmentWriteBit, AccessFlags.ShaderReadBit);

            if (Renderer.vk.EndCommandBuffer(commandBuffers[currentFrame]) != Result.Success)
                throw new Exception("Failed to record command buffer");
        }

        // Per-image mirrors of one handle-less pool column, patched by dirty range.
        private sealed class TableMirror<T> where T : unmanaged
        {
            private Silk.NET.Vulkan.Buffer[] _buffers = null!;
            private DeviceMemory[] _memories = null!;
            private nint[] _mapped = null!;
            private int[] _capacity = null!;
            private ulong[] _since = null!;

            public Silk.NET.Vulkan.Buffer BufferAt(int image) => _buffers[image];
            public int CapacityAt(int image) => _capacity[image];

            public void Resize(int imageCount)
            {
                _buffers = new Silk.NET.Vulkan.Buffer[imageCount];
                _memories = new DeviceMemory[imageCount];
                _mapped = new nint[imageCount];
                _capacity = new int[imageCount];
                Array.Fill(_capacity, -1);
                _since = new ulong[imageCount];
            }

            // Copies what changed since this image last synced. True when the buffer was replaced.
            public bool Sync(DataPool pool, int image)
            {
                T[] data = pool.Backing<T>();
                if (_capacity[image] != data.Length)
                {
                    Destroy(image);
                    AVulkanBufferHandler.CreateMappedBuffer((ulong)(sizeof(T) * data.Length), ref _buffers[image], ref _memories[image], out _mapped[image], AVulkanBufferHandler.storageBufferFlags);
                    _capacity[image] = data.Length;
                    _since[image] = pool.ContentVersion;
                    AVulkanBufferHandler.WriteMappedRange(_mapped[image], data, 0, Math.Min(pool.Count, data.Length));
                    return true;
                }

                if (pool.TryGetDirtyRange(_since[image], out int min, out int max, out ulong current))
                    AVulkanBufferHandler.WriteMappedRange(_mapped[image], data, min, Math.Min(max, data.Length - 1) - min + 1);
                _since[image] = current;
                return false;
            }

            public void DestroyAll()
            {
                if (_buffers == null) return;

                for (int i = 0; i < _buffers.Length; i++)
                    Destroy(i);
            }

            private void Destroy(int image)
            {
                if (_buffers[image].Handle == default) return;

                Renderer.vk.UnmapMemory(Renderer.logicalDevice, _memories[image]);
                Renderer.vk.DestroyBuffer(Renderer.logicalDevice, _buffers[image], null);
                Renderer.vk.FreeMemory(Renderer.logicalDevice, _memories[image], null);

                _buffers[image] = default;
                _memories[image] = default;
                _mapped[image] = 0;
                _capacity[image] = -1;
            }
        }
    }
}
