using ArctisAurora.EngineWork.Rendering.Helpers;
using ArctisAurora.EngineWork.Rendering.Modules;
using Silk.NET.Core;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;
using Silk.NET.Vulkan.Extensions.KHR;
using System.Reflection;
using System.Runtime.InteropServices;
using Windows.ApplicationModel.VoiceCommands;
using static ArctisAurora.EngineWork.Rendering.Helpers.AVulkanHelper;
using Image = Silk.NET.Vulkan.Image;
using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace ArctisAurora.EngineWork.Rendering
{
    internal unsafe class Renderer
    {
        internal static Renderer renderer;

        // window
        private int _width = 1280;
        private int _height = 720;
        internal static Extent2D windowExtent;
        internal static AGlfwWindow window;

        // driver
        internal static Vk vk = Vk.GetApi();
        internal static Instance instance;
        internal static PhysicalDevice gpu;
        internal static Device logicalDevice;
        internal SurfaceFormatKHR surfaceFormat;

        internal int queueFamilyIndex;
        internal uint presentSupportIndex;
        internal static QueueFamilyProperties[] queueFamilyProperties;
        internal static Queue graphicsQueue;
        internal static Queue presentQueue;

        internal static Semaphore[] imageAvailableSemaphores;
        internal static Semaphore[] renderFinishedSemaphores;
        internal static Fence[] inFlightFences;
        internal static Fence[] imagesInFlight;

        // features
        private readonly string[] extensions = new string[]
        {
            "VK_KHR_swapchain",
            "VK_EXT_descriptor_indexing"
        };

        private readonly string[] validationLayers = new string[]
        {
            "VK_LAYER_KHRONOS_validation"
        };

        private PhysicalDeviceFeatures _features;
        internal ref PhysicalDeviceFeatures features => ref _features;

        internal PhysicalDeviceVulkan12Features _features12;
        internal ref PhysicalDeviceVulkan12Features features12 => ref _features12;


        // rendering
        internal static uint swapchainImageCount = 3;
        internal static SwapchainKHR swapchain;
        internal static KhrSwapchain swapchainKHR;

        internal Image[] swapchainImages;
        internal ImageView[] swapchainImageViews;

        internal RenderPass renderPass;

        internal static RenderingModule[] renderingModules;

        // descriptors
        internal Dictionary<DescriptorType, DescriptorPoolSize> descriptorPoolSizes = new();
        internal DescriptorPool descriptorPool;

        // commands
        internal CommandPool commandPool;
        internal CommandBuffer[] commandBuffers;

        // debug
        private bool isDebugEnabled = true;
        private ExtDebugUtils _debugUtils;
        private DebugUtilsMessengerEXT _debugMessenger;


        internal Renderer()
        {
            renderer = this;
        }

        // setup prerequisites
        internal void PreInitialize(RenderingModule[] modules)
        {
            renderingModules = modules;
        }

        // initializes the window and driver
        internal void Initialize()
        {
            // windowing
            windowExtent = new Extent2D((uint)_width, (uint)_height);
            window = new AGlfwWindow();
            window.CreateWindow(ref windowExtent);

            // driver
            CreateVulkanInstance();

            window.CreateSurface(ref vk, ref instance);
            ChoosePhysicalDevice();
            
            queueFamilyIndex = FindQueueFamilyIndex(ref vk, ref gpu, ref queueFamilyProperties, QueueFlags.GraphicsBit);
            presentSupportIndex = FindPresentSupportIndex(ref gpu, ref queueFamilyProperties, ref window.driverSurface, ref window.surface);

            CreateLogicalDevice();

            graphicsQueue = vk.GetDeviceQueue(logicalDevice, (uint)queueFamilyIndex, 0);
            presentQueue = vk.GetDeviceQueue(logicalDevice, presentSupportIndex, 0);

            CreateSwapchain();
            PrepareDescriptorPoolSizes();
        }

        // initializes the rendering modules
        internal void PrepareDescriptors()
        {
            CreateDescriptorPool();
            CreateDescriptorSetLayouts();
        }

        internal void SetupPipelines()
        {
            for(int i=0;i< renderingModules.Length; i++)
            {
                renderingModules[i].CreateRenderPass(ref vk, ref logicalDevice, ref gpu, ref surfaceFormat, ref windowExtent);
                renderingModules[i].CreatePipeline(ref vk, ref logicalDevice, ref windowExtent);
            }
        }

        internal void CreateCommandBuffer()
        {

        }

        internal void CreateVulkanInstance()
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
            byte** glfwExtensions = window._glfw.GetRequiredInstanceExtensions(out glfwExtensionCount);
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

        internal uint DebugCallback(DebugUtilsMessageSeverityFlagsEXT messageSeverity, DebugUtilsMessageTypeFlagsEXT messageTypes, DebugUtilsMessengerCallbackDataEXT* pCallbackData, void* pUserData)
        {
            Console.WriteLine($"validation layer:" + Marshal.PtrToStringAnsi((nint)pCallbackData->PMessage));
            return Vk.False;
        }

        internal void SetupDebugMessenger()
        {
            if (!isDebugEnabled) return;

            if (vk.TryGetInstanceExtension(instance, out _debugUtils)) return;

            DebugUtilsMessengerCreateInfoEXT createInfo = new DebugUtilsMessengerCreateInfoEXT();
            PopulateDebugMessengerCreateInfo(ref createInfo);
            if (_debugUtils!.CreateDebugUtilsMessenger(instance, in createInfo, null, out _debugMessenger) != Result.Success)
            {
                throw new Exception("Failed to create debug messenger");
            }
        }

        internal void PopulateDebugMessengerCreateInfo(ref DebugUtilsMessengerCreateInfoEXT createInfo)
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

        internal void ChoosePhysicalDevice()
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
        }

        internal void CreateLogicalDevice()
        {
            float queuePriority = 1.0f;
            DeviceQueueCreateInfo queueCreateInfo = new DeviceQueueCreateInfo
            {
                SType = StructureType.DeviceQueueCreateInfo,
                QueueFamilyIndex = (uint)queueFamilyIndex,
                QueueCount = 1,
                PQueuePriorities = &queuePriority
            };

            for (int i = 0; i < renderingModules.Length; i++)
            {
                CopyStructTrues(ref features, renderingModules[i].features);
                CopyStructTrues(ref features12, renderingModules[i].features12);
            }

            PhysicalDeviceVulkan12Features f12 = features12;
            f12.SType = StructureType.PhysicalDeviceVulkan12Features;
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

            DeviceCreateInfo createInfo = new DeviceCreateInfo
            {
                SType = StructureType.DeviceCreateInfo,
                QueueCreateInfoCount = 1,
                PQueueCreateInfos = &queueCreateInfo,

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

        internal void CreateSwapchain()
        {
            SwapChainSupportDetails _support = GetSupportDetails(ref gpu, ref window.driverSurface, ref window.surface);
            surfaceFormat = GetSwapchainSurfaceFormat(_support.Formats);
            PresentModeKHR _presentMode = GetPresentMode(_support.PresentModes);

            QueueFamilyIndices _indices = FindQueueFamilies(ref queueFamilyProperties, ref gpu, ref window.driverSurface, ref window.surface);
            var _queueFamilyIndices = stackalloc[] { _indices.GraphicsFamily.Value, _indices.PresentFamily.Value };

            uint _imageCount = _support.Capabilities.MinImageCount + 1;
            SwapchainCreateInfoKHR _swapchainCreateInfo = new SwapchainCreateInfoKHR()
            {
                SType = StructureType.SwapchainCreateInfoKhr,
                Surface = window.surface,

                MinImageCount = _imageCount,
                ImageFormat = surfaceFormat.Format,
                ImageColorSpace = surfaceFormat.ColorSpace,
                ImageExtent = windowExtent,
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

            if (!vk.TryGetDeviceExtension(instance, logicalDevice, out swapchainKHR))
            {
                throw new Exception("VK_KHR_swapchain extension not found on the device");
            }

            Result r = swapchainKHR!.CreateSwapchain(logicalDevice, ref _swapchainCreateInfo, null, out swapchain);
            if (r != Result.Success)
            {
                throw new Exception("Failed to create swapchain " + r);
            }
            uint _swapchainImageCount = 0;
            swapchainKHR.GetSwapchainImages(logicalDevice, swapchain, &_swapchainImageCount, null);
            swapchainImages = new Image[_swapchainImageCount];
            fixed (Image* _imagePtr = swapchainImages)
            {
                swapchainKHR.GetSwapchainImages(logicalDevice, swapchain, &_swapchainImageCount, _imagePtr);
            }

            swapchainImageViews = new ImageView[_swapchainImageCount];
            for (int i = 0; i < swapchainImages.Length; i++)
            {
                AVulkanBufferHandler.CreateImageView(ref vk, ref logicalDevice, ref swapchainImages[i], ref swapchainImageViews[i], surfaceFormat.Format, ImageAspectFlags.ColorBit);
            }
        }

        internal void CreateCommandPool()
        {
            CommandPoolCreateInfo _createInfo = new CommandPoolCreateInfo()
            {
                SType = StructureType.CommandPoolCreateInfo,
                QueueFamilyIndex = (uint)queueFamilyIndex,
            };
            if (vk.CreateCommandPool(logicalDevice, ref _createInfo, null, out commandPool) != Result.Success)
            {
                throw new Exception("Failed to create command pool");
            }
        }

        internal void PrepareDescriptorPoolSizes()
        {
            DescriptorType[] types = Enum.GetValues<DescriptorType>();
            for (int i = 0; i < types.Length; i++)
            {
                descriptorPoolSizes[(DescriptorType)i] = new DescriptorPoolSize
                {
                    Type = (DescriptorType)i,
                    DescriptorCount = 0
                };
            }
        }

        internal void CreateDescriptorPool()
        {
            for (int i = 0; i < renderingModules.Length; i++)
            {
                renderingModules[i].CreateDescriptorPoolSizes(swapchainImageCount);
                for (int j = 0; j < renderingModules[i].descriptorPoolSizes.Length; j++)
                {
                    DescriptorType type = renderingModules[i].descriptorPoolSizes[j].Type;
                    DescriptorPoolSize size = descriptorPoolSizes[type];
                    size.DescriptorCount += renderingModules[i].descriptorPoolSizes[j].DescriptorCount;

                    descriptorPoolSizes[type] = size;
                }
            }

            DescriptorPoolSize[] poolSizes = descriptorPoolSizes.Values.Where(pool => pool.DescriptorCount > 0).ToArray();

            fixed (DescriptorPoolSize* _poolSizePtr = poolSizes)
            {
                DescriptorPoolCreateInfo _createInfo = new DescriptorPoolCreateInfo()
                {
                    SType = StructureType.DescriptorPoolCreateInfo,
                    PoolSizeCount = (uint)poolSizes.Length,
                    PPoolSizes = _poolSizePtr,
                    MaxSets = swapchainImageCount,
                    Flags = DescriptorPoolCreateFlags.FreeDescriptorSetBit
                };
                if (vk.CreateDescriptorPool(logicalDevice, ref _createInfo, null, out descriptorPool) != Result.Success)
                {
                    throw new Exception("Failed to create descriptor pool");
                }
            }
        }

        internal void CreateDescriptorSetLayouts()
        {
            for(int i=0; i< renderingModules.Length; i++)
            {
                renderingModules[i].CreateDescriptorSetLayout(ref vk, ref logicalDevice);
            }
        }

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
