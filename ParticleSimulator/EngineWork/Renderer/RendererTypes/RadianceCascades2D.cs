using System.Drawing.Imaging;
using System;
using ArctisAurora.CustomEntities;
using ArctisAurora.EngineWork.Renderer.Helpers;
using Silk.NET.Core.Native;
using Silk.NET.GLFW;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;
using Image = Silk.NET.Vulkan.Image;
using ImageLayout = Silk.NET.Vulkan.ImageLayout;
using ArctisAurora.EngineWork.EngineEntity;

namespace ArctisAurora.EngineWork.Renderer.RendererTypes
{
    internal unsafe class RadianceCascades2D : VulkanRenderer
    {
        string[] requiredExtensions =
        {
            "VK_KHR_swapchain",
            "VK_EXT_scalar_block_layout",
            "VK_EXT_descriptor_indexing",
            "VK_KHR_spirv_1_4",
        };
        //
        Pipeline computePipeline;
        PipelineLayout computePipelineLayout;

        //probe on layers pipeline
        DescriptorPool probeDescriptorPool;
        DescriptorSetLayout probeDescriptorSetLayout;
        DescriptorSet[] probeDescriptorSets;
        Pipeline probePipeline;
        PipelineLayout probePipelineLayout;

        // drawing on layers pipeline
        DescriptorPool drawingDescriptorPool;
        DescriptorSetLayout drawingDescriptorSetLayout;
        DescriptorSet[] drawingDescriptorSets;
        Pipeline drawingPipeline;
        PipelineLayout drawingPipelineLayout;

        // rendering layers pipeline
        DescriptorPool layerComputeDescriptorPool;
        DescriptorSetLayout layerComputeDescriptorSetLayout;
        DescriptorSet[] layerComputeDescriptorSets;
        Pipeline layerComputePipeline;
        PipelineLayout layerComputePipelineLayout;

        // phosphorous pipeline
        DescriptorPool phosphrousDescriptorPool;
        DescriptorSetLayout phosphrousDescriptorSetLayout;
        DescriptorSet[] phosphrousDescriptorSets;
        Pipeline phosphrousPipeline;
        PipelineLayout phosphrousPipelineLayout;

        // images
        DeviceMemory[] storageImageDM;  // frame buffer image
        Image[] storageImage;
        ImageView[] storageImageView;
        DeviceMemory lightsDM;          // where the lights are in the 2D sceme image
        Image lightsImage;
        ImageView lightsImageView;
        DeviceMemory[] probesImageDM;     // probes
        Image[] probesImage;
        ImageView[] probesImageView;

        private DateTime lastFrameTime = DateTime.Now;


        struct ProbeLayer()
        {
            //Probe[] probes;
            public int cascade = 0;
            public int rayCount = 4;
            public int rayLength = 8;
            public int rayOffset = 0;
            public Vector2D<int> probeCount;
            public Vector2D<int> probeDst;
            public Vector2D<float> offset;
            public float importance;
            public float pad;
        }

        int cascadeCount = 4;

        ProbeLayer[] probeLayers;
        Buffer probesB;
        DeviceMemory probesDM;

        public Vector2D<int>[][] pos;
        Buffer[] probesPostionB;
        DeviceMemory[] probesPositionDM;

        internal struct WorldData()
        {
            internal Vector3D<float> brushColor = new Vector3D<float>(1, 1, 1);
            internal float lightStr = 1.0f;
            internal float emissive = 0.0f;
            internal Vector2D<int> mousePos = new Vector2D<int>(0, 0);
            internal bool isLMBDown = false;
            internal bool padding1;
            internal bool padding2;
            internal bool padding3;
            internal bool isRMBDown = false;
            internal bool padding4;
            internal bool padding5;
            internal bool padding6;
            internal bool isEditingLight = true;
            internal bool padding7;
            internal bool padding8;
            internal bool padding9;
            internal int editableLayer = 0;
            internal int brushSize = 5;
        }
        internal static WorldData worldData = new WorldData();
        // Mouse position data
        internal static Buffer mousePosBuffer;
        internal static DeviceMemory mousePosMemory;

        internal struct PhosphorusData()
        {
            internal float deltaTime;    // DT
            internal float decayTime = 0.8f;    // time it takes to fully decay from 1 to 0
        }
        internal static PhosphorusData pd = new PhosphorusData();
        internal static Buffer phosphorusDataBuffer;
        internal static DeviceMemory phosphorusDM;

        internal RadianceCascades2D()
        {
            setup();
            //
            int _graphicsQFamilyIndex = AVulkanHelper.FindQueueFamilyIndex(ref _gpu, ref _qfm, QueueFlags.GraphicsBit);
            uint _presentSupportIndex = AVulkanHelper.FindPresentSupportIndex(ref _qfm, ref _glWindow._driverSurface, ref _glWindow._surface);
            _graphicsQueue = _vulkan.GetDeviceQueue(_logicalDevice, (uint)_graphicsQFamilyIndex, 0);
            _presentQueue = _vulkan.GetDeviceQueue(_logicalDevice, _presentSupportIndex, 0);

            //create the swapchain
            _swapchain = new Swapchain(ref _glWindow._driverSurface, ref _glWindow._surface);
            _swapchain.DoSwapchainMethodSequence(ref _extent);        //swapchain methods for simplicity sake
            _swapimageCount = _swapchain._swapchainImages.Length;     //engine related thing

            _camera = new AuroraCamera();
            CreateCommandPool();

            //create buffers
            AVulkanBufferHandler.CreateBuffer(ref worldData, ref mousePosBuffer, ref mousePosMemory, BufferUsageFlags.UniformBufferBit);
            AVulkanBufferHandler.UpdateBuffer(ref worldData, ref mousePosBuffer, ref mousePosMemory, BufferUsageFlags.UniformBufferBit);
            CreateImages();
            AVulkanBufferHandler.CreateBuffer(ref pd, ref phosphorusDataBuffer, ref phosphorusDM, BufferUsageFlags.UniformBufferBit);
            AVulkanBufferHandler.UpdateBuffer(ref pd, ref phosphorusDataBuffer, ref phosphorusDM, BufferUsageFlags.UniformBufferBit);

            CreateDescriptorPool();
            CreateDrawingDescriptorPool();
            CreateLayerComputeDescriptorPool();
            CreateProbeDescriptorPool();
            CreatePhosphorusDescriptorPool();

            CreateDescriptorsetlayout();
            
            setupProbes(cascadeCount, 10, 80, 40, _extent);
            Layer L0 = new Layer();
            AddEntityToRenderQueue(L0);
            UpdateDescriptorSet();
            
            CreateProbePipeline();
            CreateComputePipeline();
            CreateDrawingPipeline();
            CreateLayerComputePipeline();
            CreatePhosphorusPipeline();

            CreateCommandBuffers();
            CreateSyncObjects();
        }

        internal override void AddEntityToRenderQueue(Entity _m)
        {
            _entitiesToRender.Add(_m);
            if (_descriptorSets != null)
            {
                _vulkan.FreeDescriptorSets(_logicalDevice, _descriptorPool, (uint)_descriptorSets.Length, _descriptorSets);
                _vulkan.FreeDescriptorSets(_logicalDevice, probeDescriptorPool, (uint)probeDescriptorSets.Length, probeDescriptorSets);
                _vulkan.FreeDescriptorSets(_logicalDevice, drawingDescriptorPool, (uint)drawingDescriptorSets.Length, drawingDescriptorSets);
                _vulkan.FreeDescriptorSets(_logicalDevice, layerComputeDescriptorPool, (uint)layerComputeDescriptorSets.Length, layerComputeDescriptorSets);
                UpdateDescriptorSet();
            }
            if (_commandBuffer != null)
            {
                _vulkan.FreeCommandBuffers(_logicalDevice, _commandPool, (uint)_commandBuffer.Length, _commandBuffer);
                CreateCommandBuffers();
            }
        }

        internal static void FreeCommandBuffer()
        {
            _vulkan.DeviceWaitIdle(_logicalDevice);
            _vulkan.FreeCommandBuffers(_logicalDevice, _commandPool, (uint)_commandBuffer.Length, _commandBuffer);
        }

        private void setup()
        {
            _rendererInstance = this;
            PhysicalDeviceFeatures _deviceFeatures = new PhysicalDeviceFeatures()
            {
                SamplerAnisotropy = true,
                //ShaderStorageImageReadWithoutFormat = true,
                //ShaderStorageImageWriteWithoutFormat = true
            };
            PhysicalDeviceVulkan12Features _vulkan12FT = new PhysicalDeviceVulkan12Features()
            {
                SType = StructureType.PhysicalDeviceVulkan12Features,
                BufferDeviceAddress = true,
                RuntimeDescriptorArray = true,
                DescriptorBindingVariableDescriptorCount = true,
                DescriptorIndexing = true,
            };

            CreateLogicalDevice(requiredExtensions, _vulkan12FT, _deviceFeatures);        //abstract the gpu so we can communicate
        }

