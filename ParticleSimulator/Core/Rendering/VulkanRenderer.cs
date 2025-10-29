using ArctisAurora.EngineWork.Rendering.Helpers;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan.Extensions.EXT;
using Silk.NET.Vulkan;
using System.Runtime.InteropServices;
using Semaphore = Silk.NET.Vulkan.Semaphore;
using ArctisAurora.EngineWork.ECS.RenderingComponents.Vulkan;
using ArctisAurora.EngineWork.Rendering.RendererTypes;
using Silk.NET.GLFW;
using ArctisAurora.EngineWork.Rendering.UI;
using ArctisAurora.EngineWork.EngineEntity;
using System.Runtime.CompilerServices;

namespace ArctisAurora.EngineWork.Rendering
{
    enum ERendererTypes
    {
        Rasterizer,
        Pathtracer,
        RadianceCascades,
        RadianceCascades2D,
        UITemp
    }

    [Obsolete("this class is in the deprecation phase", false)]
    internal unsafe class VulkanRenderer
    {
        internal static VulkanRenderer _rendererInstance = null;
        internal static ERendererTypes _rendererType;
        internal int _width = 1280;
        internal int _height = 720;
        internal static Extent2D _extent;
        internal static int _swapimageCount = 1;

        internal static AuroraCamera _camera;

        //validation
        bool _isValidationLayers = true;
        private readonly string[] _validationLayers = new string[]
        {
            "VK_LAYER_KHRONOS_validation",
        };
        private ExtDebugUtils? _debugUtils;
        private DebugUtilsMessengerEXT _debugMessenger;
        //
        internal static AGlfwWindow _glWindow;                      //GLFW window
        internal static Vk _vulkan = Vk.GetApi();                   //vulkan api
        internal static Instance _instance;                         //vulkan instance
        //
        internal static PhysicalDevice _gpu;
        internal static Device _logicalDevice;
        internal static QueueFamilyProperties[] _qfm;               //api queue properties
        //
        internal static GraphicsPipeline _pipeline;
        internal static Swapchain _swapchain;
        internal static DescriptorPool _descriptorPool;
        internal static DescriptorSetLayout _descriptorSetLayout;
        internal static DescriptorSet[] _descriptorSets;
        internal static CommandPool _commandPool;
        internal static CommandBuffer[] _commandBuffer;
        //
        internal static int MAX_FRAMES_IN_FLIGHT = 2;
        internal static int _currentFrame = 0;
        internal static Semaphore[] _imageAvailableSemaphores;
        internal static Semaphore[] _renderFinishedSemaphores;
        internal static Fence[] _fencesInFlight;
        internal static Fence[] _imagesInFlight;
        //
        internal static Queue _graphicsQueue;               //api queue
        internal static Queue _presentQueue;                //fuck knows what this is (ill ask chatgpt later)
        //
        internal static List<Entity> _entitiesToRender = new List<Entity>();
        internal static List<Entity> _lightsToRender = new List<Entity>();
        internal static List<Entity> _updateEntities = new List<Entity>();

        internal void InitRenderer(ERendererTypes _type)
        {
            _rendererType = _type;
            _extent = new Extent2D() { Height = (uint)_height, Width = (uint)_width };
            //end of prerequisites

            //_glWindow.CreateWindow(ref _extent);            //create glfw window
            CreateVulkanInstance();                         //create Vulkan instance
            SetupDebugMessenger();
            //_glWindow.CreateSurface(ref _vulkan, ref _instance); //create window surface
            ChoosePhysicalDevice();                         //we get the gpu
            switch (_rendererType)
            {
                case ERendererTypes.Rasterizer:
                    {
                        Rasterizer _rasterizer = new Rasterizer();
                        break;
                    }
                case ERendererTypes.Pathtracer:
                    {
                        Pathtracing _tracer = new Pathtracing();
                        break;
                    }
                case ERendererTypes.RadianceCascades2D:
                    {
                        RadianceCascades2D _cascades = new RadianceCascades2D();
                        break;
                    }
                case ERendererTypes.UITemp:
                    {
                        UIRenderer _ui = new UIRenderer();
                        break;
                    }
                default:
                    break;
            }
        }

