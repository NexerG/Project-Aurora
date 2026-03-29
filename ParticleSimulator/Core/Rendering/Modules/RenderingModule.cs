using ArctisAurora.Core.Rendering.Helpers;
using Silk.NET.Vulkan;
using Image = Silk.NET.Vulkan.Image;
using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace ArctisAurora.EngineWork.Rendering.Modules
{
    public enum ERendererStage
    {
        Game, UI, PostProcessing
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
        internal Framebuffer[] depthFrameBuffers;

        // commands
        //public Queue graphicsQueue;
        //public CommandPool moduleCommandPool;
        internal CommandBuffer[] commandBuffers;

        // descriptors
        internal abstract List<List<DescriptorType>> descriptorTypes { get; }
        internal abstract List<List<ShaderStageFlags>> shaderStages { get; }
        internal abstract DescriptorBindingFlags[][] descriptorBindingFlags { get; }
        internal abstract int variableSetCount { get; }


        internal DescriptorPool descriptorPool;
        internal DescriptorPoolSize[] descriptorPoolSizes;
        internal DescriptorSetLayout[] descriptorSetLayouts;
        internal DescriptorSet[][] descriptorSets;

        internal abstract uint MAX_TEXTURES { get; }
        internal abstract uint MAX_STORAGE_BUFFERS { get; }
        internal abstract uint MAX_UNIFORMS_BUFFERS { get; }

        // rendered result
        public Image[] outputImages;
        public ImageView[] outputImageViews;
        public Semaphore[] renderFinishedSemaphores;

        // Updates
        public bool isDirty = true;

        // others
        internal AuroraCamera camera;


        internal abstract void PrepareObjects();

        internal virtual void RegisterVulkanQueue(QueueAllocator allocator, Vk vk, ref Device device)
        {
            //graphicsQueue = allocator.AllocateQueue(vk, device, QueueFlags.GraphicsBit);
        }

        internal abstract void UpdateModule();

        internal abstract void CreateRenderPass(ref SurfaceFormatKHR format);

        internal virtual void CreateDescriptorSetLayout()
        {
            uint setCount = (uint)variableSetCount;
            descriptorSetLayouts = new DescriptorSetLayout[variableSetCount];
            for (int set = 0; set < setCount; ++set)
            {
                uint typeCount = (uint)descriptorTypes[set].Count;
                uint[] descriptorCount = new uint[typeCount];

                for (int i = 0; i < typeCount; i++)
                {
                    if (descriptorBindingFlags[set][i].HasFlag(DescriptorBindingFlags.VariableDescriptorCountBit))
                    {
                        descriptorCount[i] = MAX_STORAGE_BUFFERS;
                    }
                    else
                    {
                        descriptorCount[i] = 1;
                    }
                }

                DescriptorSetLayoutBinding[] bindingList = new DescriptorSetLayoutBinding[typeCount];
                for (int i = 0; i < typeCount; i++)
                {
                    bindingList[i] = new DescriptorSetLayoutBinding()
                    {
                        Binding = (uint)i,
                        DescriptorCount = descriptorCount[i],
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

        internal abstract void UpdateDescriptorSets();

        internal abstract void CreateDescriptorPool();

        internal abstract void AllocateDescriptorSets();

        internal abstract void CreatePipeline();

        internal abstract void CreateFrameBuffers(ImageView[] swapchainImageViews, ImageView[] swapchainImageViewsDepth);

        internal abstract void PrepareCamera();

        internal abstract void WriteCommandBuffers();

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