        private void setupProbes(int layers, uint firstOffset, float firstDistance, int rayLength, Extent2D canvasSize)
        {
            pos = new Vector2D<int>[layers][];
            probesPostionB = new Buffer[layers];
            probesPositionDM = new DeviceMemory[layers];
            probeLayers = new ProbeLayer[layers];
            Vector2D<float> localSize = new Vector2D<float>(canvasSize.Width - firstOffset, canvasSize.Height - firstOffset);
            Vector2D<float> layerStart = new Vector2D<float>(firstOffset, firstOffset);
            float localDistance = firstDistance;
            int localRayLength = rayLength;
            int localRayOffset = (int)firstOffset;
            for (int i = 0; i < layers; i++)
            {
                int horizontal = (int)(localSize.X / localDistance) + 2;
                int vertical = (int)(localSize.Y / localDistance) + 2;
                probeLayers[i].cascade = i;
                probeLayers[i].rayCount = (int)Math.Pow(4, i + 1);
                probeLayers[i].rayLength = localRayLength;
                probeLayers[i].rayOffset = localRayOffset;
                probeLayers[i].probeCount = new Vector2D<int>(horizontal, vertical);
                probeLayers[i].probeDst = new Vector2D<int>((int)localDistance, (int)localDistance);

                pos[i] = new Vector2D<int>[vertical * horizontal];

                for (int y = 0; y < vertical; y++)
                {
                    for (int x = 0; x < horizontal; x++)
                    {
                        pos[i][y * horizontal + x] = new Vector2D<int>((int)(layerStart.X + localDistance * x), (int)(layerStart.Y + localDistance * y));
                    }
                }

                probeLayers[i].offset = new Vector2D<float>((float)pos[i][0].X / localDistance,
                                                            (float)pos[i][0].Y / localDistance);

                Vector2D<float> nextLayerOffset = new Vector2D<float>(localDistance / 2);
                layerStart += nextLayerOffset;
                localDistance = 2 * localDistance;

                localRayOffset += localRayLength;
                localRayLength *= 2;
            }

            float total = 0.0f;
            for (int i = 0; i < layers; i++)
            {
                probeLayers[i].importance = 1.0f - ((1 / layers) * i);
                total += probeLayers[i].importance;
            }
            for (int i = 0; i < layers; i++)
            {
                probeLayers[i].importance /= total;
            }


            AVulkanBufferHandler.CreateBuffer(ref probeLayers, ref probesB, ref probesDM, BufferUsageFlags.StorageBufferBit);
            for (int k = 0; k < layers; k++)
            {
                AVulkanBufferHandler.CreateBuffer(ref pos[k], ref probesPostionB[k], ref probesPositionDM[k], BufferUsageFlags.StorageBufferBit);
            }
            SetupProbeImages(probeLayers);
        }

        private void SetupProbeImages(ProbeLayer[] layers)
        {
            probesImage = new Image[cascadeCount];
            probesImageDM = new DeviceMemory[cascadeCount];
            probesImageView = new ImageView[cascadeCount];

            for (int i = 0; i < cascadeCount; i++)
            {
                Extent2D imageSize = new Extent2D((uint)(layers[i].probeCount.X * Math.Pow(2, i + 1)), (uint)(layers[i].probeCount.Y * Math.Pow(2, i + 1)));
                CreateImage(ref imageSize, ref probesImage[i], ref probesImageDM[i], ref probesImageView[i], Format.R8G8B8A8Unorm);
            }
        }

        internal override void MouseUpdate(double xPos, double yPos)
        {
            // here we do mouse updates for the compute shader.
            worldData.mousePos = new Vector2D<int>((int)xPos, (int)yPos);
            AVulkanBufferHandler.UpdateBuffer(ref worldData, ref mousePosBuffer, ref mousePosMemory, BufferUsageFlags.UniformBufferBit);
        }

        internal override void MouseClick(MouseButton button, InputAction action)
        {
            //change logic into this vvv
            int buttonID = 0; // 0 = LMB 1 = RMB 2 = MMB
            buttonID = (int)button;
            // ^^^

            bool left = false;
            bool right = false;
            bool middle = false;
            switch (button)
            {
                case MouseButton.Left:
                    left = true;
                    break;
                case MouseButton.Right:
                    left = false;
                    break;
                case MouseButton.Middle:
                    break;
                default: break;
            }

            switch (action)
            {
                case InputAction.Press:
                    if (left)
                    {
                        worldData.isLMBDown = true;
                    }
                    else
                    {
                        worldData.isRMBDown = true;
                    }
                    break;
                case InputAction.Release:
                    if (left)
                    {
                        worldData.isLMBDown = false;
                    }
                    else
                    {
                        worldData.isRMBDown = false;
                    }
                    break;
                default: break;
            }
            AVulkanBufferHandler.UpdateBuffer(ref worldData, ref mousePosBuffer, ref mousePosMemory, BufferUsageFlags.UniformBufferBit);
        }

        private void CreateDescriptorsetlayout()
        {
            // general image
            List<DescriptorType> _types1 = new List<DescriptorType> {
                DescriptorType.StorageImage, DescriptorType.StorageBuffer, DescriptorType.StorageImage, DescriptorType.StorageImage};
            List<ShaderStageFlags> _flags1 = new List<ShaderStageFlags> {
                ShaderStageFlags.ComputeBit, ShaderStageFlags.ComputeBit, ShaderStageFlags.ComputeBit, ShaderStageFlags.ComputeBit};
            DescriptorBindingFlags[] _DBF = {
                DescriptorBindingFlags.None, DescriptorBindingFlags.None, DescriptorBindingFlags.VariableDescriptorCountBit, DescriptorBindingFlags.VariableDescriptorCountBit};

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


            // probe data calculation
            List<DescriptorType> _types2 = new List<DescriptorType> {
                DescriptorType.StorageImage, DescriptorType.StorageBuffer, DescriptorType.StorageImage, DescriptorType.StorageBuffer, DescriptorType.StorageImage, DescriptorType.StorageImage };
            List<ShaderStageFlags> _flags2 = new List<ShaderStageFlags> {
                ShaderStageFlags.ComputeBit, ShaderStageFlags.ComputeBit, ShaderStageFlags.ComputeBit, ShaderStageFlags.ComputeBit, ShaderStageFlags.ComputeBit , ShaderStageFlags.ComputeBit };
            DescriptorBindingFlags[] _DBF2 = {
                DescriptorBindingFlags.None, DescriptorBindingFlags.None, DescriptorBindingFlags.VariableDescriptorCountBit, DescriptorBindingFlags.VariableDescriptorCountBit, DescriptorBindingFlags.VariableDescriptorCountBit, DescriptorBindingFlags.VariableDescriptorCountBit };
            
            uint[] _descriptorCount2 = new uint[_DBF2.Length];
            for (int i = 0; i < _DBF2.Length; i++)
            {
                if (_DBF2[i] == DescriptorBindingFlags.VariableDescriptorCountBit)
                {
                    _descriptorCount2[i] = _indexedCount;
                }
                else
                {
                    _descriptorCount2[i] = 1;
                }
            }

            CreateDescriptorSetLayout(_types2.Count, _types2, _flags2, ref probeDescriptorSetLayout, _DBF2, _descriptorCount2);

            // drawing
            List<DescriptorType> _types3 = new List<DescriptorType> {
                DescriptorType.UniformBuffer, DescriptorType.StorageImage , DescriptorType.StorageImage};
            List<ShaderStageFlags> _flags3 = new List<ShaderStageFlags> {
                ShaderStageFlags.ComputeBit, ShaderStageFlags.ComputeBit, ShaderStageFlags.ComputeBit };
            DescriptorBindingFlags[] _DBF3 = {
                DescriptorBindingFlags.None, DescriptorBindingFlags.VariableDescriptorCountBit, DescriptorBindingFlags.VariableDescriptorCountBit };

            uint[] _descriptorCount3 = new uint[_DBF3.Length];
            for (int i = 0; i < _DBF3.Length; i++)
            {
                if (_DBF3[i] == DescriptorBindingFlags.VariableDescriptorCountBit)
                {
                    _descriptorCount3[i] = _indexedCount;
                }
                else
                {
                    _descriptorCount3[i] = 1;
                }
            }

            CreateDescriptorSetLayout(_types3.Count, _types3, _flags3, ref drawingDescriptorSetLayout, _DBF3, _descriptorCount3);

            // layerCompute
            List<DescriptorType> _types4 = new List<DescriptorType> {
                DescriptorType.StorageBuffer, DescriptorType.StorageImage, DescriptorType.StorageImage, DescriptorType.StorageImage};
            List<ShaderStageFlags> _flags4 = new List<ShaderStageFlags> {
                ShaderStageFlags.ComputeBit, ShaderStageFlags.ComputeBit, ShaderStageFlags.ComputeBit, ShaderStageFlags.ComputeBit };
            DescriptorBindingFlags[] _DBF4 = {
                DescriptorBindingFlags.None, DescriptorBindingFlags.VariableDescriptorCountBit, DescriptorBindingFlags.VariableDescriptorCountBit, DescriptorBindingFlags.VariableDescriptorCountBit };

            uint[] _descriptorCount4 = new uint[_DBF4.Length];
            for (int i = 0; i < _DBF4.Length; i++)
            {
                if (_DBF4[i] == DescriptorBindingFlags.VariableDescriptorCountBit)
                {
                    _descriptorCount4[i] = _indexedCount;
                }
                else
                {
                    _descriptorCount4[i] = 1;
                }
            }

            CreateDescriptorSetLayout(_types4.Count, _types4, _flags4, ref layerComputeDescriptorSetLayout, _DBF4, _descriptorCount4);

            // phosphorous shader
            List<DescriptorType> _types5 = new List<DescriptorType> {
                DescriptorType.StorageImage, DescriptorType.StorageImage, DescriptorType.UniformBuffer, DescriptorType.StorageBuffer};
            List<ShaderStageFlags> _flags5 = new List<ShaderStageFlags> {
                ShaderStageFlags.ComputeBit, ShaderStageFlags.ComputeBit, ShaderStageFlags.ComputeBit, ShaderStageFlags.ComputeBit};
            DescriptorBindingFlags[] _DBF5 = {
                DescriptorBindingFlags.VariableDescriptorCountBit, DescriptorBindingFlags.VariableDescriptorCountBit, DescriptorBindingFlags.None, DescriptorBindingFlags.None };

            uint[] _descriptorCount5 = new uint[_DBF5.Length];
            for (int i = 0; i < _DBF5.Length; i++)
            {
                if (_DBF5[i] == DescriptorBindingFlags.VariableDescriptorCountBit)
                {
                    _descriptorCount5[i] = _indexedCount;
                }
                else
                {
                    _descriptorCount5[i] = 1;
                }
            }

            CreateDescriptorSetLayout(_types5.Count, _types5, _flags5, ref phosphrousDescriptorSetLayout, _DBF5, _descriptorCount5);
        }

