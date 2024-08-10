using ArctisAurora.EngineWork.Renderer.Helpers;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan.Extensions.EXT;
using Silk.NET.Vulkan;
using System.Runtime.InteropServices;
using ArctisAurora.GameObject;

namespace ArctisAurora.EngineWork.Renderer
{
    enum RendererTypes
    {
        Rasterizer,
        Pathtracer,
        RadianceCascades
    }

    internal unsafe class VulkanRenderer
    {
        internal static VulkanRenderer _rendererInstance = null;
        internal static RendererTypes _rendererType;
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

        internal static AGlfwWindow _glWindow = new AGlfwWindow();  //GLFW window
        internal static Vk _vulkan = Vk.GetApi();                   //vulkan api
        internal static Instance _instance;                         //vulkan instance
        //
        internal static PhysicalDevice _gpu;
        internal static Device _logicalDevice;
        internal static QueueFamilyProperties[] _qfm;               //api queue properties
        //
        internal static CommandPool _commandPool;
        //
        internal static Queue _graphicsQueue;                       //api queue
        internal static Queue _presentQueue;                        //fuck knows what this is (ill ask chatgpt later)
        //

        internal void InitRenderer(RendererTypes _type)
        {
            _rendererType = _type;
            _extent = new Extent2D() { Height = (uint)_height, Width = (uint)_width };
            //end of prerequisites

            _glWindow.CreateWindow(ref _extent);            //create glfw window
            CreateVulkanInstance();                         //create Vulkan instance
            SetupDebugMessenger();
            _glWindow.CreateSurface();                      //create window surface
            ChoosePhysicalDevice();                         //we get the gpu
            switch (_rendererType)
            {
                case RendererTypes.Rasterizer:
                    {
                        Rasterizer _rasterizer = new Rasterizer();
                        break;
                    }
                case RendererTypes.Pathtracer:
                    {
                        Pathtracing _tracer = new Pathtracing();
                        break;
                    }
                default:
                    break;
            }
        }

        internal void CreateVulkanInstance()
        {
            ApplicationInfo _appInfo = new ApplicationInfo
            {
                SType = StructureType.ApplicationInfo,
                PApplicationName = (byte*)SilkMarshal.StringToPtr("VulkanApp"),
                ApplicationVersion = AVulkanHelper.Version(1, 0, 0),
                PEngineName = (byte*)SilkMarshal.StringToPtr("ArctisAurora"),
                EngineVersion = AVulkanHelper.Version(1, 0, 0),
                ApiVersion = Vk.Version13
            };

            uint _glfwExtensionCount;
            byte** _glfwExtensions = _glWindow._glfw.GetRequiredInstanceExtensions(out _glfwExtensionCount);
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
            int _graphicsIndex = AVulkanHelper.FindQueueFamilyIndex(ref _gpu, ref _qfm, QueueFlags.GraphicsBit);
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
                Result r = _vulkan.CreateDevice(_gpu, _deviceInfo, null, out _logicalDevice);

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

        internal virtual void AddEntityToRenderQueue(Entity _m) { }

        internal virtual void AddLightToRenderQueue(Entity _m) { }

        internal void CreateCommandPool()
        {
            int _queueFamilyIndex = AVulkanHelper.FindQueueFamilyIndex(ref _gpu, ref _qfm, QueueFlags.GraphicsBit);
            CommandPoolCreateInfo _createInfo = new CommandPoolCreateInfo()
            {
                SType = StructureType.CommandPoolCreateInfo,
                QueueFamilyIndex = (uint)_queueFamilyIndex,
            };
            if (_vulkan.CreateCommandPool(_logicalDevice, _createInfo, null, out _commandPool) != Result.Success)
            {
                throw new Exception("Failed to create command pool");
            }
        }

        internal virtual void Draw() { }
    }
}