using ArctisAurora.Core.Rendering.Helpers;
using ArctisAurora.EngineWork.Rendering.Helpers;
using Silk.NET.Vulkan;
using Image = Silk.NET.Vulkan.Image;
using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace ArctisAurora.EngineWork.Rendering.Modules
{
    public enum ERendererStage
    {
        Game, UI, PostProcessing
    }

    internal class FrameResources
    {
        internal DescriptorPool pool;
        internal DescriptorSet[] sets;  // one per set layout
    }

    public unsafe abstract class RenderingModule
    {
        // type
        internal abstract ERendererTypes rendererType { get; }
        internal abstract ERendererStage RendererStage { get; }

        // features
        internal abstract PhysicalDeviceFeatures features { get; }
        internal abstract PhysicalDeviceVulkan12Features features12 { get; }

        // rendering
        internal Pipeline pipeline;
        internal PipelineLayout pipelineLayout;
        internal RenderPass renderPass;

        internal Framebuffer[] frameBuffers;

        // commands
        public Queue graphicsQueue;
        public CommandPool moduleCommandPool;
        internal CommandBuffer[] commandBuffers;
        public bool[] isDirty = { true, true, true };
        public Semaphore[] moduleFinishedSemaphores;

        // descriptors
        internal abstract List<List<DescriptorType>> descriptorTypes { get; }
        internal abstract List<List<ShaderStageFlags>> shaderStages { get; }
        internal abstract DescriptorBindingFlags[][] descriptorBindingFlags { get; }
        internal abstract int variableSetCount { get; }


        internal DescriptorSetLayout[] descriptorSetLayouts;
        internal DescriptorPoolSize[] descriptorPoolSizes;
        internal FrameResources[] frameResources;  // one per MAX_FRAMES_IN_FLIGHT
        internal abstract uint[][] descriptorMaxCounts { get; }

        // rendered result
        public Image[] outputImages;
        public ImageView[] outputImageViews;
        public DeviceMemory[] imageDeviceMemory;
        public Semaphore[] renderFinishedSemaphores;
        public int compositorOrder = 0;

        // others
        internal AuroraCamera camera;


        internal abstract void PrepareObjects();

        internal virtual void RegisterVulkanQueue(QueueAllocator allocator, Vk vk, ref Device device)
        {
            graphicsQueue = allocator.AllocateQueue(vk, device, QueueFlags.GraphicsBit);
        }

        internal abstract void UpdateModule(int currentFrame);

        internal virtual void CreateDescriptorSetLayout()
        {
            uint setCount = (uint)variableSetCount;
            descriptorSetLayouts = new DescriptorSetLayout[variableSetCount];
            for (int set = 0; set < setCount; ++set)
            {
                uint typeCount = (uint)descriptorTypes[set].Count;

                // Validation: variable flag only allowed on last binding
                for (int i = 0; i < (int)typeCount; i++)
                {
                    bool isVariable = descriptorBindingFlags[set][i].HasFlag(DescriptorBindingFlags.VariableDescriptorCountBit);
                    if (isVariable && i != (int)typeCount - 1)
                        throw new Exception($"Set {set} binding {i}: VariableDescriptorCountBit is only allowed on the last binding (binding {typeCount - 1})");
                }

                DescriptorSetLayoutBinding[] bindingList = new DescriptorSetLayoutBinding[typeCount];
                for (int i = 0; i < (int)typeCount; i++)
                {
                    bindingList[i] = new DescriptorSetLayoutBinding()
                    {
                        Binding = (uint)i,
                        DescriptorCount = descriptorMaxCounts[set][i],
                        DescriptorType = descriptorTypes[set][i],
                        PImmutableSamplers = null,
                        StageFlags = shaderStages[set][i]
                    };
                }

                DescriptorSetLayout localLayout;

                fixed (DescriptorBindingFlags* _indexedPtr = descriptorBindingFlags[set])
                fixed (DescriptorSetLayoutBinding* _bindingsPtr = bindingList)
                {
                    DescriptorSetLayoutBindingFlagsCreateInfo _setLayoutBindingFlags = new()
                    {
                        SType = StructureType.DescriptorSetLayoutBindingFlagsCreateInfoExt,
                        BindingCount = typeCount,
                        PBindingFlags = _indexedPtr
                    };

                    DescriptorSetLayoutCreateInfo _layoutCreateInfo = new DescriptorSetLayoutCreateInfo()
                    {
                        SType = StructureType.DescriptorSetLayoutCreateInfo,
                        BindingCount = typeCount,
                        PBindings = _bindingsPtr,
                        PNext = &_setLayoutBindingFlags
                    };
                    if (Renderer.vk.CreateDescriptorSetLayout(Renderer.logicalDevice, ref _layoutCreateInfo, null, &localLayout) != Result.Success)
                    {
                        throw new Exception("Failed to create descriptor set layout");
                    }
                }
                descriptorSetLayouts[set] = localLayout;
            }
        }

        internal abstract void CreateDescriptorPoolSizes(uint swapchainImageCount);

        internal abstract void UpdateDescriptorSets(int currentFrame, int entityCount);

        internal abstract void CreateDescriptorPool(int currentFrame, int entityCount);

        internal virtual void AllocateDescriptorSets(int currentFrame)
        {
            if (frameResources[currentFrame] == null)
                frameResources[currentFrame] = new FrameResources();

            frameResources[currentFrame].sets = new DescriptorSet[variableSetCount];

            for (int set = 0; set < variableSetCount; ++set)
            {
                DescriptorSetLayout layout = descriptorSetLayouts[set];

                int lastBinding = descriptorTypes[set].Count - 1;
                bool hasVariable = descriptorBindingFlags[set][lastBinding]
                    .HasFlag(DescriptorBindingFlags.VariableDescriptorCountBit);

                if (hasVariable)
                {
                    uint actualCount = GetVariableDescriptorCount(set);
                    DescriptorSetVariableDescriptorCountAllocateInfo variableInfo = new()
                    {
                        SType = StructureType.DescriptorSetVariableDescriptorCountAllocateInfo,
                        DescriptorSetCount = 1,
                        PDescriptorCounts = &actualCount
                    };

                    DescriptorSetAllocateInfo allocInfo = new()
                    {
                        SType = StructureType.DescriptorSetAllocateInfo,
                        DescriptorPool = frameResources[currentFrame].pool,
                        DescriptorSetCount = 1,
                        PSetLayouts = &layout,
                        PNext = &variableInfo
                    };
                    fixed (DescriptorSet* setPtr = &frameResources[currentFrame].sets[set])
                    {
                        Result r = Renderer.vk.AllocateDescriptorSets(Renderer.logicalDevice, ref allocInfo, setPtr);
                        if (r != Result.Success)
                            throw new Exception($"Failed to allocate descriptor set {set} for frame {currentFrame} with error: {r}");
                    }
                }
                else
                {
                    DescriptorSetAllocateInfo allocInfo = new()
                    {
                        SType = StructureType.DescriptorSetAllocateInfo,
                        DescriptorPool = frameResources[currentFrame].pool,
                        DescriptorSetCount = 1,
                        PSetLayouts = &layout
                    };
                    fixed (DescriptorSet* setPtr = &frameResources[currentFrame].sets[set])
                    {
                        Result r = Renderer.vk.AllocateDescriptorSets(Renderer.logicalDevice, ref allocInfo, setPtr);
                        if (r != Result.Success)
                            throw new Exception($"Failed to allocate descriptor set {set} for frame {currentFrame} with error: {r}");
                    }
                }
            }
        }

        internal virtual uint GetVariableDescriptorCount(int set)
        {
            throw new Exception($"Module has variable binding in set {set} but doesn't override GetVariableDescriptorCount");
        }

        internal abstract void CreatePipeline();

        internal abstract void CreateModuleFrameBuffers();

        internal abstract void CreateRenderPass();

        internal virtual void CreateOutputImages()
        {
            uint imageceCount = Renderer.swapchainImageCount;
            outputImages = new Image[imageceCount];
            outputImageViews = new ImageView[imageceCount];
            imageDeviceMemory = new DeviceMemory[imageceCount];

            for (int i = 0; i < imageceCount; i++)
            {
                AVulkanBufferHandler.CreateImage(Renderer.vk, ref Renderer.logicalDevice, Renderer.gpu,
                    Engine.window.windowSize.Width, Engine.window.windowSize.Height,
                    Format.R8G8B8A8Unorm,
                    ImageTiling.Optimal,
                    ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.SampledBit,
                    MemoryPropertyFlags.DeviceLocalBit,
                    ref outputImages[i], ref imageDeviceMemory[i]);
                AVulkanBufferHandler.CreateImageView(Renderer.vk, ref Renderer.logicalDevice, ref outputImages[i], ref outputImageViews[i], Format.R8G8B8A8Unorm, ImageAspectFlags.ColorBit);
            }
        }

        internal abstract void PrepareCamera();

        internal abstract void WriteCommandBuffers(int currentFrame);

        internal static byte[] ReadFile(string FileName)
        {
            byte[] contents = File.ReadAllBytes(FileName);
            return contents;
        }

        internal static ShaderModule CreateShaderModule(ref Vk vk, ref Device logicalDevice, byte[] _shaderCode)
        {
            ShaderModuleCreateInfo _createInfo = new ShaderModuleCreateInfo
            {
                SType = StructureType.ShaderModuleCreateInfo,
                CodeSize = (nuint)_shaderCode.Length,
            };
            ShaderModule _shaderModule;

            fixed (byte* _shaderCodePtr = _shaderCode)
            {
                _createInfo.PCode = (uint*)_shaderCodePtr;
                if (vk.CreateShaderModule(logicalDevice, ref _createInfo, null, out _shaderModule) != Result.Success)
                {
                    throw new Exception("Failed to create shader module");
                }
            }
            return _shaderModule;
        }
    }
}