        internal void CreateVulkanInstance()
        {
            IntPtr appName = SilkMarshal.StringToPtr("VulkanApp");
            ApplicationInfo _appInfo = new ApplicationInfo
            {
                SType = StructureType.ApplicationInfo,
                PApplicationName = (byte*)appName,
                ApplicationVersion = AVulkanHelper.Version(1, 0, 0),
                PEngineName = (byte*)SilkMarshal.StringToPtr("ArctisAurora"),
                EngineVersion = AVulkanHelper.Version(1, 0, 0),
                ApiVersion = Vk.Version13
            };

            uint _glfwExtensionCount;
            byte** _glfwExtensions = AGlfwWindow._glfw.GetRequiredInstanceExtensions(out _glfwExtensionCount);
            var _extensions = SilkMarshal.PtrToStringArray((nint)_glfwExtensions, (int)_glfwExtensionCount);
            if (_isValidationLayers)
            {
                _extensions = _extensions.Append(ExtDebugUtils.ExtensionName).ToArray();
            }
            // Create Vulkan instance info
            InstanceCreateInfo createInfo = new InstanceCreateInfo
            {
                SType = StructureType.InstanceCreateInfo,
                PApplicationInfo = &_appInfo,
                EnabledExtensionCount = (uint)_extensions.Length,
                PpEnabledExtensionNames = (byte**)SilkMarshal.StringArrayToPtr(_extensions)
            };

            if (_isValidationLayers)
            {
                createInfo.EnabledLayerCount = (uint)_validationLayers.Length;
                createInfo.PpEnabledLayerNames = (byte**)SilkMarshal.StringArrayToPtr(_validationLayers);
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
            fixed (Instance* instancePtr = &_instance)
            {
                if (_vulkan.CreateInstance(&createInfo, null, instancePtr) != Result.Success)
                {
                    Console.WriteLine("Failed to create Vulkan instance.");
                }
            }

            // Clean up unmanaged memory
            SilkMarshal.Free((nint)_appInfo.PApplicationName);
            SilkMarshal.Free((nint)_appInfo.PEngineName);
            SilkMarshal.Free((nint)createInfo.PpEnabledExtensionNames);
            if (_isValidationLayers)
            {
                SilkMarshal.Free((nint)createInfo.PpEnabledLayerNames);
            }
        }

        internal void SetupDebugMessenger()
        {
            if (!_isValidationLayers) return;

            if (_vulkan.TryGetInstanceExtension(_instance, out _debugUtils)) return;

            DebugUtilsMessengerCreateInfoEXT createInfo = new DebugUtilsMessengerCreateInfoEXT();
            PopulateDebugMessengerCreateInfo(ref createInfo);
            if (_debugUtils!.CreateDebugUtilsMessenger(_instance, in createInfo, null, out _debugMessenger) != Result.Success)
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

        internal uint DebugCallback(DebugUtilsMessageSeverityFlagsEXT messageSeverity, DebugUtilsMessageTypeFlagsEXT messageTypes, DebugUtilsMessengerCallbackDataEXT* pCallbackData, void* pUserData)
        {
            Console.WriteLine($"validation layer:" + Marshal.PtrToStringAnsi((nint)pCallbackData->PMessage));
            return Vk.False;
        }

        internal void ChoosePhysicalDevice()
        {
            uint _deviceCount = 0;
            _vulkan.GetPhysicalDevices(_instance);
            _vulkan.EnumeratePhysicalDevices(_instance, &_deviceCount, null);
            if (_deviceCount == 0)
            {
                throw new Exception("Failed to find Vulcan compatible device");
            }
            PhysicalDevice[] _devices = new PhysicalDevice[_deviceCount];
            _devices = (PhysicalDevice[])_vulkan.GetPhysicalDevices(_instance);
            _gpu = _devices[0];
        }

        internal void CreateLogicalDevice(string[] requiredExtensions,
            PhysicalDeviceVulkan12Features? _vulkan12FT,
            PhysicalDeviceFeatures? _deviceFeatures)
        {
            int _graphicsIndex = AVulkanHelper.FindQueueFamilyIndex(ref _vulkan, ref _gpu, ref _qfm, QueueFlags.GraphicsBit);
            float _qPriority = 1.0f;
            DeviceQueueCreateInfo _qCreateInfo = new DeviceQueueCreateInfo
            {
                SType = StructureType.DeviceQueueCreateInfo,
                QueueFamilyIndex = (uint)_graphicsIndex,
                QueueCount = 1,
                PQueuePriorities = &_qPriority
            };
            PhysicalDeviceFeatures _df = (PhysicalDeviceFeatures)_deviceFeatures;
            PhysicalDeviceVulkan12Features _v12FT = (PhysicalDeviceVulkan12Features)_vulkan12FT;
            PhysicalDeviceFeatures2 _devFeatures2 = new PhysicalDeviceFeatures2()
            {
                SType = StructureType.PhysicalDeviceFeatures2,
                Features = _df,
                PNext = &_v12FT
            };

            string[] _validationLayers = { "VK_LAYER_KHRONOS_validation" };
            byte*[] _validationLayersNames = new byte*[_validationLayers.Length];
            for (int i = 0; i < _validationLayers.Length; i++)
            {
                _validationLayersNames[i] = (byte*)SilkMarshal.StringToPtr(_validationLayers[i]);
            }

            uint extensionCount = 0;
            _vulkan.EnumerateDeviceExtensionProperties(_gpu, (byte*)null, &extensionCount, null);
            ExtensionProperties[] availableExtensions = new ExtensionProperties[extensionCount];
            fixed (ExtensionProperties* availableExtensionsPtr = availableExtensions)
            {
                _vulkan.EnumerateDeviceExtensionProperties(_gpu, (byte*)null, &extensionCount, availableExtensionsPtr);
            }

            // Check for required extensions
            foreach (string requiredExtension in requiredExtensions)
            {
                bool found = availableExtensions.Any(ext => Marshal.PtrToStringAnsi((nint)ext.ExtensionName).TrimEnd('\0') == requiredExtension);
                if (!found)
                {
                    throw new Exception($"Required extension '{requiredExtension}' is not supported by the physical device.");
                }
            }

            nint[] enabledExtensions = requiredExtensions.Select(ext => Marshal.StringToHGlobalAnsi(ext)).ToArray();
            nint ppEnabledExtensions = Marshal.AllocHGlobal(nint.Size * enabledExtensions.Length);
            Marshal.Copy(enabledExtensions, 0, ppEnabledExtensions, enabledExtensions.Length);

            fixed (byte** _ppEnabledLayerNames = _validationLayersNames)
            {
                DeviceCreateInfo _deviceInfo = new DeviceCreateInfo
                {
                    SType = StructureType.DeviceCreateInfo,
                    QueueCreateInfoCount = 1,
                    PQueueCreateInfos = &_qCreateInfo,
                    PpEnabledLayerNames = _ppEnabledLayerNames,

                    EnabledExtensionCount = (uint)enabledExtensions.Length,
                    PpEnabledExtensionNames = (byte**)ppEnabledExtensions,
                    PEnabledFeatures = null,
                    PNext = &_devFeatures2
                };
                Result r = _vulkan.CreateDevice(_gpu, ref _deviceInfo, null, out _logicalDevice);

                if (r != Result.Success)
                {
                    throw new Exception("Failed to create a logical device" + r);
                }
            }

            foreach (var f in _validationLayersNames)
            {
                SilkMarshal.Free((nint)f);
            }
            foreach (var ptr in enabledExtensions)
            {
                Marshal.FreeHGlobal(ptr);
            }
            Marshal.FreeHGlobal(ppEnabledExtensions);
        }

        internal virtual void AddEntityToRenderQueue(Entity _m) 
        {
            for (int i = 0; i < _entitiesToRender.Count; i++)
                _entitiesToRender[i].GetComponent<MeshComponent>().FreeDescriptorSets();
            if (_descriptorPool.Handle != 0)
                _vulkan.DestroyDescriptorPool(_logicalDevice, _descriptorPool, null);
            _entitiesToRender.Add(_m);
        }

        internal virtual void AddEntityToUpdate(Entity _m)
        {
            if (!_updateEntities.Contains(_m))
            {
                if (_m.GetComponent<MeshComponent>() != null)
                    _updateEntities.Add(_m);
            }
        }

        internal virtual void AddLightToRenderQueue(Entity _m) { }

        internal virtual void MouseUpdate(double xPos, double yPos)
        {
            _camera.ProcessMouseMovements(xPos, yPos);
        }


        internal virtual void MouseClick(MouseButton button, InputAction action)
        {

        }

        internal void CreateCommandPool()
        {
            int _queueFamilyIndex = AVulkanHelper.FindQueueFamilyIndex(ref _vulkan, ref _gpu, ref _qfm, QueueFlags.GraphicsBit);
            CommandPoolCreateInfo _createInfo = new CommandPoolCreateInfo()
            {
                SType = StructureType.CommandPoolCreateInfo,
                QueueFamilyIndex = (uint)_queueFamilyIndex,
            };
            if (_vulkan.CreateCommandPool(_logicalDevice, ref _createInfo, null, out _commandPool) != Result.Success)
            {
                throw new Exception("Failed to create command pool");
            }
        }

        internal virtual void CreateCommandBuffers()
        {
            _commandBuffer = new CommandBuffer[_swapimageCount];

            CommandBufferAllocateInfo _allocInfo = new CommandBufferAllocateInfo()
            {
                SType = StructureType.CommandBufferAllocateInfo,
                CommandPool = _commandPool,
                Level = CommandBufferLevel.Primary,
                CommandBufferCount = (uint)_commandBuffer.Length
            };
            fixed (CommandBuffer* _commandBufferPtr = _commandBuffer)
            {
                Result r = _vulkan.AllocateCommandBuffers(_logicalDevice, ref _allocInfo, _commandBufferPtr);
                if (r != Result.Success)
                {
                    throw new Exception("Failed to allocate command buffer with error " + r);
                }
            }
        }

        internal virtual void RecreateCommandBuffers()
        {
            fixed (CommandBuffer* CBPtr = _commandBuffer)
            {
                _vulkan.FreeCommandBuffers(_logicalDevice, _commandPool, (uint)_commandBuffer.Length, CBPtr);
            }
        }

        internal virtual void CreateDescriptorPool() { }

        internal virtual void CreateGlobalDescriptorSets()
        {
            DescriptorSetLayout[] localLayout = new DescriptorSetLayout[_swapimageCount];
            Array.Fill(localLayout, _descriptorSetLayout);

            fixed (DescriptorSetLayout* _layoutsPtr = localLayout)
            {
                uint bufferCount = (uint)_entitiesToRender.Count;
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
        }

        internal void CreateDescriptorSetLayout(int _bindingCount, List<DescriptorType> _descriptorTypes, List<ShaderStageFlags> _stageFlags, ref DescriptorSetLayout _dsl, DescriptorBindingFlags[] indexedFlags, uint descriptorCount = 1)
        {
            List<DescriptorSetLayoutBinding> _bindingList = new List<DescriptorSetLayoutBinding>();
            for (int i=0; i<_bindingCount; i++)
            {
                DescriptorSetLayoutBinding _binding = new DescriptorSetLayoutBinding()
                {
                    Binding = (uint)i,
                    DescriptorCount = descriptorCount,
                    DescriptorType = _descriptorTypes[i],
                    PImmutableSamplers = null,
                    StageFlags = _stageFlags[i]
                };
                _bindingList.Add(_binding);
            }
            var _b = _bindingList.ToArray();
            fixed (DescriptorBindingFlags* _indexedPtr = indexedFlags)
            fixed (DescriptorSetLayoutBinding* _bindingsPtr = _b)
            fixed (DescriptorSetLayout* _descSetLayoutPtr = &_dsl)
            {
                DescriptorSetLayoutBindingFlagsCreateInfoEXT _setLayoutBindingFlags = new()
                {
                    SType = StructureType.DescriptorSetLayoutBindingFlagsCreateInfoExt,
                    BindingCount = (uint)indexedFlags.Length,
                    PBindingFlags = _indexedPtr
                };

                DescriptorSetLayoutCreateInfo _layoutCreateInfo = new DescriptorSetLayoutCreateInfo()
                {
                    SType = StructureType.DescriptorSetLayoutCreateInfo,
                    BindingCount = (uint)_bindingList.Count,
                    PBindings = _bindingsPtr,
                    PNext = &_setLayoutBindingFlags
                };
                if (_vulkan.CreateDescriptorSetLayout(_logicalDevice, ref _layoutCreateInfo, null, _descSetLayoutPtr) != Result.Success)
                {
                    throw new Exception("Failed to create descriptor set layout");
                }
            }
        }
        internal void CreateDescriptorSetLayout(int _bindingCount, List<DescriptorType> _descriptorTypes, List<ShaderStageFlags> _stageFlags, ref DescriptorSetLayout _dsl, DescriptorBindingFlags[] indexedFlags, uint[] descriptorCount)
        {
            List<DescriptorSetLayoutBinding> _bindingList = new List<DescriptorSetLayoutBinding>();
            for (int i = 0; i < _bindingCount; i++)
            {
                DescriptorSetLayoutBinding _binding = new DescriptorSetLayoutBinding()
                {
                    Binding = (uint)i,
                    DescriptorCount = descriptorCount[i],
                    DescriptorType = _descriptorTypes[i],
                    PImmutableSamplers = null,
                    StageFlags = _stageFlags[i]
                };
                _bindingList.Add(_binding);
            }
            var _b = _bindingList.ToArray();
            fixed (DescriptorBindingFlags* _indexedPtr = indexedFlags)
            fixed (DescriptorSetLayoutBinding* _bindingsPtr = _b)
            fixed (DescriptorSetLayout* _descSetLayoutPtr = &_dsl)
            {
                DescriptorSetLayoutBindingFlagsCreateInfo _setLayoutBindingFlags = new()
                {
                    SType = StructureType.DescriptorSetLayoutBindingFlagsCreateInfoExt,
                    BindingCount = (uint)indexedFlags.Length,
                    PBindingFlags = _indexedPtr
                };

                DescriptorSetLayoutCreateInfo _layoutCreateInfo = new DescriptorSetLayoutCreateInfo()
                {
                    SType = StructureType.DescriptorSetLayoutCreateInfo,
                    BindingCount = (uint)_bindingList.Count,
                    PBindings = _bindingsPtr,
                    PNext = &_setLayoutBindingFlags
                };
                if (_vulkan.CreateDescriptorSetLayout(_logicalDevice, ref _layoutCreateInfo, null, _descSetLayoutPtr) != Result.Success)
                {
                    throw new Exception("Failed to create descriptor set layout");
                }
            }
        }

        internal virtual void Draw() { }

        internal void CreateSyncObjects()
        {
            _imageAvailableSemaphores = new Semaphore[MAX_FRAMES_IN_FLIGHT];
            _renderFinishedSemaphores = new Semaphore[MAX_FRAMES_IN_FLIGHT];
            _fencesInFlight = new Fence[MAX_FRAMES_IN_FLIGHT];
            _imagesInFlight = new Fence[_swapchain._swapchainImages.Length];

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
                if (_vulkan.CreateSemaphore(_logicalDevice, ref _semaphoreCreateInfo, null, out _imageAvailableSemaphores[i]) != Result.Success ||
                    _vulkan.CreateSemaphore(_logicalDevice, ref _semaphoreCreateInfo, null, out _renderFinishedSemaphores[i]) != Result.Success ||
                    _vulkan.CreateFence(_logicalDevice, ref _fenceCreateInfo, null, out _fencesInFlight[i]) != Result.Success)
                {
                    throw new Exception("Failed to create synch objects for a frame at index " + i);
                }
            }
        }
    }
}