        private void CreateComputePipeline()
        {
            DescriptorSetLayout[] _DSLayouts = new DescriptorSetLayout[1];
            _DSLayouts[0] = _descriptorSetLayout;
            fixed (DescriptorSetLayout* _setPtr = _DSLayouts)
            {
                PushConstantRange pushConstantRange = new PushConstantRange()
                {
                    StageFlags = ShaderStageFlags.ComputeBit,
                    Offset = 0,
                    Size = sizeof(int),
                };
                
                PipelineLayoutCreateInfo _pipelineLayoutCreateInfo = new()
                {
                    SType = StructureType.PipelineLayoutCreateInfo,
                    SetLayoutCount = (uint)_DSLayouts.Length,
                    PSetLayouts = _setPtr,
                    PushConstantRangeCount = 1,
                    PPushConstantRanges = &pushConstantRange
                };
                fixed (PipelineLayout* _pipePtr = &computePipelineLayout)
                    if (_vulkan.CreatePipelineLayout(_logicalDevice, ref _pipelineLayoutCreateInfo, null, _pipePtr) != Result.Success)
                    {
                        throw new Exception("Failed to create pipeline layout");
                    }
            }

            ShaderModule computeShaderModule = LoadShader("../../../Shaders/RadianceCascades2D/Radiance.comp.spv");

            PipelineShaderStageCreateInfo shaderStageCreateInfo = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.ComputeBit,
                Module = computeShaderModule,
                PName = (byte*)SilkMarshal.StringToPtr("main")
            };

            ComputePipelineCreateInfo pipelineCreateInfo = new ComputePipelineCreateInfo
            {
                SType = StructureType.ComputePipelineCreateInfo,
                Stage = shaderStageCreateInfo,
                Layout = computePipelineLayout
            };
            if (_vulkan.CreateComputePipelines(_logicalDevice, default, 1, &pipelineCreateInfo, null, out computePipeline) != Result.Success)
                throw new Exception("Failed to create compute pipeline");

            _vulkan.DestroyShaderModule(_logicalDevice, computeShaderModule, null);
        }

        private void CreateProbePipeline()
        {
            DescriptorSetLayout[] _DSLayouts = new DescriptorSetLayout[1];
            _DSLayouts[0] = probeDescriptorSetLayout;
            fixed (DescriptorSetLayout* _setPtr = _DSLayouts)
            {
                PushConstantRange pushConstantRange = new PushConstantRange()
                {
                    StageFlags = ShaderStageFlags.ComputeBit,
                    Offset = 0,
                    Size = sizeof(int),
                };

                PipelineLayoutCreateInfo _pipelineLayoutCreateInfo = new()
                {
                    SType = StructureType.PipelineLayoutCreateInfo,
                    SetLayoutCount = (uint)_DSLayouts.Length,
                    PSetLayouts = _setPtr,
                    PushConstantRangeCount = 1,
                    PPushConstantRanges = &pushConstantRange
                };

                fixed (PipelineLayout* _pipePtr = &probePipelineLayout)
                    if (_vulkan.CreatePipelineLayout(_logicalDevice, ref _pipelineLayoutCreateInfo, null, _pipePtr) != Result.Success)
                    {
                        throw new Exception("Failed to create pipeline layout");
                    }
            }

            ShaderModule computeShaderModule = LoadShader("../../../Shaders/RadianceCascades2D/Radiance.Probes.comp.spv");

            PipelineShaderStageCreateInfo shaderStageCreateInfo = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.ComputeBit,
                Module = computeShaderModule,
                PName = (byte*)SilkMarshal.StringToPtr("main")
            };

            ComputePipelineCreateInfo pipelineCreateInfo = new ComputePipelineCreateInfo
            {
                SType = StructureType.ComputePipelineCreateInfo,
                Stage = shaderStageCreateInfo,
                Layout = probePipelineLayout
            };
            if (_vulkan.CreateComputePipelines(_logicalDevice, default, 1, &pipelineCreateInfo, null, out probePipeline) != Result.Success)
                throw new Exception("Failed to create compute pipeline");

