using Silk.NET.Vulkan;
using Windows.ApplicationModel.VoiceCommands;

namespace ArctisAurora.EngineWork.Rendering.Modules
{
    public enum ERendererStage
    {
        Game, UI, PostProcessing
    }


    internal unsafe abstract class RenderingModule
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

        internal CommandBuffer[] commandBuffers;

        // descriptors
        internal abstract List<DescriptorType> descriptorTypes { get; }

        internal abstract List<ShaderStageFlags> shaderStages { get; }

        internal abstract DescriptorBindingFlags[] descriptorBindingFlags { get; }

        internal DescriptorPool descriptorPool;
        internal DescriptorPoolSize[] descriptorPoolSizes;
        internal DescriptorSetLayout descriptorSetLayout;
        internal DescriptorSet[] descriptorSets;

        // others
        internal AuroraCamera camera;


        internal abstract void PrepareObjects();

        internal abstract void UpdateModule();

        internal abstract void CreateRenderPass(ref SurfaceFormatKHR format);

        internal virtual void CreateDescriptorSetLayout()
        {
            uint typeCount = (uint)descriptorTypes.Count;
            uint indexedMaxCount = 50000;
            uint[] descriptorCount = new uint[typeCount];

            for (int i = 0; i < typeCount; i++)
            {
                if (descriptorBindingFlags[i].HasFlag(DescriptorBindingFlags.VariableDescriptorCountBit))
                {
                    descriptorCount[i] = indexedMaxCount;
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
                    DescriptorType = descriptorTypes[i],
                    PImmutableSamplers = null,
                    StageFlags = shaderStages[i]
                };
            }
            fixed (DescriptorBindingFlags* _indexedPtr = descriptorBindingFlags)
            fixed (DescriptorSetLayoutBinding* _bindingsPtr = bindingList)
            fixed (DescriptorSetLayout* _descSetLayoutPtr = &descriptorSetLayout)
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
                if (Renderer.vk.CreateDescriptorSetLayout(Renderer.logicalDevice, ref _layoutCreateInfo, null, _descSetLayoutPtr) != Result.Success)
                {
                    throw new Exception("Failed to create descriptor set layout");
                }
            }
        }

        internal abstract void CreateDescriptorPoolSizes(uint swapchainImageCount);

        internal abstract void UpdateDescriptorSets();

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
