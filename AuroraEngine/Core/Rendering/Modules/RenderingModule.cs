using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.Rendering.Helpers;
using ArctisAurora.EngineWork.Rendering.Helpers;
using Silk.NET.Vulkan;
using Image = Silk.NET.Vulkan.Image;
using ImageLayout = Silk.NET.Vulkan.ImageLayout;
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

        // the window this module renders into — everything sized to the swapchain reads it from here
        internal RenderWindow window;

        // commands
        public Queue graphicsQueue;
        public CommandPool moduleCommandPool;
        internal CommandBuffer[] commandBuffers;
        public bool[] isDirty;
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
        // Dynamic rendering makes the pipeline name its attachment format up front, and it has to
        // agree with the images below — hence one constant instead of a literal per use site.
        internal const Format outputFormat = Format.R8G8B8A8Unorm;
        public Image[] outputImages;
        public ImageView[] outputImageViews;
        public DeviceMemory[] imageDeviceMemory;
        public Semaphore[] renderFinishedSemaphores;
        public int compositorOrder = 0;

        // quick access
        internal AuroraCamera camera;
        internal abstract IReadOnlyList<Entity> renderEntities { get; set; }

        internal abstract void PrepareObjects();

        // Joins the module to its window and sizes everything indexed by swapchain image. Called
        // once the window's swapchain exists, since the driver decides how many images that is.
        internal void BindWindow(RenderWindow window)
        {
            this.window = window;
            RebindImageCount(window);
        }

        // The pool this module's command buffers come from, so they can be freed when the per-image
        // arrays are resized.
        internal virtual CommandPool commandBufferPool => moduleCommandPool;

        // Re-sizes the per-image arrays after the swapchain handed back a different image count.
        internal virtual void RebindImageCount(RenderWindow window)
        {
            isDirty = new bool[window.imageCount];
            Array.Fill(isDirty, true);

            if (commandBuffers != null)
            {
                fixed (CommandBuffer* buffersPtr = commandBuffers)
                    Renderer.vk.FreeCommandBuffers(Renderer.logicalDevice, commandBufferPool, (uint)commandBuffers.Length, buffersPtr);
                commandBuffers = null;
            }
        }

        internal virtual void RegisterVulkanQueue(QueueAllocator allocator, Vk vk, ref Device device)
        {
            graphicsQueue = allocator.AllocateQueue(vk, device, QueueFlags.GraphicsBit);
        }

        internal abstract void UpdateModule(int currentFrame);

        // Work this module found for itself by polling, as opposed to being told. isDirty stays for
        // invalidation from outside the module (swapchain rebuild, window resize); this covers what
        // the module can see, so producers no longer have to flag the renderer after changing data.
        internal virtual bool HasPendingWork(int frame) => false;

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

        internal virtual void CreateOutputImages()
        {
            uint imageceCount = window.imageCount;
            outputImages = new Image[imageceCount];
            outputImageViews = new ImageView[imageceCount];
            imageDeviceMemory = new DeviceMemory[imageceCount];

            for (int i = 0; i < imageceCount; i++)
            {
                AVulkanBufferHandler.CreateImage(Renderer.vk, ref Renderer.logicalDevice, Renderer.gpu,
                    window.swapchainExtent.Width, window.swapchainExtent.Height,
                    outputFormat,
                    ImageTiling.Optimal,
                    ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.SampledBit,
                    MemoryPropertyFlags.DeviceLocalBit,
                    ref outputImages[i], ref imageDeviceMemory[i]);
                AVulkanBufferHandler.CreateImageView(Renderer.vk, ref Renderer.logicalDevice, ref outputImages[i], ref outputImageViews[i], outputFormat, ImageAspectFlags.ColorBit);
            }
        }

        // Everything this module owns on the device. Called when its window closes, after
        // DeviceWaitIdle. Shared assets are not touched here — they outlive any one window.
        internal virtual void DestroyGpuResources()
        {
            DestroySizeDependentResources();

            if (commandBuffers != null)
            {
                fixed (CommandBuffer* buffersPtr = commandBuffers)
                    Renderer.vk.FreeCommandBuffers(Renderer.logicalDevice, commandBufferPool, (uint)commandBuffers.Length, buffersPtr);
                commandBuffers = null;
            }

            if (frameResources != null)
                for (int i = 0; i < frameResources.Length; i++)
                    if (frameResources[i] != null && frameResources[i].pool.Handle != 0)
                        Renderer.vk.DestroyDescriptorPool(Renderer.logicalDevice, frameResources[i].pool, null);

            if (descriptorSetLayouts != null)
                for (int i = 0; i < descriptorSetLayouts.Length; i++)
                    if (descriptorSetLayouts[i].Handle != 0)
                        Renderer.vk.DestroyDescriptorSetLayout(Renderer.logicalDevice, descriptorSetLayouts[i], null);

            if (pipeline.Handle != 0)
                Renderer.vk.DestroyPipeline(Renderer.logicalDevice, pipeline, null);
            if (pipelineLayout.Handle != 0)
                Renderer.vk.DestroyPipelineLayout(Renderer.logicalDevice, pipelineLayout, null);
            if (moduleCommandPool.Handle != 0)
                Renderer.vk.DestroyCommandPool(Renderer.logicalDevice, moduleCommandPool, null);

            camera?.Destroy();
        }

        // Destroys everything sized to the window/swapchain (the output images). Called on resize before
        // recreation. Null-safe so the compositor, which owns no output images, can call it harmlessly.
        internal virtual void DestroySizeDependentResources()
        {
            if (outputImageViews != null)
                for (int i = 0; i < outputImageViews.Length; i++)
                    if (outputImageViews[i].Handle != 0)
                        Renderer.vk.DestroyImageView(Renderer.logicalDevice, outputImageViews[i], null);

            if (outputImages != null)
                for (int i = 0; i < outputImages.Length; i++)
                    if (outputImages[i].Handle != 0)
                        Renderer.vk.DestroyImage(Renderer.logicalDevice, outputImages[i], null);

            if (imageDeviceMemory != null)
                for (int i = 0; i < imageDeviceMemory.Length; i++)
                    if (imageDeviceMemory[i].Handle != 0)
                        Renderer.vk.FreeMemory(Renderer.logicalDevice, imageDeviceMemory[i], null);
        }

        internal abstract void PrepareCamera();

        internal abstract void WriteCommandBuffers(int currentFrame);

        // A render pass used to do the layout transitions for free — initial/final layouts moved the
        // attachment in and out, and the subpass dependencies supplied the execution and memory barrier
        // around it. CmdBeginRendering does none of that, so every transition the render pass used to
        // imply is now an explicit barrier either side of the CmdBeginRendering/CmdEndRendering pair.
        internal static void ImageBarrier(CommandBuffer commandBuffer, Image image,
            ImageLayout oldLayout, ImageLayout newLayout,
            PipelineStageFlags srcStage, PipelineStageFlags dstStage,
            AccessFlags srcAccess, AccessFlags dstAccess)
        {
            ImageMemoryBarrier barrier = new ImageMemoryBarrier()
            {
                SType = StructureType.ImageMemoryBarrier,
                OldLayout = oldLayout,
                NewLayout = newLayout,
                SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
                DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
                Image = image,
                SrcAccessMask = srcAccess,
                DstAccessMask = dstAccess,
                SubresourceRange = new ImageSubresourceRange()
                {
                    AspectMask = ImageAspectFlags.ColorBit,
                    BaseMipLevel = 0,
                    LevelCount = 1,
                    BaseArrayLayer = 0,
                    LayerCount = 1
                }
            };
            Renderer.vk.CmdPipelineBarrier(commandBuffer, srcStage, dstStage, 0, 0, null, 0, null, 1, &barrier);
        }

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