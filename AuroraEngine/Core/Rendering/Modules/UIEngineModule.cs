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
            new uint[] { 1, 1, 1, 1, 1 },             // set 1: camera UBO, geometry SSBO, control SSBO, gradient SSBO, paint SSBO
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
                DescriptorType.StorageBuffer, DescriptorType.StorageBuffer
            },
            new List<DescriptorType> {
                DescriptorType.CombinedImageSampler
            }
        };
        internal override List<List<ShaderStageFlags>> shaderStages => new()
        {
            new List<ShaderStageFlags>{
                ShaderStageFlags.VertexBit, ShaderStageFlags.VertexBit, ShaderStageFlags.VertexBit,
                ShaderStageFlags.FragmentBit, ShaderStageFlags.VertexBit
            },
            new List<ShaderStageFlags>{
                ShaderStageFlags.FragmentBit
            }
        };
        internal override DescriptorBindingFlags[][] descriptorBindingFlags => [
            [
                DescriptorBindingFlags.None, DescriptorBindingFlags.None, DescriptorBindingFlags.None,
                DescriptorBindingFlags.None, DescriptorBindingFlags.None
            ],
            [
                DescriptorBindingFlags.VariableDescriptorCountBit | DescriptorBindingFlags.PartiallyBoundBit
            ]
        ];
        internal override int variableSetCount => 2;

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
        private int[] _mirrorCapacity = null!;

        // Per-image mirrors of Palettes.Table, rewritten when the published table is a different array.
        private Silk.NET.Vulkan.Buffer[] _paintBuffers = null!;
        private DeviceMemory[] _paintMemories = null!;
        private nint[] _paintMapped = null!;
        private Vector4[]?[] _paintWritten = null!;

        // One table for the whole process, uploaded once — never destroyed with a window, or the
        // windows that outlive it would keep a dangling descriptor.
        private static Silk.NET.Vulkan.Buffer _gradientBuffer;
        private static DeviceMemory _gradientMemory;

        private int[] _frameBuiltCapacity = null!;
        private int[] _frameTableVersion = null!;

        // This window's rows in UIEngine.Quads as first << 32 | count, published by UIEngine.BuildDrawLists.
        private long _quadRange;

        // A drag preview's control, drawn in place of a tree; it stays in its own window's tree. The
        // rect is its box, built on the main thread for the camera.
        internal Control? rangeRoot;
        internal LayoutRect? rangeRect;

        // What the last MirrorDrawList copied, and so what the record that follows it may draw.
        private int _drawFirst;
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

        // The list is rebuilt every frame, so every frame is pending. A dirty flag on the rebuild is
        // what gives this an answer worth asking.
        internal override bool HasPendingWork(int frame) => true;

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
            _mirrorCapacity = new int[window.imageCount];
            Array.Fill(_mirrorCapacity, -1);
            _paintBuffers = new Silk.NET.Vulkan.Buffer[window.imageCount];
            _paintMemories = new DeviceMemory[window.imageCount];
            _paintMapped = new nint[window.imageCount];
            _paintWritten = new Vector4[]?[window.imageCount];
        }

        internal override void PrepareObjects()
        {
            Renderer.renderer.CreateCommandPool((uint)Renderer.queueAllocator.GetFamilyIndex(QueueFlags.GraphicsBit), out moduleCommandPool, CommandPoolCreateFlags.ResetCommandBufferBit);
            RegisterVulkanQueue(Renderer.queueAllocator, Renderer.vk, ref Renderer.logicalDevice);
            _quad = AssetRegistries.GetRegistryByValueType<string, AVulkanMesh>(typeof(AVulkanMesh))["uidefault"];
            CreateGradientTable();
            PrepareCamera();
        }

        // Slot 0 is always present, so the buffer is never zero-sized even with no gradients authored.
        private static void CreateGradientTable()
        {
            if (_gradientBuffer.Handle != default) return;

            GpuGradient[] gradients = Gradients.Table;
            AVulkanBufferHandler.CreateBuffer(ref gradients, ref Renderer.transferQueue, ref Renderer.transferCommandPool, ref _gradientBuffer, ref _gradientMemory, BufferUsageFlags.StorageBufferBit);
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
            MirrorDrawList(currentFrame);
            MirrorPaints(currentFrame);

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
            _drawFirst = (int)(range >> 32);
            _drawCount = Math.Clamp((int)range, 0, Math.Max(0, geometry.Length - _drawFirst));

            if (_mirrorCapacity[currentFrame] != geometry.Length)
            {
                DestroyMirror(currentFrame);

                ulong geometrySize = (ulong)(sizeof(ControlGeometry) * geometry.Length);
                ulong controlSize = (ulong)(sizeof(VulkanControl) * controls.Length);
                AVulkanBufferHandler.CreateMappedBuffer(geometrySize, ref _geometryBuffers[currentFrame], ref _geometryMemories[currentFrame], out _geometryMapped[currentFrame], AVulkanBufferHandler.storageBufferFlags);
                AVulkanBufferHandler.CreateMappedBuffer(controlSize, ref _controlBuffers[currentFrame], ref _controlMemories[currentFrame], out _controlMapped[currentFrame], AVulkanBufferHandler.storageBufferFlags);
                _mirrorCapacity[currentFrame] = geometry.Length;
            }

            if (_drawCount == 0) return;

            AVulkanBufferHandler.WriteMappedRange(_geometryMapped[currentFrame], geometry, _drawFirst, _drawCount);
            AVulkanBufferHandler.WriteMappedRange(_controlMapped[currentFrame], controls, _drawFirst, _drawCount);
        }

        // Copies the paint table into the image's mirror when a new one was published. A mirror of another
        // size is replaced, and the descriptor rebuild that follows points the set at it.
        private void MirrorPaints(int currentFrame)
        {
            Vector4[] paints = Palettes.Table;
            if (_paintWritten[currentFrame] == paints) return;

            if (_paintWritten[currentFrame]?.Length != paints.Length)
            {
                DestroyPaintMirror(currentFrame);
                AVulkanBufferHandler.CreateMappedBuffer((ulong)(sizeof(Vector4) * paints.Length), ref _paintBuffers[currentFrame], ref _paintMemories[currentFrame], out _paintMapped[currentFrame], AVulkanBufferHandler.storageBufferFlags);
                _frameBuiltCapacity[currentFrame] = -1;
            }

            AVulkanBufferHandler.WriteMappedRange(_paintMapped[currentFrame], paints, 0, paints.Length);
            _paintWritten[currentFrame] = paints;
        }

        private void DestroyMirrors()
        {
            if (_geometryBuffers == null) return;

            for (int i = 0; i < _geometryBuffers.Length; i++)
            {
                DestroyMirror(i);
                DestroyPaintMirror(i);
            }
        }

        private void DestroyPaintMirror(int image)
        {
            if (_paintBuffers[image].Handle == default) return;

            Renderer.vk.UnmapMemory(Renderer.logicalDevice, _paintMemories[image]);
            Renderer.vk.DestroyBuffer(Renderer.logicalDevice, _paintBuffers[image], null);
            Renderer.vk.FreeMemory(Renderer.logicalDevice, _paintMemories[image], null);

            _paintBuffers[image] = default;
            _paintMemories[image] = default;
            _paintMapped[image] = 0;
            _paintWritten[image] = null;
        }

        // Frees one image's pair of mirrors.
        private void DestroyMirror(int image)
        {
            if (_geometryBuffers[image].Handle == default) return;

            Renderer.vk.UnmapMemory(Renderer.logicalDevice, _geometryMemories[image]);
            Renderer.vk.DestroyBuffer(Renderer.logicalDevice, _geometryBuffers[image], null);
            Renderer.vk.FreeMemory(Renderer.logicalDevice, _geometryMemories[image], null);

            Renderer.vk.UnmapMemory(Renderer.logicalDevice, _controlMemories[image]);
            Renderer.vk.DestroyBuffer(Renderer.logicalDevice, _controlBuffers[image], null);
            Renderer.vk.FreeMemory(Renderer.logicalDevice, _controlMemories[image], null);

            _geometryBuffers[image] = default;
            _geometryMemories[image] = default;
            _geometryMapped[image] = 0;
            _controlBuffers[image] = default;
            _controlMemories[image] = default;
            _controlMapped[image] = 0;
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
                    DescriptorCount = 4
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
            if (_mirrorCapacity[currentFrame] < 0 || _paintWritten[currentFrame] == null) return;

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
                Buffer = _gradientBuffer,
                Offset = 0,
                Range = (ulong)(Unsafe.SizeOf<GpuGradient>() * Gradients.Count)
            };
            DescriptorBufferInfo paintInfo = new DescriptorBufferInfo()
            {
                Buffer = _paintBuffers[currentFrame],
                Offset = 0,
                Range = (ulong)(Unsafe.SizeOf<Vector4>() * _paintWritten[currentFrame]!.Length)
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

            if (_drawCount > 0 && frameResources[currentFrame] != null && frameResources[currentFrame].sets != null)
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
                Renderer.vk.CmdBindDescriptorSets(commandBuffers[currentFrame], PipelineBindPoint.Graphics, pipelineLayout, 2, 1, frameResources[currentFrame].sets[1], 0, null);
                Renderer.vk.CmdDrawIndexed(commandBuffers[currentFrame], (uint)_quad.indices.Length, (uint)_drawCount, 0, 0, (uint)_drawFirst);
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