            _vulkan.DestroyShaderModule(_logicalDevice, computeShaderModule, null);
        }

        private void CreateDrawingPipeline()
        {
            DescriptorSetLayout[] _DSLayouts = new DescriptorSetLayout[1];
            _DSLayouts[0] = drawingDescriptorSetLayout;
            fixed (DescriptorSetLayout* _setPtr = _DSLayouts)
            {
                PipelineLayoutCreateInfo _pipelineLayoutCreateInfo = new()
                {
                    SType = StructureType.PipelineLayoutCreateInfo,
                    SetLayoutCount = (uint)_DSLayouts.Length,
                    PSetLayouts = _setPtr
                };

                fixed (PipelineLayout* _pipePtr = &drawingPipelineLayout)
                    if (_vulkan.CreatePipelineLayout(_logicalDevice, ref _pipelineLayoutCreateInfo, null, _pipePtr) != Result.Success)
                    {
                        throw new Exception("Failed to create pipeline layout");
                    }
            }

            ShaderModule computeShaderModule = LoadShader("../../../Shaders/RadianceCascades2D/Radiance.Drawing.comp.spv");

            PipelineShaderStageCreateInfo shaderStageCreateInfo = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.ComputeBit,
                Module = computeShaderModule,
                PName = (byte*)SilkMarshal.StringToPtr("main")
            };

            ComputePipelineCreateInfo pipelineCreateInfo = new ComputePipelineCreateInfo
            {
                SType = StructureType.ComputePipelineCreateInfo,
                Stage = shaderStageCreateInfo,
                Layout = drawingPipelineLayout
            };
            if (_vulkan.CreateComputePipelines(_logicalDevice, default, 1, &pipelineCreateInfo, null, out drawingPipeline) != Result.Success)
                throw new Exception("Failed to create compute pipeline");

            _vulkan.DestroyShaderModule(_logicalDevice, computeShaderModule, null);
        }

        private void CreateLayerComputePipeline()
        {
            DescriptorSetLayout[] _DSLayouts = new DescriptorSetLayout[1];
            _DSLayouts[0] = layerComputeDescriptorSetLayout;
            fixed (DescriptorSetLayout* _setPtr = _DSLayouts)
            {
                PushConstantRange pushConstantRange = new PushConstantRange()
                {
                    StageFlags = ShaderStageFlags.ComputeBit,
                    Offset = 0,
                    Size = sizeof(int),
                };

                PipelineLayoutCreateInfo _pipelineLayoutCreateInfo = new()
                {
                    SType = StructureType.PipelineLayoutCreateInfo,
                    SetLayoutCount = (uint)_DSLayouts.Length,
                    PSetLayouts = _setPtr,
                    PPushConstantRanges = &pushConstantRange,
                    PushConstantRangeCount = 1
                };

                fixed (PipelineLayout* _pipePtr = &layerComputePipelineLayout)
                    if (_vulkan.CreatePipelineLayout(_logicalDevice, ref _pipelineLayoutCreateInfo, null, _pipePtr) != Result.Success)
                    {
                        throw new Exception("Failed to create pipeline layout");
                    }
            }

            ShaderModule computeShaderModule = LoadShader("../../../Shaders/RadianceCascades2D/Radiance.LayerCompute.comp.spv");

            PipelineShaderStageCreateInfo shaderStageCreateInfo = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.ComputeBit,
                Module = computeShaderModule,
                PName = (byte*)SilkMarshal.StringToPtr("main")
            };

            ComputePipelineCreateInfo pipelineCreateInfo = new ComputePipelineCreateInfo
            {
                SType = StructureType.ComputePipelineCreateInfo,
                Stage = shaderStageCreateInfo,
                Layout = layerComputePipelineLayout
            };
            if (_vulkan.CreateComputePipelines(_logicalDevice, default, 1, &pipelineCreateInfo, null, out layerComputePipeline) != Result.Success)
                throw new Exception("Failed to create compute pipeline");

            _vulkan.DestroyShaderModule(_logicalDevice, computeShaderModule, null);
        }

        private void CreatePhosphorusPipeline()
        {
            DescriptorSetLayout[] _DSLayouts = new DescriptorSetLayout[1];
            _DSLayouts[0] = phosphrousDescriptorSetLayout;
            fixed (DescriptorSetLayout* _setPtr = _DSLayouts)
            {
                PushConstantRange pushConstantRange = new PushConstantRange()
                {
                    StageFlags = ShaderStageFlags.ComputeBit,
                    Offset = 0,
                    Size = sizeof(int),
                };

                PipelineLayoutCreateInfo _pipelineLayoutCreateInfo = new()
                {
                    SType = StructureType.PipelineLayoutCreateInfo,
                    SetLayoutCount = (uint)_DSLayouts.Length,
                    PSetLayouts = _setPtr,
                    PPushConstantRanges = &pushConstantRange,
                    PushConstantRangeCount = 1
                };

                fixed (PipelineLayout* _pipePtr = &phosphrousPipelineLayout)
                    if (_vulkan.CreatePipelineLayout(_logicalDevice, ref _pipelineLayoutCreateInfo, null, _pipePtr) != Result.Success)
                    {
                        throw new Exception("Failed to create pipeline layout");
                    }
            }

            ShaderModule computeShaderModule = LoadShader("../../../Shaders/RadianceCascades2D/Radiance.Phosphorus.comp.spv");

            PipelineShaderStageCreateInfo shaderStageCreateInfo = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.ComputeBit,
                Module = computeShaderModule,
                PName = (byte*)SilkMarshal.StringToPtr("main")
            };

            ComputePipelineCreateInfo pipelineCreateInfo = new ComputePipelineCreateInfo
            {
                SType = StructureType.ComputePipelineCreateInfo,
                Stage = shaderStageCreateInfo,
                Layout = phosphrousPipelineLayout
            };
            if (_vulkan.CreateComputePipelines(_logicalDevice, default, 1, &pipelineCreateInfo, null, out phosphrousPipeline) != Result.Success)
                throw new Exception("Failed to create compute pipeline");

            _vulkan.DestroyShaderModule(_logicalDevice, computeShaderModule, null);
        }

        private void CreateImages()
        {
            storageImage = new Image[_swapimageCount];
            storageImageDM = new DeviceMemory[_swapimageCount];
            storageImageView = new ImageView[_swapimageCount];

            lightsImage = new Image();
            lightsDM = new DeviceMemory();
            lightsImageView = new ImageView();

            for (int i = 0; i < _swapimageCount; i++)
            {
                CreateImage(ref _extent, ref storageImage[i], ref storageImageDM[i], ref storageImageView[i], Format.R8G8B8A8Unorm);
            }
            CreateImage(ref _extent, ref lightsImage, ref lightsDM, ref lightsImageView, Format.R8G8B8A8Unorm);
        }

        private void CreateImage(ref Extent2D size, ref Image image, ref DeviceMemory deviceMemory, ref ImageView imageView, Format format)
        {
            AVulkanBufferHandler.CreateImage(size.Width, size.Height, format, ImageTiling.Optimal, ImageUsageFlags.TransferSrcBit | ImageUsageFlags.StorageBit | ImageUsageFlags.TransferDstBit, MemoryPropertyFlags.DeviceLocalBit, ref image, ref deviceMemory);
            _swapchain.CreateImageView(ref imageView, ref image, ImageAspectFlags.ColorBit, format);

            CommandBuffer _imageTransition = AVulkanBufferHandler.BeginSingleTimeCommands();

            ImageMemoryBarrier _barrier = new()
            {
                SType = StructureType.ImageMemoryBarrier,
                OldLayout = ImageLayout.Undefined,
                NewLayout = ImageLayout.General,
                SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
                DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
                Image = image,
                SubresourceRange =
                    {
                    AspectMask = ImageAspectFlags.ColorBit,
                    BaseMipLevel = 0,
                    LevelCount = 1,
                    BaseArrayLayer = 0,
                    LayerCount = 1,
                    },
                SrcAccessMask = 0,
                DstAccessMask = AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit,
            };
            _vulkan.CmdPipelineBarrier(_imageTransition, PipelineStageFlags.TopOfPipeBit, PipelineStageFlags.ComputeShaderBit, 0, 0, null, 0, null, 1, ref _barrier);
            AVulkanBufferHandler.EndSingleTimeCommands(ref _imageTransition);
        }

        internal override void CreateDescriptorPool()
        {
            var _poolSizes = new DescriptorPoolSize[]
            {
                new DescriptorPoolSize()
                {
                    Type = DescriptorType.StorageImage,
                    DescriptorCount = (uint)(_swapimageCount * 3) + 20
                },
                new DescriptorPoolSize()
                {
                    Type = DescriptorType.UniformBuffer,
                    DescriptorCount = (uint)_swapimageCount
                },
                new DescriptorPoolSize()
                {
                    Type = DescriptorType.StorageBuffer,
                    DescriptorCount = (uint)_swapimageCount
                }
            };
            fixed (DescriptorPoolSize* _poolSizePtr = _poolSizes)
            fixed (DescriptorPool* _dpPtr = &_descriptorPool)
            {
                DescriptorPoolCreateInfo _createInfo = new DescriptorPoolCreateInfo()
                {
                    SType = StructureType.DescriptorPoolCreateInfo,
                    PoolSizeCount = (uint)_poolSizes.Length,
                    PPoolSizes = _poolSizePtr,
                    MaxSets = (uint)_swapimageCount,
                    Flags = DescriptorPoolCreateFlags.FreeDescriptorSetBit
                };
                if (_vulkan.CreateDescriptorPool(_logicalDevice, ref _createInfo, null, _dpPtr) != Result.Success)
                {
                    throw new Exception("Failed to create descriptor pool");
                }
            }
        }

        private void CreateProbeDescriptorPool()
        {
            var _poolSizes = new DescriptorPoolSize[]
            {
                new DescriptorPoolSize()
                {
                    Type = DescriptorType.StorageImage,
                    DescriptorCount = 50000
                },
                new DescriptorPoolSize()
                {
                    Type = DescriptorType.StorageBuffer,
                    DescriptorCount = (uint)(1 + cascadeCount) * 2
                }
            };
            fixed (DescriptorPoolSize* _poolSizePtr = _poolSizes)
            fixed (DescriptorPool* _dpPtr = &probeDescriptorPool)
            {
                DescriptorPoolCreateInfo _createInfo = new DescriptorPoolCreateInfo()
                {
                    SType = StructureType.DescriptorPoolCreateInfo,
                    PoolSizeCount = (uint)_poolSizes.Length,
                    PPoolSizes = _poolSizePtr,
                    MaxSets = (uint)_swapimageCount,
                    Flags = DescriptorPoolCreateFlags.FreeDescriptorSetBit
                };
                if (_vulkan.CreateDescriptorPool(_logicalDevice, ref _createInfo, null, _dpPtr) != Result.Success)
                {
                    throw new Exception("Failed to create descriptor pool");
                }
            }
        }

        private void CreateDrawingDescriptorPool()
        {
            var _poolSizes = new DescriptorPoolSize[]
            {
                new DescriptorPoolSize()
                {
                    Type = DescriptorType.StorageImage,
                    DescriptorCount = 50000
                },
                new DescriptorPoolSize()
                {
                    Type = DescriptorType.UniformBuffer,
                    DescriptorCount = (uint)_swapimageCount
                }
            };
            fixed (DescriptorPoolSize* _poolSizePtr = _poolSizes)
            fixed (DescriptorPool* _dpPtr = &drawingDescriptorPool)
            {
                DescriptorPoolCreateInfo _createInfo = new DescriptorPoolCreateInfo()
                {
                    SType = StructureType.DescriptorPoolCreateInfo,
                    PoolSizeCount = (uint)_poolSizes.Length,
                    PPoolSizes = _poolSizePtr,
                    MaxSets = (uint)_swapimageCount,
                    Flags = DescriptorPoolCreateFlags.FreeDescriptorSetBit
                };
                if (_vulkan.CreateDescriptorPool(_logicalDevice, ref _createInfo, null, _dpPtr) != Result.Success)
                {
                    throw new Exception("Failed to create descriptor pool");
                }
            }
        }

        private void CreateLayerComputeDescriptorPool()
        {
            var _poolSizes = new DescriptorPoolSize[]
            {
                new DescriptorPoolSize()
                {
                    Type = DescriptorType.StorageImage,
                    DescriptorCount = 50000
                },
                new DescriptorPoolSize()
                {
                    Type = DescriptorType.StorageBuffer,
                    DescriptorCount = (uint)_swapimageCount
                }
            };
            fixed (DescriptorPoolSize* _poolSizePtr = _poolSizes)
            fixed (DescriptorPool* _dpPtr = &layerComputeDescriptorPool)
            {
                DescriptorPoolCreateInfo _createInfo = new DescriptorPoolCreateInfo()
                {
                    SType = StructureType.DescriptorPoolCreateInfo,
                    PoolSizeCount = (uint)_poolSizes.Length,
                    PPoolSizes = _poolSizePtr,
                    MaxSets = (uint)_swapimageCount,
                    Flags = DescriptorPoolCreateFlags.FreeDescriptorSetBit
                };
                if (_vulkan.CreateDescriptorPool(_logicalDevice, ref _createInfo, null, _dpPtr) != Result.Success)
                {
                    throw new Exception("Failed to create descriptor pool");
                }
            }
        }

        private void CreatePhosphorusDescriptorPool()
        {
            var _poolSizes = new DescriptorPoolSize[]
            {
                new DescriptorPoolSize()
                {
                    Type = DescriptorType.StorageImage,
                    DescriptorCount = 50000
                },
                new DescriptorPoolSize()
                {
                    Type = DescriptorType.UniformBuffer,
                    DescriptorCount = 3
                }
            };
            fixed (DescriptorPoolSize* _poolSizePtr = _poolSizes)
            fixed (DescriptorPool* _dpPtr = &phosphrousDescriptorPool)
            {
                DescriptorPoolCreateInfo _createInfo = new DescriptorPoolCreateInfo()
                {
                    SType = StructureType.DescriptorPoolCreateInfo,
                    PoolSizeCount = (uint)_poolSizes.Length,
                    PPoolSizes = _poolSizePtr,
                    MaxSets = (uint)_swapimageCount,
                    Flags = DescriptorPoolCreateFlags.FreeDescriptorSetBit
                };
                if (_vulkan.CreateDescriptorPool(_logicalDevice, ref _createInfo, null, _dpPtr) != Result.Success)
                {
                    throw new Exception("Failed to create descriptor pool");
                }
            }
        }

        private void UpdateDescriptorSet()
        {
            DescriptorSetLayout[] localLayout = new DescriptorSetLayout[_swapimageCount];
            Array.Fill(localLayout, _descriptorSetLayout);
            DescriptorSetLayout[] localLayout2 = new DescriptorSetLayout[_swapimageCount];
            Array.Fill(localLayout2, probeDescriptorSetLayout);
            DescriptorSetLayout[] localLayout3 = new DescriptorSetLayout[_swapimageCount];
            Array.Fill(localLayout3, drawingDescriptorSetLayout);
            DescriptorSetLayout[] localLayout4 = new DescriptorSetLayout[_swapimageCount];
            Array.Fill(localLayout4, layerComputeDescriptorSetLayout);
            DescriptorSetLayout[] localLayout5 = new DescriptorSetLayout[_swapimageCount];
            Array.Fill(localLayout5, phosphrousDescriptorSetLayout);

            fixed (DescriptorSetLayout* _layoutsPtr = localLayout)
            {
                uint bufferCount = (uint)cascadeCount;
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
                        DescriptorPool = _descriptorPool,
                        DescriptorSetCount = (uint)_swapimageCount,
                        PSetLayouts = _layoutsPtr,
                        PNext = &_variableDSCount
                    };

                    _descriptorSets = new DescriptorSet[_swapimageCount];
                    fixed (DescriptorSet* _descriptorSetsPtr = _descriptorSets)
                    {
                        Result r = _vulkan.AllocateDescriptorSets(_logicalDevice, ref _allocateInfo, _descriptorSetsPtr);
                        if (r != Result.Success)
                        {
                            throw new Exception("Failed to allocate descriptor set with error code: " + r);
                        }
                    }
                }
            }

            fixed (DescriptorSetLayout* _layoutsPtr = localLayout2)
            {
                uint bufferCount = (uint)cascadeCount;
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
                        DescriptorPool = probeDescriptorPool,
                        DescriptorSetCount = (uint)_swapimageCount,
                        PSetLayouts = _layoutsPtr,
                        PNext = &_variableDSCount
                    };

                    probeDescriptorSets = new DescriptorSet[_swapimageCount];
                    fixed (DescriptorSet* _descriptorSetsPtr = probeDescriptorSets)
                    {
                        Result r = _vulkan.AllocateDescriptorSets(_logicalDevice, ref _allocateInfo, _descriptorSetsPtr);
                        if (r != Result.Success)
                        {
                            throw new Exception("Failed to allocate descriptor set with error code: " + r);
                        }
                    }
                }
            }

            fixed (DescriptorSetLayout* _layoutsPtr = localLayout3)
            {
                uint bufferCount = (uint)cascadeCount;
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
                        DescriptorPool = drawingDescriptorPool,
                        DescriptorSetCount = (uint)_swapimageCount,
                        PSetLayouts = _layoutsPtr,
                        PNext = &_variableDSCount
                    };

                    drawingDescriptorSets = new DescriptorSet[_swapimageCount];
                    fixed (DescriptorSet* _descriptorSetsPtr = drawingDescriptorSets)
                    {
                        Result r = _vulkan.AllocateDescriptorSets(_logicalDevice, ref _allocateInfo, _descriptorSetsPtr);
                        if (r != Result.Success)
                        {
                            throw new Exception("Failed to allocate descriptor set with error code: " + r);
                        }
                    }
                }
            }

            fixed (DescriptorSetLayout* _layoutsPtr = localLayout4)
            {
                uint bufferCount = (uint)cascadeCount;
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
                        DescriptorPool = layerComputeDescriptorPool,
                        DescriptorSetCount = (uint)_swapimageCount,
                        PSetLayouts = _layoutsPtr,
                        PNext = &_variableDSCount
                    };

                    layerComputeDescriptorSets = new DescriptorSet[_swapimageCount];
                    fixed (DescriptorSet* _descriptorSetsPtr = layerComputeDescriptorSets)
                    {
                        Result r = _vulkan.AllocateDescriptorSets(_logicalDevice, ref _allocateInfo, _descriptorSetsPtr);
                        if (r != Result.Success)
                        {
                            throw new Exception("Failed to allocate descriptor set with error code: " + r);
                        }
                    }
                }
            }

            fixed (DescriptorSetLayout* _layoutsPtr = localLayout5)
            {
                uint bufferCount = (uint)cascadeCount;
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
                        DescriptorPool = phosphrousDescriptorPool,
                        DescriptorSetCount = (uint)_swapimageCount,
                        PSetLayouts = _layoutsPtr,
                        PNext = &_variableDSCount
                    };

                    phosphrousDescriptorSets = new DescriptorSet[_swapimageCount];
                    fixed (DescriptorSet* _descriptorSetsPtr = phosphrousDescriptorSets)
                    {
                        Result r = _vulkan.AllocateDescriptorSets(_logicalDevice, ref _allocateInfo, _descriptorSetsPtr);
                        if (r != Result.Success)
                        {
                            throw new Exception("Failed to allocate descriptor set with error code: " + r);
                        }
                    }
                }
            }

            for (int i = 0; i < _swapimageCount; i++)
            {
                // probes
                DescriptorImageInfo lightsImageInfo = new DescriptorImageInfo
                {
                    ImageView = lightsImageView,
                    ImageLayout = ImageLayout.General // Compute shader writes to it
                };
                DescriptorBufferInfo probeLayerInfo = new DescriptorBufferInfo()
                {
                    Buffer = probesB,
                    Offset = 0,
                    Range = (ulong)(sizeof(ProbeLayer) * cascadeCount)
                };
                DescriptorBufferInfo[] probePositionInfos = new DescriptorBufferInfo[cascadeCount];
                DescriptorImageInfo[] texels = new DescriptorImageInfo[cascadeCount];
                for (int k = 0; k < cascadeCount; k++)
                {
                    probePositionInfos[k] = new DescriptorBufferInfo()
                    {
                        Buffer = probesPostionB[k],
                        Offset = 0,
                        Range = (ulong)(sizeof(Vector2D<int>) * pos[k].Length)
                    };
                    texels[k] = new DescriptorImageInfo()
                    {
                        ImageView = probesImageView[k],
                        ImageLayout = ImageLayout.General // Compute shader writes to it
                    };
                }

                DescriptorImageInfo[] layersColor = new DescriptorImageInfo[_entitiesToRender.Count];
                DescriptorImageInfo[] layersLight = new DescriptorImageInfo[_entitiesToRender.Count];
                DescriptorImageInfo[] layersFinal = new DescriptorImageInfo[_entitiesToRender.Count];
                for (int k = 0; k < _entitiesToRender.Count; k++)
                {
                    Layer l = _entitiesToRender[k] as Layer;
                    layersColor[k] = new DescriptorImageInfo()
                    {
                        ImageView = l.layerImageView,
                        ImageLayout = ImageLayout.General
                    };
                    layersLight[k] = new DescriptorImageInfo()
                    {
                        ImageView = l.layerLightsView,
                        ImageLayout = ImageLayout.General
                    };
                    layersFinal[k] = new DescriptorImageInfo()
                    {
                        ImageView = l.layerFView,
                        ImageLayout = ImageLayout.General
                    };
                }

                fixed (DescriptorImageInfo* layeredEmitPtr = layersColor)
                fixed (DescriptorImageInfo* layeredLightsPtr = layersLight)
                fixed (DescriptorImageInfo* texelsPtr = texels)
                fixed (DescriptorBufferInfo* probePtr = probePositionInfos)
                {
                    var probeWrites = new WriteDescriptorSet[]
                    {
                        new WriteDescriptorSet
                        {
                            SType = StructureType.WriteDescriptorSet,
                            DstSet = probeDescriptorSets[i],
                            DstBinding = 0,
                            DescriptorCount = 1,
                            DescriptorType = DescriptorType.StorageImage,
                            PImageInfo = &lightsImageInfo
                        },
                        new WriteDescriptorSet
                        {
                            SType = StructureType.WriteDescriptorSet,
                            DstSet = probeDescriptorSets[i],
                            DstBinding = 1,
                            DescriptorCount = 1,
                            DescriptorType = DescriptorType.StorageBuffer,
                            PBufferInfo = &probeLayerInfo
                        },
                        new WriteDescriptorSet
                        {
                            SType = StructureType.WriteDescriptorSet,
                            DstSet = probeDescriptorSets[i],
                            DstBinding = 2,
                            DescriptorCount = (uint)cascadeCount,
                            DescriptorType = DescriptorType.StorageImage,
                            PImageInfo = texelsPtr,
                            DstArrayElement = 0
                        },
                        new WriteDescriptorSet
                        {
                            SType = StructureType.WriteDescriptorSet,
                            DstSet = probeDescriptorSets[i],
                            DstBinding = 3,
                            DescriptorCount = (uint)cascadeCount,
                            DescriptorType = DescriptorType.StorageBuffer,
                            PBufferInfo = probePtr,
                            DstArrayElement = 0
                        },
                        new WriteDescriptorSet
                        {
                            SType = StructureType.WriteDescriptorSet,
                            DstSet = probeDescriptorSets[i],
                            DstBinding = 4,
                            DescriptorCount = (uint)_entitiesToRender.Count,
                            DescriptorType = DescriptorType.StorageImage,
                            PImageInfo = layeredLightsPtr,
                            DstArrayElement = 0
                        },
                        new WriteDescriptorSet
                        {
                            SType = StructureType.WriteDescriptorSet,
                            DstSet = probeDescriptorSets[i],
                            DstBinding = 5,
                            DescriptorCount = (uint)_entitiesToRender.Count,
                            DescriptorType = DescriptorType.StorageImage,
                            PImageInfo = layeredEmitPtr,
                            DstArrayElement = 0
                        }
                    };

                    fixed (WriteDescriptorSet* _descPtr = probeWrites)
                    {
                        _vulkan!.UpdateDescriptorSets(_logicalDevice, (uint)probeWrites.Length, _descPtr, 0, null);
                    }
                }
                //-----------------------------------------------------------------------------------------------
                // framebuffer
                DescriptorImageInfo imageInfo = new DescriptorImageInfo
                {
                    ImageView = storageImageView[i],
                    ImageLayout = ImageLayout.General // Compute shader writes to it
                };
                DescriptorBufferInfo worldDataInfo = new DescriptorBufferInfo()
                {
                    Buffer = mousePosBuffer,
                    Offset = 0,
                    Range = (ulong)sizeof(WorldData)
                };
                DescriptorBufferInfo phosphorusDataInfo = new DescriptorBufferInfo()
                {
                    Buffer = phosphorusDataBuffer,
                    Offset = 0,
                    Range = (ulong)sizeof(PhosphorusData)
                };

                fixed (DescriptorImageInfo* layersClrPtr = layersColor)
                fixed (DescriptorImageInfo* layersFinalPtr = layersFinal)
                {
                    var writeDescriptorSets = new WriteDescriptorSet[]
                    {
                        new WriteDescriptorSet
                        {
                            SType = StructureType.WriteDescriptorSet,
                            DstSet = _descriptorSets[i],
                            DstBinding = 0,
                            DescriptorCount = 1,
                            DescriptorType = DescriptorType.StorageImage,
                            PImageInfo = &imageInfo
                        },
                        new WriteDescriptorSet
                        {
                            SType = StructureType.WriteDescriptorSet,
                            DstSet = _descriptorSets[i],
                            DstBinding = 1,
                            DescriptorCount = 1,
                            DescriptorType = DescriptorType.StorageBuffer,
                            PBufferInfo = &probeLayerInfo
                        },
                        new WriteDescriptorSet
                        {
                            SType = StructureType.WriteDescriptorSet,
                            DstSet = _descriptorSets[i],
                            DstBinding = 2,
                            DescriptorCount = (uint)_entitiesToRender.Count,
                            DescriptorType = DescriptorType.StorageImage,
                            PImageInfo = layersFinalPtr,
                            DstArrayElement = 0
                        },
                        new WriteDescriptorSet
                        {
                            SType = StructureType.WriteDescriptorSet,
                            DstSet = _descriptorSets[i],
                            DstBinding = 3,
                            DescriptorCount = (uint)_entitiesToRender.Count,
                            DescriptorType = DescriptorType.StorageImage,
                            PImageInfo = layersClrPtr,
                            DstArrayElement = 0
                        }
                    };

                    fixed (WriteDescriptorSet* _descPtr = writeDescriptorSets)
                    {
                        _vulkan!.UpdateDescriptorSets(_logicalDevice, (uint)writeDescriptorSets.Length, _descPtr, 0, null);
                    }
                }
                //-----------------------------------------------------------------------------------------------
                // drawing
                fixed (DescriptorImageInfo* layersLightPtr = layersLight)
                fixed (DescriptorImageInfo* layersClrPtr = layersColor)
                {
                    var writeDescriptorSets = new WriteDescriptorSet[]
                    {
                        new WriteDescriptorSet
                        {
                            SType = StructureType.WriteDescriptorSet,
                            DstSet = drawingDescriptorSets[i],
                            DstBinding = 0,
                            DescriptorCount = 1,
                            DescriptorType = DescriptorType.UniformBuffer,
                            PBufferInfo = &worldDataInfo
                        },
                        new WriteDescriptorSet
                        {
                            SType = StructureType.WriteDescriptorSet,
                            DstSet = drawingDescriptorSets[i],
                            DstBinding = 1,
                            DescriptorCount = (uint)_entitiesToRender.Count,
                            DescriptorType = DescriptorType.StorageImage,
                            PImageInfo = layersClrPtr,
                            DstArrayElement = 0
                        },
                        new WriteDescriptorSet
                        {
                            SType = StructureType.WriteDescriptorSet,
                            DstSet = drawingDescriptorSets[i],
                            DstBinding = 2,
                            DescriptorCount = (uint)_entitiesToRender.Count,
                            DescriptorType = DescriptorType.StorageImage,
                            PImageInfo = layersLightPtr,
                            DstArrayElement = 0
                        }
                    };
                    fixed (WriteDescriptorSet* _descPtr = writeDescriptorSets)
                    {
                        _vulkan!.UpdateDescriptorSets(_logicalDevice, (uint)writeDescriptorSets.Length, _descPtr, 0, null);
                    }
                }

                //-----------------------------------------------------------------------------------------------
                // phosphorus compute
                fixed (DescriptorImageInfo* texelsPtr = texels) 
                fixed (DescriptorImageInfo* layersClrPtr = layersColor)
                {
                    var writeDescriptorSets = new WriteDescriptorSet[]
                    {
                        new WriteDescriptorSet
                        {
                            SType = StructureType.WriteDescriptorSet,
                            DstSet = phosphrousDescriptorSets[i],
                            DstBinding = 0,
                            DescriptorCount = (uint)_entitiesToRender.Count,
                            DescriptorType = DescriptorType.StorageImage,
                            PImageInfo = layersClrPtr,
                            DstArrayElement = 0
                        },
                        new WriteDescriptorSet
                        {
                            SType = StructureType.WriteDescriptorSet,
                            DstSet = phosphrousDescriptorSets[i],
                            DstBinding = 1,
                            DescriptorCount = (uint)cascadeCount,
                            DescriptorType = DescriptorType.StorageImage,
                            PImageInfo = texelsPtr,
                            DstArrayElement = 0
                        },
                        new WriteDescriptorSet
                        {
                            SType = StructureType.WriteDescriptorSet,
                            DstSet = phosphrousDescriptorSets[i],
                            DstBinding = 2,
                            DescriptorCount = 1,
                            DescriptorType = DescriptorType.UniformBuffer,
                            PBufferInfo = &phosphorusDataInfo
                        },
                        new WriteDescriptorSet
                        {
                            SType = StructureType.WriteDescriptorSet,
                            DstSet = phosphrousDescriptorSets[i],
                            DstBinding = 3,
                            DescriptorCount = 1,
                            DescriptorType = DescriptorType.StorageBuffer,
                            PBufferInfo = &probeLayerInfo
                        }
                    };
                    fixed (WriteDescriptorSet* _descPtr = writeDescriptorSets)
                    {
                        _vulkan!.UpdateDescriptorSets(_logicalDevice, (uint)writeDescriptorSets.Length, _descPtr, 0, null);
                    }
                }

                //-----------------------------------------------------------------------------------------------
                // layer compute
                fixed (DescriptorImageInfo* texelsPtr = texels) 
                fixed (DescriptorImageInfo* layersFinalPtr = layersFinal)
                fixed (DescriptorImageInfo* layersClrPtr = layersColor)
                {
                    var writeDescriptorSets = new WriteDescriptorSet[]
                    {
                        new WriteDescriptorSet
                        {
                            SType = StructureType.WriteDescriptorSet,
                            DstSet = layerComputeDescriptorSets[i],
                            DstBinding = 0,
                            DescriptorCount = 1,
                            DescriptorType = DescriptorType.StorageBuffer,
                            PBufferInfo = &probeLayerInfo
                        },
                        new WriteDescriptorSet
                        {
                            SType = StructureType.WriteDescriptorSet,
                            DstSet = layerComputeDescriptorSets[i],
                            DstBinding = 1,
                            DescriptorCount = (uint)_entitiesToRender.Count,
                            DescriptorType = DescriptorType.StorageImage,
                            PImageInfo = layersClrPtr,
                            DstArrayElement = 0
                        },
                        new WriteDescriptorSet
                        {
                            SType = StructureType.WriteDescriptorSet,
                            DstSet = layerComputeDescriptorSets[i],
                            DstBinding = 2,
                            DescriptorCount = (uint)_entitiesToRender.Count,
                            DescriptorType = DescriptorType.StorageImage,
                            PImageInfo = layersFinalPtr,
                            DstArrayElement = 0
                        },
                        new WriteDescriptorSet
                        {
                            SType = StructureType.WriteDescriptorSet,
                            DstSet = layerComputeDescriptorSets[i],
                            DstBinding = 3,
                            DescriptorCount = (uint)cascadeCount,
                            DescriptorType = DescriptorType.StorageImage,
                            PImageInfo = texelsPtr,
                            DstArrayElement = 0
                        }
                    };
                    fixed (WriteDescriptorSet* _descPtr = writeDescriptorSets)
                    {
                        _vulkan!.UpdateDescriptorSets(_logicalDevice, (uint)writeDescriptorSets.Length, _descPtr, 0, null);
                    }
                }
            }
        }

        internal override void CreateCommandBuffers()
        {
            ImageSubresourceRange _sr = new()
            {
                AspectMask = ImageAspectFlags.ColorBit,
                BaseArrayLayer = 0,
                BaseMipLevel = 0,
                LayerCount = 1,
                LevelCount = 1
            };

            _commandBuffer = new CommandBuffer[_swapimageCount];
            CommandBufferAllocateInfo allocInfo = new CommandBufferAllocateInfo
            {
                SType = StructureType.CommandBufferAllocateInfo,
                Level = CommandBufferLevel.Primary,
                CommandPool = _commandPool,
                CommandBufferCount = (uint)_swapimageCount
            };
            _vulkan.AllocateCommandBuffers(_logicalDevice, &allocInfo, _commandBuffer);
            CommandBufferBeginInfo beginInfo = new CommandBufferBeginInfo { SType = StructureType.CommandBufferBeginInfo };
            for (int i = 0; i < _swapimageCount; i++)
            {
                _vulkan.BeginCommandBuffer(_commandBuffer[i], &beginInfo);
                // drawing
                _vulkan.CmdBindPipeline(_commandBuffer[i], PipelineBindPoint.Compute, drawingPipeline);
                _vulkan.CmdBindDescriptorSets(_commandBuffer[i], PipelineBindPoint.Compute, drawingPipelineLayout, 0, 1, ref drawingDescriptorSets[i], 0, null);
                _vulkan.CmdDispatch(_commandBuffer[i], 1, 1, 1);

                for (int layer = 0; layer < _entitiesToRender.Count; layer++)
                {
                    // probes
                    _vulkan.CmdBindPipeline(_commandBuffer[i], PipelineBindPoint.Compute, probePipeline);
                    _vulkan.CmdBindDescriptorSets(_commandBuffer[i], PipelineBindPoint.Compute, probePipelineLayout, 0, 1, ref probeDescriptorSets[i], 0, null);

                    int layerIndex = layer;
                    _vulkan.CmdPushConstants(_commandBuffer[i], probePipelineLayout, ShaderStageFlags.ComputeBit, 0, sizeof(int), &layerIndex);

                    int probeCount = pos[0].Length;
                    int rayCount = (int)Math.Pow(4, cascadeCount);
                    _vulkan.CmdDispatch(_commandBuffer[i], (uint)probeCount, (uint)rayCount, (uint)cascadeCount);

                    // render each layer out
                    _vulkan.CmdBindPipeline(_commandBuffer[i], PipelineBindPoint.Compute, layerComputePipeline);
                    _vulkan.CmdBindDescriptorSets(_commandBuffer[i], PipelineBindPoint.Compute, layerComputePipelineLayout, 0, 1, ref layerComputeDescriptorSets[i], 0, null);
                    _vulkan.CmdPushConstants(_commandBuffer[i], layerComputePipelineLayout, ShaderStageFlags.ComputeBit, 0, sizeof(int), &layerIndex);

                    _vulkan.CmdDispatch(_commandBuffer[i], _extent.Width / 16, _extent.Height / 16, 1);

                    // phosphorus update
                    Layer l = _entitiesToRender[layer] as Layer;
                    if (l.isPhosphorus)
                    {
                        _vulkan.CmdBindPipeline(_commandBuffer[i], PipelineBindPoint.Compute, phosphrousPipeline);
                        _vulkan.CmdBindDescriptorSets(_commandBuffer[i], PipelineBindPoint.Compute, phosphrousPipelineLayout, 0, 1, ref phosphrousDescriptorSets[i], 0, null);
                        _vulkan.CmdPushConstants(_commandBuffer[i], phosphrousPipelineLayout, ShaderStageFlags.ComputeBit, 0, sizeof(int), &layerIndex);

                        _vulkan.CmdDispatch(_commandBuffer[i], _extent.Width / 16, _extent.Height / 16, 1);
                    }
                }

                // final composite
                _vulkan.CmdBindPipeline(_commandBuffer[i], PipelineBindPoint.Compute, computePipeline);
                _vulkan.CmdBindDescriptorSets(_commandBuffer[i], PipelineBindPoint.Compute, computePipelineLayout, 0, 1, ref _descriptorSets[i], 0, null);

                int layerCount = _entitiesToRender.Count;
                _vulkan.CmdPushConstants(_commandBuffer[i], computePipelineLayout, ShaderStageFlags.ComputeBit, 0, sizeof(int), &layerCount);
                
                _vulkan.CmdDispatch(_commandBuffer[i], _extent.Width / 16, _extent.Height / 16, 1);

                SetImageLayout(ref _commandBuffer[i], ref _swapchain._swapchainImages[i], ImageLayout.Undefined, ImageLayout.TransferDstOptimal, _sr);
                SetImageLayout(ref _commandBuffer[i], ref storageImage[i], ImageLayout.General, ImageLayout.TransferSrcOptimal, _sr);
                ImageCopy _ic = new ImageCopy()
                {
                    SrcSubresource =
                    {
                        AspectMask = ImageAspectFlags.ColorBit,
                        BaseArrayLayer = 0,
                        LayerCount = 1,
                        MipLevel = 0
                    },
                    SrcOffset = { X = 0, Y = 0, Z = 0 },
                    DstSubresource =
                    {
                        AspectMask = ImageAspectFlags.ColorBit,
                        MipLevel = 0,
                        LayerCount = 1,
                        BaseArrayLayer = 0
                    },
                    DstOffset = { X = 0, Y = 0, Z = 0 },
                    Extent =
                    {
                        Height = _extent.Height , Width = _extent.Width, Depth = 1
                    }
                };
                _vulkan.CmdCopyImage(_commandBuffer[i], storageImage[i], ImageLayout.TransferSrcOptimal, _swapchain._swapchainImages[i], ImageLayout.TransferDstOptimal, 1, &_ic);
                SetImageLayout(ref _commandBuffer[i], ref _swapchain._swapchainImages[i], ImageLayout.TransferDstOptimal, ImageLayout.PresentSrcKhr, _sr);
                SetImageLayout(ref _commandBuffer[i], ref storageImage[i], ImageLayout.TransferSrcOptimal, ImageLayout.General, _sr);

                if (_vulkan.EndCommandBuffer(_commandBuffer[i]) != Result.Success)
                {
                    throw new Exception("Failed to record command buffer");
                }
            }
        }

        internal override void Draw()
        {
            Result r;
            //_camera.ProcessKeyboard();
            r = _vulkan.WaitForFences(_logicalDevice, 1, ref _fencesInFlight[_currentFrame], true, ulong.MaxValue);
            uint _imageIndex = 0;
            r = _swapchain._driverSwapchain.AcquireNextImage(_logicalDevice, _swapchain._swapchainKHR, ulong.MaxValue, _imageAvailableSemaphores[_currentFrame], default, ref _imageIndex);

            if (r == Result.ErrorOutOfDateKhr)
            {
                //RecreateSwapchain();
                return;
            }
            else if (r != Result.Success && r != Result.SuboptimalKhr)
            {
                throw new Exception("Failed to acquire swapchain image");
            }

            // update deltattime here
            //DateTime now = DateTime.Now;
            //pd.deltaTime = (float)(now - lastFrameTime).TotalSeconds;
            //AVulkanBufferHandler.UpdateBuffer(ref pd, ref phosphorusDataBuffer, ref phosphorusDM, BufferUsageFlags.UniformBufferBit);
            //lastFrameTime = now;
            //_camera.UpdateCameraMatrix(_extent, _imageIndex);
            //-----------------------------------
            if (_imagesInFlight[_imageIndex].Handle != default)
            {
                r = _vulkan.WaitForFences(_logicalDevice, 1, ref _imagesInFlight[_imageIndex], true, ulong.MaxValue);
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
                throw new Exception("command buffer is not recorded or invalid:" + r);
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
            if (r == Result.ErrorOutOfDateKhr || r == Result.SuboptimalKhr || _glWindow._frameBufferResized)
            {
                _glWindow._frameBufferResized = false;
                //RecreateSwapchain();
            }
            else if (r != Result.Success)
            {
                throw new Exception("Failed to present swap chain image");
            }
            _currentFrame = (_currentFrame + 1) % MAX_FRAMES_IN_FLIGHT;
        }

        private void TransitionImageLayout(CommandBuffer commandBuffer, Image image, ImageLayout oldLayout, ImageLayout newLayout)
        {
            ImageMemoryBarrier barrier = new ImageMemoryBarrier
            {
                SType = StructureType.ImageMemoryBarrier,
                OldLayout = oldLayout,
                NewLayout = newLayout,
                SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
                DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
                Image = image,
                SubresourceRange = new ImageSubresourceRange
                {
                    AspectMask = ImageAspectFlags.ColorBit,
                    BaseMipLevel = 0,
                    LevelCount = 1,
                    BaseArrayLayer = 0,
                    LayerCount = 1
                }
            };

            PipelineStageFlags sourceStage;
            PipelineStageFlags destinationStage;


            if (oldLayout == ImageLayout.Undefined && newLayout == ImageLayout.General)
            {
                barrier.SrcAccessMask = 0;
                barrier.DstAccessMask = AccessFlags.ShaderWriteBit;
                sourceStage = PipelineStageFlags.TopOfPipeBit;
                destinationStage = PipelineStageFlags.ComputeShaderBit;
            }
            else if (oldLayout == ImageLayout.General && newLayout == ImageLayout.TransferSrcOptimal)
            {
                barrier.SrcAccessMask = AccessFlags.ShaderWriteBit;
                barrier.DstAccessMask = AccessFlags.TransferReadBit;
                sourceStage = PipelineStageFlags.ComputeShaderBit;
                destinationStage = PipelineStageFlags.TransferBit;
            }
            else if (oldLayout == ImageLayout.PresentSrcKhr && newLayout == ImageLayout.TransferDstOptimal)
            {
                barrier.SrcAccessMask = AccessFlags.None;
                barrier.DstAccessMask = AccessFlags.TransferWriteBit;
                sourceStage = PipelineStageFlags.TopOfPipeBit;
                destinationStage = PipelineStageFlags.TransferBit;
            }
            else if (oldLayout == ImageLayout.TransferDstOptimal && newLayout == ImageLayout.PresentSrcKhr)
            {
                barrier.SrcAccessMask = AccessFlags.TransferWriteBit;
                barrier.DstAccessMask = AccessFlags.None;
                sourceStage = PipelineStageFlags.TransferBit;
                destinationStage = PipelineStageFlags.BottomOfPipeBit;
            }
            else if (oldLayout == ImageLayout.General && newLayout == ImageLayout.PresentSrcKhr)
            {
                barrier.SrcAccessMask = AccessFlags.ShaderWriteBit;
                barrier.DstAccessMask = AccessFlags.None;
                sourceStage = PipelineStageFlags.ComputeShaderBit;
                destinationStage = PipelineStageFlags.BottomOfPipeBit;
            }
            else
            {
                throw new Exception($"Unsupported layout transition: {oldLayout} -> {newLayout}");
            }
            _vulkan.CmdPipelineBarrier(commandBuffer, sourceStage, destinationStage, 0, 0, null, 0, null, 1, &barrier);
        }

        private void SetImageLayout(ref CommandBuffer _cBuffer, ref Image _image, ImageLayout _oldLayout, ImageLayout _newLayout, ImageSubresourceRange _subresource)
        {
            ImageMemoryBarrier _barrier = new()
            {
                SType = StructureType.ImageMemoryBarrier,
                OldLayout = _oldLayout,
                NewLayout = _newLayout,
                SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
                DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
                Image = _image,
                SubresourceRange = _subresource
            };
            PipelineStageFlags sourceStage;
            PipelineStageFlags destinationStage;

            if (_oldLayout == ImageLayout.Undefined && _newLayout == ImageLayout.TransferDstOptimal)
            {
                _barrier.SrcAccessMask = 0;
                _barrier.DstAccessMask = AccessFlags.TransferWriteBit;

                sourceStage = PipelineStageFlags.TopOfPipeBit;
                destinationStage = PipelineStageFlags.TransferBit;
            }
            else if (_oldLayout == ImageLayout.TransferDstOptimal && _newLayout == ImageLayout.PresentSrcKhr)
            {
                _barrier.SrcAccessMask = AccessFlags.TransferWriteBit;
                _barrier.DstAccessMask = AccessFlags.MemoryReadBit;

                sourceStage = PipelineStageFlags.TransferBit;
                destinationStage = PipelineStageFlags.BottomOfPipeBit;
            }
            else if (_oldLayout == ImageLayout.TransferSrcOptimal && _newLayout == ImageLayout.General)
            {
                _barrier.SrcAccessMask = AccessFlags.TransferReadBit;
                _barrier.DstAccessMask = AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit;

                sourceStage = PipelineStageFlags.TransferBit;
                destinationStage = PipelineStageFlags.ComputeShaderBit;
            }
            else if (_oldLayout == ImageLayout.General && _newLayout == ImageLayout.TransferSrcOptimal)
            {
                _barrier.SrcAccessMask = AccessFlags.ShaderWriteBit;
                _barrier.DstAccessMask = AccessFlags.TransferReadBit;

                sourceStage = PipelineStageFlags.ComputeShaderBit;
                destinationStage = PipelineStageFlags.TransferBit;
            }
            else
            {
                throw new Exception("unsupported layout transition!");
            }
            _vulkan!.CmdPipelineBarrier(_cBuffer, sourceStage, destinationStage, 0, 0, null, 0, null, 1, &_barrier);
        }

        private ShaderModule LoadShader(string path)
        {
            byte[] code = File.ReadAllBytes(path);
            fixed (byte* codePtr = code)
            {
                ShaderModuleCreateInfo createInfo = new ShaderModuleCreateInfo
                {
                    SType = StructureType.ShaderModuleCreateInfo,
                    CodeSize = (nuint)code.Length,
                    PCode = (uint*)codePtr
                };

                ShaderModule shaderModule;
                if (_vulkan.CreateShaderModule(_logicalDevice, &createInfo, null, &shaderModule) != Result.Success)
                    throw new Exception("Failed to create shader module");

                return shaderModule;
            }
        }
    }
}