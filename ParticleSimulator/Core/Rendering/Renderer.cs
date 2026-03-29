using ArctisAurora.Core.AssetRegistry;
using ArctisAurora.Core.Rendering.Helpers;
using ArctisAurora.EngineWork.Rendering.Helpers;
using ArctisAurora.EngineWork.Rendering.Modules;
using Silk.NET.Core;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;
using Silk.NET.Vulkan.Extensions.KHR;
using System.Reflection;
using System.Runtime.InteropServices;
using static ArctisAurora.Core.Rendering.Helpers.QueueAllocator;
using static ArctisAurora.EngineWork.Rendering.Helpers.AVulkanHelper;
using Image = Silk.NET.Vulkan.Image;
using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace ArctisAurora.EngineWork.Rendering
{
    internal unsafe class Renderer
    {
        internal static Renderer renderer;
        internal static QueueAllocator queueAllocator;
        // driver
        internal static Vk vk = Vk.GetApi();
        internal static Instance instance;
        internal static PhysicalDevice gpu;
        internal static Device logicalDevice;
        internal SurfaceFormatKHR surfaceFormat;

        // commands
        internal static Queue presentQueue;     // present surface queue
        internal static Queue graphicsQueue;    // graphics queue
        internal static CommandPool graphicsCommandPool;
        //internal CommandBuffer[] graphicsCommandBuffers;

        internal static Queue transferQueue;      // for buffer transfers
        internal CommandBuffer[] transferCommandBuffers;
        internal static CommandPool transferCommandPool;

        internal static Semaphore[] imageAvailableSemaphores;
        internal static Semaphore[] renderFinishedSemaphores;
        internal static Fence[] inFlightFences;
        internal static Fence[] inFlightImages;

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


        // rendering
        internal static uint swapchainImageCount = 3;
        internal static SwapchainKHR swapchain;
        internal static KhrSwapchain swapchainKHR;

        internal Image[] swapchainImages;
        internal ImageView[] swapchainImageViews;

        internal DeviceMemory[] swapchainImageMemoriesDepth;
        internal Image[] swapchainImagesDepth;
        internal ImageView[] swapchainImageViewsDepth;

        internal const int MAX_FRAMES_IN_FLIGHT = 2;
        internal static int currentFrame = 0;

        internal static RenderingModule[] renderingModules;

        // debug
        private bool isDebugEnabled = true;
        private ExtDebugUtils _debugUtils;
        private DebugUtilsMessengerEXT _debugMessenger;

        
        internal Renderer()
        {
            renderer = this;
        }

        /*[A_XSDActionDependency("Renderer.NewRenderer", "Bootstrap")]
        internal void InitRenderer()
        {
            renderer = new Renderer();
        }*/

        // setup prerequisites
        internal void PreInitialize(RenderingModule[] modules)
        {
            renderingModules = modules;
        }

        // initializes the window and driver
        [A_XSDActionDependency("Renderer.Initialize", "Bootstrap")]
        internal static void Initialize()
        {
            // driver
            renderer.CreateVulkanInstance();
            Engine.window.CreateSurface();
            renderer.ChoosePhysicalDevice();

            queueAllocator = new QueueAllocator(vk, ref gpu);
            renderer.CreateLogicalDevice();

            graphicsQueue = queueAllocator.AllocateQueue(vk, logicalDevice, QueueFlags.GraphicsBit);
            presentQueue = queueAllocator.AllocatePresentQueue(vk, logicalDevice);
            transferQueue = queueAllocator.AllocateQueue(vk, logicalDevice, QueueFlags.TransferBit);

            renderer.CreateSwapchain();
            renderer.CreateCommandPool((uint)queueAllocator.GetFamilyIndex(QueueFlags.GraphicsBit), out graphicsCommandPool, CommandPoolCreateFlags.ResetCommandBufferBit);
            renderer.CreateCommandPool((uint)queueAllocator.GetFamilyIndex(QueueFlags.TransferBit), out transferCommandPool, CommandPoolCreateFlags.TransientBit);
        }

        // initializes the rendering modules
        [A_XSDActionDependency("Renderer.PrepareDescriptors", "Bootstrap")]
        internal static void PrepareDescriptors()
        {
            renderer.CreateDescriptorSetLayouts();
            //CreateDescriptorPool();
            //AllocateDescriptorSets();
            //UpdateGlobalDescriptorSet();
        }

        [A_XSDActionDependency("Renderer.SetupObjects", "Bootstrap")]
        internal static void SetupObjects()
        {
            for (int i = 0; i < renderingModules.Length; i++)
            {
                renderingModules[i].PrepareObjects();
            }
        }

        [A_XSDActionDependency("Renderer.SetupPipelines", "Bootstrap")]
        internal static void SetupPipelines()
        {
            for(int i=0;i< renderingModules.Length; i++)
            {
                renderingModules[i].CreateRenderPass(ref renderer.surfaceFormat);
                renderingModules[i].CreateFrameBuffers(renderer.swapchainImageViews, renderer.swapchainImageViewsDepth);
                renderingModules[i].CreatePipeline();
            }
        }

        internal void CreateCommandBuffers()
        {
            for (int modulesIndex = 0; modulesIndex < renderingModules.Length; modulesIndex++)
            {
                renderingModules[modulesIndex].WriteCommandBuffers();
            }
        }

        [A_XSDActionDependency("Renderer.CreateSyncObjects", "Bootstrap")]
        internal static void CreateSyncObjects()
        {
            imageAvailableSemaphores = new Semaphore[MAX_FRAMES_IN_FLIGHT];
            renderFinishedSemaphores = new Semaphore[MAX_FRAMES_IN_FLIGHT];
            inFlightFences = new Fence[MAX_FRAMES_IN_FLIGHT];
            inFlightImages = new Fence[swapchainImageCount];

            SemaphoreCreateInfo _semaphoreCreateInfo = new SemaphoreCreateInfo()
            {
                SType = StructureType.SemaphoreCreateInfo
            };

            FenceCreateInfo _fenceCreateInfo = new FenceCreateInfo()
            {
                SType = StructureType.FenceCreateInfo,
                Flags = FenceCreateFlags.SignaledBit
            };

            for (int i = 0; i < MAX_FRAMES_IN_FLIGHT; i++)
            {
                if (vk.CreateSemaphore(logicalDevice, ref _semaphoreCreateInfo, null, out imageAvailableSemaphores[i]) != Result.Success ||
                    vk.CreateSemaphore(logicalDevice, ref _semaphoreCreateInfo, null, out renderFinishedSemaphores[i]) != Result.Success ||
                    vk.CreateFence(logicalDevice, ref _fenceCreateInfo, null, out inFlightFences[i]) != Result.Success)
                {
                    throw new Exception("Failed to create synch objects for a frame at index " + i);
                }
            }
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
            Console.WriteLine($"validation layer:" + Marshal.PtrToStringAnsi((nint)pCallbackData->PMessage));
            return Vk.False;
        }

        private void SetupDebugMessenger()
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
        }

        private void CreateLogicalDevice()
        {
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

        private void CreateSwapchain()
        {
            SwapChainSupportDetails _support = GetSupportDetails(ref gpu, ref Engine.window.driverSurface, ref Engine.window.surface);
            surfaceFormat = GetSwapchainSurfaceFormat(_support.Formats);
            PresentModeKHR _presentMode = GetPresentMode(_support.PresentModes);

            var _queueFamilyIndices = stackalloc[] { (uint)queueAllocator.GetFamilyIndex(QueueFlags.GraphicsBit), (uint)queueAllocator.presentFamilyIndex };
            uint _imageCount = _support.Capabilities.MinImageCount + 1;
            SwapchainCreateInfoKHR _swapchainCreateInfo = new SwapchainCreateInfoKHR()
            {
                SType = StructureType.SwapchainCreateInfoKhr,
                Surface = Engine.window.surface,

                MinImageCount = _imageCount,
                ImageFormat = surfaceFormat.Format,
                ImageColorSpace = surfaceFormat.ColorSpace,
                ImageExtent = Engine.window.windowSize,
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

            swapchainImagesDepth = new Image[_swapchainImageCount];
            swapchainImageMemoriesDepth = new DeviceMemory[_swapchainImageCount];
            Format depthFormat = GetDepthFormat();
            for(int i = 0; i < swapchainImages.Length; i++)
            {
                AVulkanBufferHandler.CreateImage(vk, logicalDevice, gpu, Engine.window.windowSize.Width, Engine.window.windowSize.Height, depthFormat, ImageTiling.Optimal, ImageUsageFlags.DepthStencilAttachmentBit, MemoryPropertyFlags.DeviceLocalBit, ref swapchainImagesDepth[i], ref swapchainImageMemoriesDepth[i]);
            }

            swapchainImageViews = new ImageView[_swapchainImageCount];
            swapchainImageViewsDepth = new ImageView[_swapchainImageCount];
            for (int i = 0; i < swapchainImages.Length; i++)
            {
                AVulkanBufferHandler.CreateImageView(ref vk, ref logicalDevice, ref swapchainImages[i], ref swapchainImageViews[i], surfaceFormat.Format, ImageAspectFlags.ColorBit);
                AVulkanBufferHandler.CreateImageView(ref vk, ref logicalDevice, ref swapchainImagesDepth[i], ref swapchainImageViewsDepth[i], depthFormat, ImageAspectFlags.DepthBit);
            }
        }

        private void CreateCommandPool(uint qfIndex, out CommandPool pool, CommandPoolCreateFlags flags)
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
            for(int i = 0; i < renderingModules.Length; i++)
            {
                renderingModules[i].AllocateDescriptorSets();
            }
        }

        internal void UpdateGlobalDescriptorSet()
        {
            for(int i=0; i < renderingModules.Length; i++)
            {
                renderingModules[i].UpdateDescriptorSets();
            }
        }

        internal void UpdateModules()
        {
            for (int i = 0; i < renderingModules.Length; i++)
            {
                if (renderingModules[i].RendererStage == ERendererStage.UI)
                {
                    renderingModules[i].isDirty = true;
                    //renderingModules[i].UpdateModule();
                    //return;
                }
            }
        }

        private void CreateDescriptorSetLayouts()
        {
            for(int i=0; i< renderingModules.Length; i++)
            {
                renderingModules[i].CreateDescriptorSetLayout();
            }
        }

        internal void Draw()
        {
            vk.WaitForFences(logicalDevice, 1, ref inFlightFences[currentFrame], true, ulong.MaxValue);
            uint imageIndex = 0;
            Result r = swapchainKHR.AcquireNextImage(logicalDevice, swapchain, ulong.MaxValue, imageAvailableSemaphores[currentFrame], default, ref imageIndex);

            if (r == Result.ErrorOutOfDateKhr)
            {
                //RecreateSwapChain();
                return;
            }
            else if (r != Result.Success && r != Result.SuboptimalKhr)
            {
                throw new Exception("Failed to acquire swapchain image");
            }

            for (int i = 0; i < renderingModules.Length; i++)
            {
                if(renderingModules[i].isDirty)
                    renderingModules[i].UpdateModule();
                renderingModules[i].camera.UpdateCameraMatrix(Engine.window.windowSize, imageIndex, (uint)i);
            }
            //int localEntityCount = 0;
            //foreach (Entity e in EntityManager.entitiesToUpdate)
            //{
            //    MeshComponent meshComponent = e.GetComponent<MeshComponent>();
            //    if (meshComponent == null)
            //    {
            //        continue;
            //    }
            //    e.GetComponent<MeshComponent>().UpdateMatrices();
            //    localEntityCount++;
            //}
            //EntityManager.RemoveEntityUpdate(0, localEntityCount);
            //uniforms done
            if (inFlightImages[imageIndex].Handle != default)
            {
                vk.WaitForFences(logicalDevice, 1, ref inFlightImages[imageIndex], true, ulong.MaxValue);
            }
            inFlightImages[imageIndex] = inFlightFences[currentFrame];

            SubmitInfo _submitInfo = new SubmitInfo()
            {
                SType = StructureType.SubmitInfo
            };

            var _waitSemaphores = stackalloc[]
            {
                imageAvailableSemaphores[currentFrame]
            };
            var _waitStages = stackalloc[]
            {
                PipelineStageFlags.ColorAttachmentOutputBit
            };

            CommandBuffer[] executionBuffer = new CommandBuffer[renderingModules.Length];
            for (int i = 0; i < renderingModules.Length; i++)
            {
                executionBuffer[i] = renderingModules[i].commandBuffers[imageIndex];
            }
            var _signalSemaphores = stackalloc[]
{
                renderFinishedSemaphores[currentFrame]
            };

            fixed (CommandBuffer* ptrCommandBuffer = executionBuffer)
            {
                _submitInfo = _submitInfo with
                {
                    WaitSemaphoreCount = 1,
                    PWaitSemaphores = _waitSemaphores,
                    PWaitDstStageMask = _waitStages,

                    CommandBufferCount = (uint)renderingModules.Length,
                    PCommandBuffers = ptrCommandBuffer
                };

                _submitInfo.SignalSemaphoreCount = 1;
                _submitInfo.PSignalSemaphores = _signalSemaphores;

                vk.ResetFences(logicalDevice, 1, ref inFlightFences[currentFrame]);
                r = vk.QueueSubmit(graphicsQueue, 1, ref _submitInfo, inFlightFences[currentFrame]);
                if (r != Result.Success)
                {
                    throw new Exception("Failed to send command buffer to the GPU with error code:" + r);
                }
            }

            var _swapChains = stackalloc[] { swapchain };
            PresentInfoKHR _presentInfo = new PresentInfoKHR()
            {
                SType = StructureType.PresentInfoKhr,
                WaitSemaphoreCount = 1,
                PWaitSemaphores = _signalSemaphores,
                SwapchainCount = 1,
                PSwapchains = _swapChains,
                PImageIndices = &imageIndex
            };
            r = swapchainKHR.QueuePresent(presentQueue, ref _presentInfo);
            if (r == Result.ErrorOutOfDateKhr || r == Result.SuboptimalKhr || Engine.window.frameBufferResized)
            {
                Engine.window.frameBufferResized = false;
                //RecreateSwapChain();
            }
            else if (r != Result.Success)
            {
                throw new Exception("Failed to present swap chain image");
            }

            currentFrame = (currentFrame + 1) % MAX_FRAMES_IN_FLIGHT;
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