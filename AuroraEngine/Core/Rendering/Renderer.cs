using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Rendering.Helpers;
using ArctisAurora.Core.Rendering.Modules;
using ArctisAurora.EngineWork.Rendering.Helpers;
using ArctisAurora.EngineWork.Rendering.Modules;
using Silk.NET.Core;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;
using Silk.NET.Vulkan.Extensions.KHR;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static ArctisAurora.EngineWork.Rendering.Helpers.AVulkanHelper;
using Buffer = Silk.NET.Vulkan.Buffer;
using Image = Silk.NET.Vulkan.Image;
using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace ArctisAurora.EngineWork.Rendering
{
    // What the renderer publishes to every shader once a frame, in the global set.
    [StructLayout(LayoutKind.Sequential)]
    public struct GpuEngineStats
    {
        // wall time of each system's last completed tick, milliseconds
        public float mainTickMs;
        public float physicsTickMs;
        public float renderTickMs;
        // seconds since boot, whole and wrapped
        public float totalTime;
        public float wrappedTime;
        public uint frameIndex;
    }

    internal unsafe class Renderer
    {
        internal static Renderer renderer = null!;
        internal static QueueAllocator queueAllocator = null!;
        // driver
        internal static Vk vk = Vk.GetApi();
        internal static Instance instance;
        internal static PhysicalDevice gpu;
        internal static Device logicalDevice;

        // commands
        internal static Queue presentQueue;                     // present surface queue
        internal static Queue compositeQueue;                   // graphics queue
        internal static CommandPool compositeCommandPool;

        internal static readonly object transferCommandLock = new object();
        internal static Queue transferQueue;                    // for buffer transfers
        internal CommandBuffer[] transferCommandBuffers = null!;
        internal static CommandPool transferCommandPool;

        // Frame sync, the swapchain and the modules all live on RenderWindow — one set per OS window.
        // The timeline semaphore value is (frameCounter - MAX_FRAMES_IN_FLIGHT) * 2 + 2; modules
        // signal at +1, the compositor at +2.

        // features
        private readonly string[] extensions = new string[]
        {
            "VK_KHR_swapchain",
            "VK_EXT_descriptor_indexing",
            "VK_EXT_scalar_block_layout"
        };

        private readonly string[] validationLayers = new string[]
        {
            "VK_LAYER_KHRONOS_validation"
        };

        private PhysicalDeviceFeatures _features;
        internal ref PhysicalDeviceFeatures features => ref _features;

        internal PhysicalDeviceVulkan12Features _features12;
        internal ref PhysicalDeviceVulkan12Features features12 => ref _features12;

        // Not aggregated from the modules like the other two. Dynamic rendering replaces VkRenderPass and
        // VkFramebuffer for every module at once, so it is a renderer-wide requirement, not a module opt-in.
        internal PhysicalDeviceVulkan13Features _features13;
        internal ref PhysicalDeviceVulkan13Features features13 => ref _features13;


        // rendering
        internal const int MAX_FRAMES_IN_FLIGHT = 2;

        // Global frame data — one buffer per swapchain image, mapped for the life of the process and
        // bound at set 0. Everything a module owns starts at set 1. The count is a ceiling, not any
        // one window's image count: these outlive every window and are never rebuilt.
        internal const int MAX_SWAPCHAIN_IMAGES = 8;
        private const double timeWrapSeconds = 1024.0;
        internal static DescriptorSetLayout globalSetLayout;
        internal static DescriptorSet[] globalSets = null!;
        private static DescriptorPool globalDescriptorPool;
        private static Buffer[] engineStatsBuffers = null!;
        private static DeviceMemory[] engineStatsMemory = null!;
        private static nint[] engineStatsPtrs = null!;

        // What kind of renderer is running, for the code that only asks the question globally.
        internal static ERendererTypes PrimaryRendererType => Engine.primary.modules[0].rendererType;

        // debug
        private bool isDebugEnabled = true;
        private ExtDebugUtils _debugUtils = null!;
        private DebugUtilsMessengerEXT _debugMessenger;

        
        internal Renderer()
        {
            renderer = this;
        }

        // Setup prerequisites: the feature set the logical device has to be created with, collected
        // from the modules the windows built for themselves.
        internal void PreInitialize()
        {
            features12.TimelineSemaphore = true;
            features13.DynamicRendering = true;

            RenderingModule[] modules = Engine.primary.modules;
            for (int i = 0; i < modules.Length; i++)
            {
                CopyStructTrues(ref features, modules[i].features);
                CopyStructTrues(ref features12, modules[i].features12);
            }
        }

        // initializes the window and driver
        [A_XSDActionDependency("Renderer.Initialize", "Bootstrap")]
        internal static bool Initialize()
        {
            RenderWindow window = Engine.primary;

            // driver
            renderer.CreateVulkanInstance();
            renderer.SetupDebugMessenger();
            window.os.CreateSurface();
            renderer.ChoosePhysicalDevice();

            queueAllocator = new QueueAllocator(vk, ref gpu);
            renderer.CreateLogicalDevice();

            compositeQueue = queueAllocator.AllocateQueue(vk, logicalDevice, QueueFlags.GraphicsBit);
            presentQueue = queueAllocator.AllocatePresentQueue(vk, logicalDevice);
            transferQueue = queueAllocator.AllocateQueue(vk, logicalDevice, QueueFlags.TransferBit);

            renderer.CreateSwapchain(window);
            for (int i = 0; i < window.modules.Length; i++)
                window.modules[i].BindWindow(window);

            renderer.CreateCommandPool((uint)queueAllocator.GetFamilyIndex(QueueFlags.GraphicsBit), out compositeCommandPool, CommandPoolCreateFlags.ResetCommandBufferBit);
            renderer.CreateCommandPool((uint)queueAllocator.GetFamilyIndex(QueueFlags.TransferBit), out transferCommandPool, CommandPoolCreateFlags.TransientBit);

            return true;
        }

        // initializes the rendering modules
        [A_XSDActionDependency("Renderer.PrepareDescriptors", "Bootstrap")]
        internal static bool PrepareDescriptors()
        {
            renderer.CreateGlobalResources();
            renderer.CreateDescriptorSetLayouts();
            //CreateDescriptorPool();
            //AllocateDescriptorSets();
            //UpdateGlobalDescriptorSet();
            return true;
        }

        // The set every module binds at 0, and the buffers behind it. Built once and never touched
        // again, so a module's recorded command buffer can bind its image's set and keep it.
        private void CreateGlobalResources()
        {
            ulong size = (ulong)Unsafe.SizeOf<GpuEngineStats>();
            engineStatsBuffers = new Buffer[MAX_SWAPCHAIN_IMAGES];
            engineStatsMemory = new DeviceMemory[MAX_SWAPCHAIN_IMAGES];
            engineStatsPtrs = new nint[MAX_SWAPCHAIN_IMAGES];

            for (int i = 0; i < MAX_SWAPCHAIN_IMAGES; i++)
            {
                AVulkanBufferHandler.CreateMappedBuffer(size, ref engineStatsBuffers[i], ref engineStatsMemory[i],
                    out engineStatsPtrs[i], BufferUsageFlags.UniformBufferBit);
            }

            DescriptorSetLayoutBinding binding = new DescriptorSetLayoutBinding()
            {
                Binding = 0,
                DescriptorCount = 1,
                DescriptorType = DescriptorType.UniformBuffer,
                StageFlags = ShaderStageFlags.All
            };
            DescriptorSetLayoutCreateInfo layoutInfo = new DescriptorSetLayoutCreateInfo()
            {
                SType = StructureType.DescriptorSetLayoutCreateInfo,
                BindingCount = 1,
                PBindings = &binding
            };
            if (vk.CreateDescriptorSetLayout(logicalDevice, ref layoutInfo, null, out globalSetLayout) != Result.Success)
                throw new Exception("Failed to create the global descriptor set layout");

            DescriptorPoolSize poolSize = new DescriptorPoolSize()
            {
                Type = DescriptorType.UniformBuffer,
                DescriptorCount = MAX_SWAPCHAIN_IMAGES
            };
            DescriptorPoolCreateInfo poolInfo = new DescriptorPoolCreateInfo()
            {
                SType = StructureType.DescriptorPoolCreateInfo,
                PoolSizeCount = 1,
                PPoolSizes = &poolSize,
                MaxSets = MAX_SWAPCHAIN_IMAGES
            };
            if (vk.CreateDescriptorPool(logicalDevice, ref poolInfo, null, out globalDescriptorPool) != Result.Success)
                throw new Exception("Failed to create the global descriptor pool");

            DescriptorSetLayout[] layouts = new DescriptorSetLayout[MAX_SWAPCHAIN_IMAGES];
            Array.Fill(layouts, globalSetLayout);
            globalSets = new DescriptorSet[MAX_SWAPCHAIN_IMAGES];

            fixed (DescriptorSetLayout* layoutsPtr = layouts)
            fixed (DescriptorSet* setsPtr = globalSets)
            {
                DescriptorSetAllocateInfo allocInfo = new DescriptorSetAllocateInfo()
                {
                    SType = StructureType.DescriptorSetAllocateInfo,
                    DescriptorPool = globalDescriptorPool,
                    DescriptorSetCount = MAX_SWAPCHAIN_IMAGES,
                    PSetLayouts = layoutsPtr
                };
                Result r = vk.AllocateDescriptorSets(logicalDevice, ref allocInfo, setsPtr);
                if (r != Result.Success)
                    throw new Exception("Failed to allocate the global descriptor sets with error " + r);
            }

            for (int i = 0; i < MAX_SWAPCHAIN_IMAGES; i++)
            {
                DescriptorBufferInfo bufferInfo = new DescriptorBufferInfo()
                {
                    Buffer = engineStatsBuffers[i],
                    Offset = 0,
                    Range = size
                };
                WriteDescriptorSet write = new WriteDescriptorSet()
                {
                    SType = StructureType.WriteDescriptorSet,
                    DstSet = globalSets[i],
                    DstBinding = 0,
                    DstArrayElement = 0,
                    DescriptorCount = 1,
                    DescriptorType = DescriptorType.UniformBuffer,
                    PBufferInfo = &bufferInfo
                };
                vk.UpdateDescriptorSets(logicalDevice, 1, &write, 0, null);
            }
        }

        // The frame's engine-wide data, written per swapchain image before the frame is submitted.
        internal static void UpdateGlobalBuffers(RenderWindow window, uint imageIndex)
        {
            double total = Volatile.Read(ref Engine.totalTime);
            GpuEngineStats stats = new GpuEngineStats()
            {
                mainTickMs = (float)Engine.mainSystem.LastTickMs,
                physicsTickMs = (float)Engine.physicsSystem.LastTickMs,
                renderTickMs = (float)Engine.renderSystem.LastTickMs,
                totalTime = (float)total,
                wrappedTime = (float)(total % timeWrapSeconds),
                frameIndex = (uint)window.frameCounter
            };
            Unsafe.Write((void*)engineStatsPtrs[imageIndex], stats);
        }

        [A_XSDActionDependency("Renderer.SetupObjects", "Bootstrap")]
        internal static bool SetupObjects()
        {
            RenderingModule[] modules = Engine.primary.modules;
            for (int i = 0; i < modules.Length; i++)
            {
                modules[i].PrepareObjects();
            }
            return true;
        }

        [A_XSDActionDependency("Renderer.SetupPipelines", "Bootstrap")]
        internal static bool SetupPipelines()
        {
            RenderWindow window = Engine.primary;
            for (int i = 0; i < window.modules.Length; i++)
            {
                window.modules[i].CreateOutputImages();
                window.modules[i].CreatePipeline();
            }
            window.compositor = new CompositorModule();
            window.compositor.BindWindow(window);
            window.compositor.Init(window.modules, window.swapchainImageViews);

            return true;
        }

        // Dead — no caller; the live path is Draw() -> UpdateModule -> WriteCommandBuffers.
        internal void CreateCommandBuffers()
        {
            RenderWindow window = Engine.primary;
            for (int modulesIndex = 0; modulesIndex < window.modules.Length; modulesIndex++)
            {
                window.modules[modulesIndex].WriteCommandBuffers(window.currentFrame);
            }
        }

        [A_XSDActionDependency("Renderer.CreateSyncObjects", "Bootstrap")]
        internal static bool CreateSyncObjects()
        {
            CreateSyncObjects(Engine.primary);
            return true;
        }

        internal static void CreateSyncObjects(RenderWindow window)
        {
            window.imageAvailableSemaphores = new Semaphore[MAX_FRAMES_IN_FLIGHT];
            window.renderFinishedSemaphores = new Semaphore[window.imageCount];
            window.modulesFinishedSemaphores = new Semaphore[window.imageCount];
            window.frameCounter = MAX_FRAMES_IN_FLIGHT;
            window.timelineSemaphore = new Semaphore();

            SemaphoreCreateInfo _semaphoreCreateInfo = new SemaphoreCreateInfo()
            {
                SType = StructureType.SemaphoreCreateInfo
            };

            for (int i = 0; i < MAX_FRAMES_IN_FLIGHT; i++)
            {
                if (vk.CreateSemaphore(logicalDevice, ref _semaphoreCreateInfo, null, out window.imageAvailableSemaphores[i]) != Result.Success)
                {
                    throw new Exception("Failed to create 'Image Available Semaphore' at index " + i);
                }
            }

            for (int i = 0; i < window.imageCount; i++)
            {
                if (vk.CreateSemaphore(logicalDevice, ref _semaphoreCreateInfo, null, out window.renderFinishedSemaphores[i]) != Result.Success)
                {
                    throw new Exception("Failed to create 'Render Finished Semaphore' at index " + i);
                }
            }

            for (int i = 0; i < window.imageCount; i++)
            {
                if (vk.CreateSemaphore(logicalDevice, ref _semaphoreCreateInfo, null, out window.modulesFinishedSemaphores[i]) != Result.Success)
                    throw new Exception("Failed to create 'Modules Finished Semaphore' at index " + i);
            }

            SemaphoreTypeCreateInfo timelineCI = new SemaphoreTypeCreateInfo()
            {
                SType = StructureType.SemaphoreTypeCreateInfo,
                SemaphoreType = SemaphoreType.Timeline,
                InitialValue = MAX_FRAMES_IN_FLIGHT * 2
            };
            SemaphoreCreateInfo semCI = new SemaphoreCreateInfo()
            {
                SType = StructureType.SemaphoreCreateInfo,
                PNext = &timelineCI
            };

            if (vk.CreateSemaphore(logicalDevice, ref semCI, null, out window.timelineSemaphore) != Result.Success)
                throw new Exception("Failed to create frame timeline semaphore");
        }

        private void CreateVulkanInstance()
        {
            IntPtr appName = SilkMarshal.StringToPtr("AuroraRenderer");
            IntPtr engineName = SilkMarshal.StringToPtr("ArctisAurora");

            ApplicationInfo appInfo = new ApplicationInfo
            {
                SType = StructureType.ApplicationInfo,
                PApplicationName = (byte*)appName,
                ApplicationVersion = AVulkanHelper.Version(1, 0, 0),
                PEngineName = (byte*)engineName,
                EngineVersion = AVulkanHelper.Version(1, 0, 0),
                ApiVersion = Vk.Version13
            };

            uint glfwExtensionCount;
            byte** glfwExtensions = AGlfwWindow._glfw.GetRequiredInstanceExtensions(out glfwExtensionCount);
            var localExtensions = SilkMarshal.PtrToStringArray((nint)glfwExtensions, (int)glfwExtensionCount);
            if (isDebugEnabled)
            {
                localExtensions = localExtensions.Append(ExtDebugUtils.ExtensionName).ToArray();
            }
            // Create Vulkan instance info
            IntPtr enabledExtensionNames = SilkMarshal.StringArrayToPtr(localExtensions);
            InstanceCreateInfo createInfo = new InstanceCreateInfo
            {
                SType = StructureType.InstanceCreateInfo,
                PApplicationInfo = &appInfo,
                EnabledExtensionCount = (uint)localExtensions.Length,
                PpEnabledExtensionNames = (byte**)enabledExtensionNames
            };

            IntPtr enabledLayerNames = SilkMarshal.StringArrayToPtr(validationLayers);
            if (isDebugEnabled)
            {
                createInfo.EnabledLayerCount = (uint)validationLayers.Length;
                createInfo.PpEnabledLayerNames = (byte**)enabledLayerNames;
                DebugUtilsMessengerCreateInfoEXT debugCreateInfo = new();
                debugCreateInfo.MessageSeverity =
                    DebugUtilsMessageSeverityFlagsEXT.VerboseBitExt
                    | DebugUtilsMessageSeverityFlagsEXT.WarningBitExt
                    | DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt;
                debugCreateInfo.MessageType =
                    DebugUtilsMessageTypeFlagsEXT.GeneralBitExt
                    | DebugUtilsMessageTypeFlagsEXT.ValidationBitExt
                    | DebugUtilsMessageTypeFlagsEXT.PerformanceBitExt;

                PopulateDebugMessengerCreateInfo(ref debugCreateInfo);
                createInfo.PNext = &debugCreateInfo;
            }
            else
            {
                createInfo.EnabledLayerCount = 0;
                createInfo.PNext = null;
            }

            // Create Vulkan instance
            fixed (Instance* instancePtr = &instance)
            {
                if (vk.CreateInstance(&createInfo, null, instancePtr) != Result.Success)
                {
                    Console.WriteLine("Failed to create Vulkan instance.");
                }
            }

            // Clean up unmanaged memory
            SilkMarshal.Free(appName);
            SilkMarshal.Free(engineName);
            SilkMarshal.Free(enabledExtensionNames);
            SilkMarshal.Free(enabledLayerNames);
        }

        private uint DebugCallback(DebugUtilsMessageSeverityFlagsEXT messageSeverity, DebugUtilsMessageTypeFlagsEXT messageTypes, DebugUtilsMessengerCallbackDataEXT* pCallbackData, void* pUserData)
        {
            if (messageSeverity < DebugUtilsMessageSeverityFlagsEXT.WarningBitExt)
                return Vk.False;

            string msg = Marshal.PtrToStringAnsi((nint)pCallbackData->PMessage);
            string stack = new System.Diagnostics.StackTrace(true).ToString();
            Console.WriteLine($"[Vulkan {messageSeverity}] {msg}");
            Console.WriteLine(stack);
            return Vk.False;
        }

        private void SetupDebugMessenger()
        {
            if (!isDebugEnabled) return;

            if (!vk.TryGetInstanceExtension(instance, out _debugUtils)) return;

            DebugUtilsMessengerCreateInfoEXT createInfo = new DebugUtilsMessengerCreateInfoEXT();
            PopulateDebugMessengerCreateInfo(ref createInfo);
            if (_debugUtils!.CreateDebugUtilsMessenger(instance, in createInfo, null, out _debugMessenger) != Result.Success)
            {
                throw new Exception("Failed to create debug messenger");
            }
        }

        /*internal static void SetDebugName(ulong objectHandle, ObjectType objectType, string name)
        {
            if (!vk.TryGetInstanceExtension<ExtDebugUtils>(instance, out var debugUtils))
                return;

            fixed (byte* namePtr = System.Text.Encoding.UTF8.GetBytes(name + '\0'))
            {
                DebugUtilsObjectNameInfoEXT nameInfo = new()
                {
                    SType = StructureType.DebugUtilsObjectNameInfoExt,
                    ObjectType = objectType,
                    ObjectHandle = objectHandle,
                    PObjectName = namePtr
                };
                debugUtils.SetDebugUtilsObjectName(logicalDevice, &nameInfo);
            }
        }*/

        private void PopulateDebugMessengerCreateInfo(ref DebugUtilsMessengerCreateInfoEXT createInfo)
        {
            createInfo.SType = StructureType.DebugUtilsMessengerCreateInfoExt;
            createInfo.MessageSeverity = DebugUtilsMessageSeverityFlagsEXT.VerboseBitExt |
                                         DebugUtilsMessageSeverityFlagsEXT.WarningBitExt |
                                         DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt;
            createInfo.MessageType = DebugUtilsMessageTypeFlagsEXT.GeneralBitExt |
                                     DebugUtilsMessageTypeFlagsEXT.PerformanceBitExt |
                                     DebugUtilsMessageTypeFlagsEXT.ValidationBitExt;
            createInfo.PfnUserCallback = (DebugUtilsMessengerCallbackFunctionEXT)DebugCallback;
        }

        private void ChoosePhysicalDevice()
        {
            uint deviceCount = 0;
            vk.GetPhysicalDevices(instance);
            vk.EnumeratePhysicalDevices(instance, &deviceCount, null);
            if (deviceCount == 0)
            {
                throw new Exception("Failed to find Vulcan compatible device");
            }
            PhysicalDevice[] devices = new PhysicalDevice[deviceCount];
            devices = (PhysicalDevice[])vk.GetPhysicalDevices(instance);
            gpu = devices[0];

            string preferred = SettingsRegistry.Get<GraphicsSettings>().device.name;
            if (string.IsNullOrWhiteSpace(preferred)) return;

            for (int i = 0; i < devices.Length; i++)
            {
                if (!DeviceName(devices[i]).Contains(preferred, StringComparison.OrdinalIgnoreCase)) continue;
                gpu = devices[i];
                return;
            }
            Console.WriteLine($"[Renderer] no device matching '{preferred}' — using {DeviceName(gpu)}.");
        }

        private string DeviceName(PhysicalDevice device)
        {
            PhysicalDeviceProperties properties;
            vk.GetPhysicalDeviceProperties(device, &properties);
            return SilkMarshal.PtrToString((nint)properties.DeviceName);
        }

        // Asks the GPU what it actually supports before vkCreateDevice does. Without this an unsupported
        // driver only reports a bare ErrorFeatureNotPresent with no indication of which feature was missing.
        private void VerifyRequiredFeatures()
        {
            PhysicalDeviceVulkan13Features supported13 = new PhysicalDeviceVulkan13Features()
            {
                SType = StructureType.PhysicalDeviceVulkan13Features
            };
            PhysicalDeviceFeatures2 supported = new PhysicalDeviceFeatures2()
            {
                SType = StructureType.PhysicalDeviceFeatures2,
                PNext = &supported13
            };
            vk.GetPhysicalDeviceFeatures2(gpu, &supported);

            if (!supported13.DynamicRendering)
                throw new Exception("GPU driver does not support dynamic rendering (VkPhysicalDeviceVulkan13Features::dynamicRendering). Minimum: NVIDIA Maxwell, AMD Polaris, Intel Skylake.");
        }

        private void CreateLogicalDevice()
        {
            VerifyRequiredFeatures();

            PhysicalDeviceVulkan13Features f13 = features13;
            f13.SType = StructureType.PhysicalDeviceVulkan13Features;
            PhysicalDeviceVulkan12Features f12 = features12;
            f12.SType = StructureType.PhysicalDeviceVulkan12Features;
            f12.PNext = &f13;
            PhysicalDeviceFeatures2 physicalDeviceFeatures2 = new PhysicalDeviceFeatures2()
            {
                SType = StructureType.PhysicalDeviceFeatures2,
                Features = features,
                PNext = &f12
            };

            nint[] validationLayersPtrs = validationLayers.Select(layer => Marshal.StringToHGlobalAnsi(layer)).ToArray();
            nint ppValidationLayers = Marshal.UnsafeAddrOfPinnedArrayElement(validationLayers.Select(Marshal.StringToHGlobalAnsi).ToArray(), 0);
            Marshal.Copy(validationLayersPtrs, 0, ppValidationLayers, validationLayersPtrs.Length);

            uint extensionCount = 0;
            vk.EnumerateDeviceExtensionProperties(gpu, (byte*)null, &extensionCount, null);
            ExtensionProperties[] availableExtensions = new ExtensionProperties[extensionCount];
            fixed (ExtensionProperties* availableExtensionsPtr = availableExtensions)
            {
                vk.EnumerateDeviceExtensionProperties(gpu, (byte*)null, &extensionCount, availableExtensionsPtr);
            }

            // Check for required extensions
            foreach (string requiredExtension in extensions)
            {
                bool found = availableExtensions.Any(ext => Marshal.PtrToStringAnsi((nint)ext.ExtensionName).TrimEnd('\0') == requiredExtension);
                if (!found)
                {
                    throw new Exception($"Required extension '{requiredExtension}' is not supported by the physical device.");
                }
            }

            nint[] enabledExtensions = extensions.Select(ext => Marshal.StringToHGlobalAnsi(ext)).ToArray();
            nint ppEnabledExtensions = Marshal.AllocHGlobal(nint.Size * enabledExtensions.Length);
            Marshal.Copy(enabledExtensions, 0, ppEnabledExtensions, enabledExtensions.Length);

            float queuePriority = 1.0f;
            DeviceQueueCreateInfo graphicsQueue = new DeviceQueueCreateInfo
            {
                SType = StructureType.DeviceQueueCreateInfo,
                QueueFamilyIndex = (uint)queueAllocator.GetFamilyIndex(QueueFlags.GraphicsBit),
                QueueCount = 1,
                PQueuePriorities = &queuePriority
            };
            DeviceQueueCreateInfo transferQueue = new DeviceQueueCreateInfo
            {
                SType = StructureType.DeviceQueueCreateInfo,
                QueueFamilyIndex = (uint)queueAllocator.GetFamilyIndex(QueueFlags.TransferBit),
                QueueCount = 1,
                PQueuePriorities = &queuePriority
            };
            var queues = stackalloc[] { graphicsQueue, transferQueue };


            DeviceCreateInfo createInfo = new DeviceCreateInfo
            {
                SType = StructureType.DeviceCreateInfo,
                QueueCreateInfoCount = 2,
                PQueueCreateInfos = queues,

                EnabledExtensionCount = (uint)enabledExtensions.Length,
                PpEnabledExtensionNames = (byte**)ppEnabledExtensions,

                EnabledLayerCount = (uint)validationLayers.Length,
                PpEnabledLayerNames = (byte**)ppValidationLayers,

                PEnabledFeatures = null,

                PNext = &physicalDeviceFeatures2
            };
            Result r = vk.CreateDevice(gpu, ref createInfo, null, out logicalDevice);
            if (r != Result.Success)
            {
                throw new Exception("Failed to create logical device");
            }


            // cleanup unmanaged memory
            foreach (var ptr in validationLayersPtrs)
            {
                Marshal.FreeHGlobal(ptr);
            }
            foreach(var ptr in enabledExtensions)
            {
                Marshal.FreeHGlobal(ptr);
            }
            Marshal.FreeHGlobal(ppEnabledExtensions);
        }

        // The surface reports the extent the images will actually get, and mid-drag it is already a frame
        // behind or ahead of the window size GLFW hands us. Everything sized to the swapchain reads the
        // result back off the window's swapchainExtent rather than the live window.
        private Extent2D ChooseSwapchainExtent(ref SurfaceCapabilitiesKHR capabilities, RenderWindow window)
        {
            if (capabilities.CurrentExtent.Width != uint.MaxValue)
                return capabilities.CurrentExtent;

            Extent2D _window = window.os.windowSize;
            return new Extent2D(
                Math.Clamp(_window.Width, capabilities.MinImageExtent.Width, capabilities.MaxImageExtent.Width),
                Math.Clamp(_window.Height, capabilities.MinImageExtent.Height, capabilities.MaxImageExtent.Height));
        }

        internal void CreateSwapchain(RenderWindow window)
        {
            SwapChainSupportDetails _support = GetSupportDetails(ref gpu, ref window.os.driverSurface, ref window.os.surface);
            window.surfaceFormat = GetSwapchainSurfaceFormat(_support.Formats);
            PresentModeKHR _presentMode = GetPresentMode(_support.PresentModes);

            var _queueFamilyIndices = stackalloc[] { (uint)queueAllocator.GetFamilyIndex(QueueFlags.GraphicsBit), (uint)queueAllocator.presentFamilyIndex };
            uint _imageCount = _support.Capabilities.MinImageCount + 1;
            window.swapchainExtent = ChooseSwapchainExtent(ref _support.Capabilities, window);
            SwapchainCreateInfoKHR _swapchainCreateInfo = new SwapchainCreateInfoKHR()
            {
                SType = StructureType.SwapchainCreateInfoKhr,
                Surface = window.os.surface,

                MinImageCount = _imageCount,
                ImageFormat = window.surfaceFormat.Format,
                ImageColorSpace = window.surfaceFormat.ColorSpace,
                ImageExtent = window.swapchainExtent,
                ImageArrayLayers = 1,
                ImageUsage = ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransferDstBit,
                ImageSharingMode = SharingMode.Exclusive,
                PresentMode = _presentMode,
                Clipped = true,
                OldSwapchain = default,
                CompositeAlpha = CompositeAlphaFlagsKHR.OpaqueBitKhr,
                PreTransform = _support.Capabilities.CurrentTransform,
                QueueFamilyIndexCount = 2,
                PQueueFamilyIndices = _queueFamilyIndices,
            };

            if (!vk.TryGetDeviceExtension(instance, logicalDevice, out window.swapchainKHR))
            {
                throw new Exception("VK_KHR_swapchain extension not found on the device");
            }

            Result r = window.swapchainKHR!.CreateSwapchain(logicalDevice, ref _swapchainCreateInfo, null, out window.swapchain);
            if (r != Result.Success)
            {
                throw new Exception("Failed to create swapchain " + r);
            }
            // The driver decides how many images it hands back, so this is read rather than assumed —
            // every per-image array in the window and its modules is sized off it.
            uint _swapchainImageCount = 0;
            window.swapchainKHR.GetSwapchainImages(logicalDevice, window.swapchain, &_swapchainImageCount, null);
            window.swapchainImages = new Image[_swapchainImageCount];
            fixed (Image* _imagePtr = window.swapchainImages)
            {
                window.swapchainKHR.GetSwapchainImages(logicalDevice, window.swapchain, &_swapchainImageCount, _imagePtr);
            }
            window.imageCount = _swapchainImageCount;

            window.swapchainImageViews = new ImageView[_swapchainImageCount];
            for (int i = 0; i < window.swapchainImages.Length; i++)
            {
                AVulkanBufferHandler.CreateImageView(vk, ref logicalDevice, ref window.swapchainImages[i], ref window.swapchainImageViews[i], window.surfaceFormat.Format, ImageAspectFlags.ColorBit);
            }
        }

        // Raises the flag the render thread already watches, rather than tearing the swapchain down
        // from whichever thread applied the setting.
        [A_XSDActionDependency("Renderer.RequestSwapchainRebuild", "Settings")]
        internal static void RequestSwapchainRebuild()
        {
            foreach (RenderWindow window in Engine.windows.Values)
                window.os.frameBufferResized = true;
        }

        // Rebuilds the swapchain and every window-sized resource after a resize. Pipelines use
        // dynamic viewport/scissor (see modules), so they are NOT recreated here.
        //
        // The window size is whatever the main thread's resize callback last published — GLFW
        // documents its window queries as main-thread only, and this runs on the render thread.
        internal void RecreateSwapchain(RenderWindow window)
        {
            // wait until the GPU is idle before tearing down resources still in use
            vk.DeviceWaitIdle(logicalDevice);

            // bail if minimized
            if (window.os.windowSize.Width == 0 || window.os.windowSize.Height == 0)
                return;

            // tear down size-dependent resources (the module output images)
            for (int i = 0; i < window.modules.Length; i++)
                window.modules[i].DestroySizeDependentResources();
            window.compositor.DestroySizeDependentResources();

            // tear down the swapchain image views and the swapchain itself
            for (int i = 0; i < window.swapchainImageViews.Length; i++)
                vk.DestroyImageView(logicalDevice, window.swapchainImageViews[i], null);
            window.swapchainKHR.DestroySwapchain(logicalDevice, window.swapchain, null);

            // recreate the swapchain at the new size
            uint previousImageCount = window.imageCount;
            CreateSwapchain(window);

            // A present-mode change can hand back a different number of images, and every per-image
            // array in the window and its modules was sized to the old count.
            if (window.imageCount != previousImageCount)
                ResizePerImageResources(window);

            // recreate per-module output images at the new size
            for (int i = 0; i < window.modules.Length; i++)
                window.modules[i].CreateOutputImages();

            // the compositor's descriptors sample the freshly created module output views — rewrite them
            for (int f = 0; f < (int)window.imageCount; f++)
                window.compositor.UpdateDescriptorSets(f, 0);

            // force command buffers to be re-recorded at the new size on every image
            for (int i = 0; i < window.modules.Length; i++)
                for (int d = 0; d < window.modules[i].isDirty.Length; d++)
                    window.modules[i].isDirty[d] = true;
            for (int d = 0; d < window.compositor.isDirty.Length; d++)
                window.compositor.isDirty[d] = true;
        }

        // Re-sizes everything indexed by swapchain image after the image count itself changed.
        private void ResizePerImageResources(RenderWindow window)
        {
            DestroySyncObjects(window);
            CreateSyncObjects(window);

            for (int i = 0; i < window.modules.Length; i++)
                window.modules[i].RebindImageCount(window);
            window.compositor.RebindImageCount(window);
        }

        internal static void DestroySyncObjects(RenderWindow window)
        {
            for (int i = 0; i < window.imageAvailableSemaphores.Length; i++)
                vk.DestroySemaphore(logicalDevice, window.imageAvailableSemaphores[i], null);
            for (int i = 0; i < window.renderFinishedSemaphores.Length; i++)
                vk.DestroySemaphore(logicalDevice, window.renderFinishedSemaphores[i], null);
            for (int i = 0; i < window.modulesFinishedSemaphores.Length; i++)
                vk.DestroySemaphore(logicalDevice, window.modulesFinishedSemaphores[i], null);
            vk.DestroySemaphore(logicalDevice, window.timelineSemaphore, null);
        }

        public void CreateCommandPool(uint qfIndex, out CommandPool pool, CommandPoolCreateFlags flags)
        {
            CommandPoolCreateInfo _createInfo = new CommandPoolCreateInfo()
            {
                SType = StructureType.CommandPoolCreateInfo,
                QueueFamilyIndex = qfIndex,
                Flags = flags
            };
            if (vk.CreateCommandPool(logicalDevice, ref _createInfo, null, out pool) != Result.Success)
            {
                throw new Exception("Failed to create command pool");
            }
        }

        private void AllocateDescriptorSets()
        {
            RenderWindow window = Engine.primary;
            for(int i = 0; i < window.modules.Length; i++)
            {
                window.modules[i].AllocateDescriptorSets(window.currentFrame);
            }
        }

        internal void UpdateGlobalDescriptorSet()
        {
            RenderWindow window = Engine.primary;
            for(int i=0; i < window.modules.Length; i++)
            {
                window.modules[i].UpdateDescriptorSets(window.currentFrame, 0);
            }
        }

        internal void UpdateModules()
        {
            foreach (RenderWindow window in Engine.windows.Values)
            {
                if (!window.gpuReady || window.closeRequested) continue;

                RenderingModule[] modules = window.modules;
                for (int i = 0; i < modules.Length; i++)
                {
                    if (modules[i].RendererStage != ERendererStage.UI) continue;

                    for (int d = 0; d < modules[i].isDirty.Length; d++)
                        modules[i].isDirty[d] = true;
                }
            }
        }

        private void CreateDescriptorSetLayouts()
        {
            RenderingModule[] modules = Engine.primary.modules;
            for(int i=0; i< modules.Length; i++)
            {
                modules[i].CreateDescriptorSetLayout();
            }
        }

        internal void Draw(RenderWindow window)
        {
            // skip rendering while minimized (0-area framebuffer) — also avoids recreating at 0x0
            if (window.os.windowSize.Width == 0 || window.os.windowSize.Height == 0)
                return;

            ulong waitValue = (window.frameCounter - MAX_FRAMES_IN_FLIGHT) * 2 + 2;
            fixed(Semaphore* timelineSemaphorePtr = &window.timelineSemaphore)
            {
                SemaphoreWaitInfo waitInfo = new SemaphoreWaitInfo()
                {
                    SType = StructureType.SemaphoreWaitInfo,
                    SemaphoreCount = 1,
                    PSemaphores = timelineSemaphorePtr,
                    PValues = &waitValue
                };
                vk.WaitSemaphores(logicalDevice, ref waitInfo, ulong.MaxValue);
            }
            // get next image
            uint imageIndex = 0;
            Result r = window.swapchainKHR.AcquireNextImage(logicalDevice, window.swapchain, ulong.MaxValue, window.imageAvailableSemaphores[window.currentFrame], default, ref imageIndex);

            // update renderer if needed before draw
            if (r == Result.ErrorOutOfDateKhr)
            {
                RecreateSwapchain(window);
                return;
            }
            else if (r != Result.Success && r != Result.SuboptimalKhr)
            {
                throw new Exception("Failed to acquire swapchain image");
            }

            // the renderer's own buffers first, then each module's
            UpdateGlobalBuffers(window, imageIndex);

            // update modules if needed
            for (int i = 0; i < window.modules.Length; i++)
            {
                if (window.modules[i].isDirty[imageIndex] || window.modules[i].HasPendingWork((int)imageIndex))
                    window.modules[i].UpdateModule((int)imageIndex);
                window.modules[i].UpdateFrameData((int)imageIndex);
            }
            if (window.compositor.isDirty[imageIndex])
                window.compositor.UpdateModule((int)imageIndex);

            // submit command buffer
            SubmitInfo _submitInfo = new SubmitInfo()
            {
                SType = StructureType.SubmitInfo
            };

            CommandBuffer[] moduleCBs = new CommandBuffer[window.modules.Length];
            for (int i = 0; i < window.modules.Length; i++)
                moduleCBs[i] = window.modules[i].commandBuffers[imageIndex];

            var semaphoreImageAvailable = stackalloc[] { window.imageAvailableSemaphores[window.currentFrame] };
            var semaphoreStage = stackalloc[] { PipelineStageFlags.ColorAttachmentOutputBit };
            var semaphoreSignalModulesFinished = stackalloc[] { window.timelineSemaphore };
            ulong waitValueModulesFinished = waitValue + 2;
            ulong signalValueModulesFinished = waitValue + 3;

            TimelineSemaphoreSubmitInfo tssInfoModules = new TimelineSemaphoreSubmitInfo()
            {
                SType = StructureType.TimelineSemaphoreSubmitInfo,
                WaitSemaphoreValueCount = 1,
                PWaitSemaphoreValues = &waitValueModulesFinished,
                SignalSemaphoreValueCount = 1,
                PSignalSemaphoreValues = &signalValueModulesFinished,
            };

            fixed (CommandBuffer* moduleCBsPtr = moduleCBs)
            {
                SubmitInfo modulesSubmit = new SubmitInfo()
                {
                    SType = StructureType.SubmitInfo,
                    WaitSemaphoreCount = 1,
                    PWaitSemaphores = semaphoreImageAvailable,
                    PWaitDstStageMask = semaphoreStage,
                    CommandBufferCount = (uint)window.modules.Length,
                    PCommandBuffers = moduleCBsPtr,
                    SignalSemaphoreCount = 1,
                    PSignalSemaphores = semaphoreSignalModulesFinished,
                    PNext = &tssInfoModules
                };
                //vk.ResetFences(logicalDevice, 1, ref inFlightFences[currentFrame]);
                if (vk.QueueSubmit(compositeQueue, 1, ref modulesSubmit, default /*inFlightFences[currentFrame]*/) != Result.Success)
                    throw new Exception("Failed to submit module command buffers");
            }

            // compositor submit and wait
            CommandBuffer compositorCB = window.compositor.commandBuffers[imageIndex];
            var waitSemaphoreModulesFinished = stackalloc[] { window.timelineSemaphore };
            var waitStageCompositor = stackalloc[] { PipelineStageFlags.FragmentShaderBit };
            var signalSemaphoreRenderFinished = stackalloc[] { window.renderFinishedSemaphores[imageIndex], window.timelineSemaphore };

            ulong* signalValsCompositor = stackalloc ulong[2];
            signalValsCompositor[0] = 0;                    // binary — ignored
            signalValsCompositor[1] = waitValue + 4;        // timeline
            TimelineSemaphoreSubmitInfo tssInfoCompositor = new TimelineSemaphoreSubmitInfo()
            {
                SType = StructureType.TimelineSemaphoreSubmitInfo,
                WaitSemaphoreValueCount = 1,
                PWaitSemaphoreValues = &signalValueModulesFinished,
                SignalSemaphoreValueCount = 2,
                PSignalSemaphoreValues = signalValsCompositor,
            };

            SubmitInfo compositorSubmit = new SubmitInfo()
            {
                SType = StructureType.SubmitInfo,
                WaitSemaphoreCount = 1,
                PWaitSemaphores = waitSemaphoreModulesFinished,
                PWaitDstStageMask = waitStageCompositor,
                CommandBufferCount = 1,
                PCommandBuffers = &compositorCB,
                SignalSemaphoreCount = 2,
                PSignalSemaphores = signalSemaphoreRenderFinished,
                PNext = &tssInfoCompositor
            };
            if (vk.QueueSubmit(compositeQueue, 1, ref compositorSubmit, default) != Result.Success)
                throw new Exception("Failed to submit compositor command buffer");


            // present
            var _swapChains = stackalloc[] { window.swapchain };
            var waitSemaphorePresent = stackalloc[] { window.renderFinishedSemaphores[imageIndex] };
            PresentInfoKHR _presentInfo = new PresentInfoKHR()
            {
                SType = StructureType.PresentInfoKhr,
                WaitSemaphoreCount = 1,
                PWaitSemaphores = waitSemaphorePresent,
                SwapchainCount = 1,
                PSwapchains = _swapChains,
                PImageIndices = &imageIndex
            };
            r = window.swapchainKHR.QueuePresent(presentQueue, ref _presentInfo);
            if (r == Result.ErrorOutOfDateKhr || r == Result.SuboptimalKhr || window.os.frameBufferResized)
            {
                window.os.frameBufferResized = false;
                RecreateSwapchain(window);
            }
            else if (r != Result.Success)
            {
                throw new Exception("Failed to present swap chain image");
            }

            window.currentFrame = (window.currentFrame + 1) % MAX_FRAMES_IN_FLIGHT;
            window.frameCounter++;
        }

        // EXTRAS ------------------------------
        internal void CopyStructTrues<T>(ref T destination, T source) where T : struct
        {
            foreach (FieldInfo field in typeof(T).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (field.FieldType == typeof(Bool32))
                {
                    var value = (Bool32)field.GetValue(source);
                    if(value == true)
                    {
                        field.SetValueDirect(__makeref(destination), value);
                    }
                }
            }
        }
    }
}