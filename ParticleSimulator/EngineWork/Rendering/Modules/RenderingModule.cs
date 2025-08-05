using Silk.NET.Vulkan;
using Image = Silk.NET.Vulkan.Image;

namespace ArctisAurora.EngineWork.Rendering.Modules
{
    internal unsafe abstract class RenderingModule
    {
        // type
        internal abstract ERendererTypes rendererType { get; }

        // features
        internal abstract PhysicalDeviceFeatures features { get; }
        internal abstract PhysicalDeviceVulkan12Features features12 { get; }

        // rendering
        internal Pipeline pipeline;
        internal PipelineLayout pipelineLayout;
        internal RenderPass renderPass;

        internal Framebuffer[] frameBuffers;
        internal Framebuffer[] depthFrameBuffers;

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



        internal abstract void CreateRenderPass(ref Vk vk, ref Device logicalDevice, ref PhysicalDevice gpu, ref SurfaceFormatKHR format, ref Extent2D extent2D);

        internal virtual void CreateDescriptorSetLayout(ref Vk vk, ref Device logicalDevice)
        {
            uint typeCount = (uint)descriptorTypes.Count;
            uint indexedMaxCount = 50000;
            uint[] descriptorCount = new uint[typeCount];

            for (int i = 0; i < typeCount; i++)
            {
                if (descriptorBindingFlags[i] == DescriptorBindingFlags.VariableDescriptorCountBit)
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
                if (vk.CreateDescriptorSetLayout(logicalDevice, ref _layoutCreateInfo, null, _descSetLayoutPtr) != Result.Success)
                {
                    throw new Exception("Failed to create descriptor set layout");
                }
            }
        }

        internal abstract void CreateDescriptorPoolSizes(uint swapchainImageCount);

        internal abstract void UpdateDescriptorSets();

        internal abstract void CreatePipeline(ref Vk vk, ref Device logicalDevice, ref Extent2D extent2D);

        internal abstract void CreateFrameBuffers(ref Vk vk, ref Device logicalDevice, ImageView[] swapchainImageViews, ImageView[] swapchainImageViewsDepth, uint swapchainImageCount, ref Extent2D extent);

        internal abstract void PrepareCamera();

        internal abstract void WriteCommandBuffers(ref Vk vk, ref Device logicalDevice, Extent2D extent, CommandBuffer[] commandBuffers, int index);

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
