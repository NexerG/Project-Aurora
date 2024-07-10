﻿using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using System.Runtime.InteropServices;
using Buffer = Silk.NET.Vulkan.Buffer;
using ArctisAurora.EngineWork.Rendering.Renderers.Renderer_Vulkan;
using ArctisAurora.EngineWork.ECS.RenderingComponents.Vulkan;
using Semaphore = Silk.NET.Vulkan.Semaphore;
using ArctisAurora.GameObject;
using ArctisAurora.CustomEntities;
using Silk.NET.Maths;
using Silk.NET.Vulkan.Extensions.EXT;

namespace ArctisAurora.EngineWork.Rendering.Renderers.Vulkan
{
    struct LightData
    {
        internal Vector3D<float> _pos;
        internal Vector4D<float> _color;
    }

    internal unsafe class VulkanRenderer
    {
        //early window setting
        internal int _width = 1280;
        internal int _height = 720;
        internal static Extent2D _extent;
        //
        internal static VulkanRenderer _rendererInstance = null;    //Engine renderer reference
        internal static AVulkanBufferHandler _bufferHandlerHelper = new AVulkanBufferHandler(); //just buffer helper
        //window & vulkan setup
        internal static AGlfwWindow _glWindow = new AGlfwWindow();  //GLFW window
        internal static Vk _vulkan = Vk.GetApi();                   //vulkan api
        //validation
        bool _isValidationLayers = true;
        private readonly string[] _validationLayers = new string[]
        {
            "VK_LAYER_KHRONOS_validation"
        };
        private ExtDebugUtils? _debugUtils;
        private DebugUtilsMessengerEXT _debugMessenger;
        //whole rendering pipeline variables
        internal static Instance _instance;                         //vulkan instance
        internal static AVulkanSwapchain? _swapchain;
        internal static AVulkanGraphicsPipeline _pipeline;
        //buffers
        private Framebuffer[] _framebuffer;
        private CommandBuffer[] _commandBuffer;
        internal static Buffer _lightBuffer;
        internal static DeviceMemory _lightBufferMemory;
        internal static Buffer[] _lightUBO;
        internal static DeviceMemory[] _lightUBOMemory;
        internal static CommandPool _commandPool;
        //descriptors
        internal static DescriptorSetLayout _descriptorSetLayout;
        internal static DescriptorPool _descriptorPool;
        internal static DescriptorSetLayout _descriptorSetLayoutShadow;
        internal static DescriptorPool _descriptorPoolShadow;
        //cpu - gpu sync variables
        private int MAX_FRAMES_IN_FLIGHT = 2;
        private int _currentFrame = 0;
        private Semaphore[] _imageAvailableSemaphores;
        private Semaphore[] _renderFinishedSemaphores;
        private Fence[] _fencesInFlight;
        private Fence[] _imagesInFlight;
        //
        private QueueFamilyProperties[] _qfm;               //api queue properties
        internal static Device _logicalDevice;              //the interface that will interact with the GPU
        internal static PhysicalDevice _gpu;                //gpu reference
        //
        internal static Queue _graphicsQueue;               //api queue
        private Queue _presentQueue;                        //fuck knows what this is (ill ask chatgpt later)
        //-------------------------------------
        internal static Sampler _textureSampler;
        //
        private List<Entity> _entitiesToRender = new List<Entity>();
        private List<Entity> _lightsToRender = new List<Entity>();
        internal static AVulkanCamera _camera;//camera

        public VulkanRenderer()
        {
            //some engine specific rendering prerequisites
            _rendererInstance = this;
            _extent = new Extent2D() { Height = (uint)_height, Width = (uint)_width };
            //end of prerequisites

            _glWindow.CreateWindow(ref _extent);                //create glfw window
            CreateVulkanInstance();                             //create Vulkan instance
            SetupDebugMessenger();
            _glWindow.CreateSurface();                          //create window surface
            ChoosePhysicalDevice();                             //we get the gpu
            CreateLogicalDevice();                              //abstract the gpu so we can communicate

            //getting the render queues ready
            int _graphicsQFamilyIndex = FindQueueFamilyIndex(QueueFlags.GraphicsBit);
            uint _presentSupportIndex = _glWindow.FindPresentSupportIndex(ref _qfm);
            _graphicsQueue = _vulkan.GetDeviceQueue(_logicalDevice, (uint)_graphicsQFamilyIndex, 0);
            _presentQueue = _vulkan.GetDeviceQueue(_logicalDevice, _presentSupportIndex, 0);

            //create the swapchain
            _swapchain = new AVulkanSwapchain(ref _glWindow._driverSurface, ref _glWindow._surface);
            _swapchain.DoSwapchainMethodSequence(ref _extent);        //swapchain methods for simplicity sake
            _camera = new AVulkanCamera();

            //initiate the draw command pipeline
            CreateDescriptorSetLayout();                    //
            CreateShadowDescriptorSetLayout();              //
            CreateGraphicsPipeline();                       //graphics pipeline
            _swapchain.CreateDepthImages();                 //
            CreateFrameBuffers();                           //frame buffers
            CreateCommandPool();                            //
            CreateImageSampler();                           //
            CreateDescriptorPool();                         //descriptor pool
            CreateShadowDescriptorPool();                   //descriptor pool for shadows

            //from this point the entities can be created
            CreateCommandBuffers();                         //the draw command sequence that'll be used for rendering

            CreateSyncObjects();                            //CPU - GPU sync logic
        }

        internal void AddEntityToRenderQueue(Entity _m)
        {
            _entitiesToRender.Add(_m);
        }

        internal void AddLighToRenderQueue(Entity _l)
        {
            _lightsToRender.Add(_l);
            if (_lightsToRender.Count == 1)
            {
                _bufferHandlerHelper.CreateLightsBuffer(ref _lightsToRender, ref _lightBuffer, ref _lightBufferMemory);
                _bufferHandlerHelper.CreateLightUBO(ref _lightUBO, ref _lightUBOMemory, 1);
            }
            else
            {
                _bufferHandlerHelper.RecreateLightsBuffer(ref _lightsToRender, ref _lightBuffer, ref _lightBufferMemory);
                foreach(Buffer b in  _lightUBO)
                    _vulkan.DestroyBuffer(_logicalDevice, b, null);
                _vulkan.FreeMemory(_logicalDevice, _lightBufferMemory, null);
                _bufferHandlerHelper.CreateLightUBO(ref _lightUBO, ref _lightUBOMemory, _lightsToRender.Count);
                //recreate descriptor sets cause we just nuked all the buffers
            }
        }

            private void CreateVulkanInstance()
        {
            ApplicationInfo _appInfo = new ApplicationInfo
            {
                SType = StructureType.ApplicationInfo,
                PApplicationName = (byte*)SilkMarshal.StringToPtr("VulkanApp"),
                ApplicationVersion = VkVersion(1, 0, 0),
                PEngineName = (byte*)SilkMarshal.StringToPtr("ArctisAurora"),
                EngineVersion = VkVersion(1, 0, 0),
                ApiVersion = Vk.Version13
            };

            uint _glfwExtensionCount;
            byte** _glfwExtensions = _glWindow._glfw.GetRequiredInstanceExtensions(out _glfwExtensionCount);
            var _extensions = SilkMarshal.PtrToStringArray((nint)_glfwExtensions, (int) _glfwExtensionCount);
            if (_isValidationLayers)
            {
                _extensions = _extensions.Append(ExtDebugUtils.ExtensionName).ToArray();
            }
            //_extensions = _extensions.Append("VK_EXT_robustness2").ToArray();

            // Create Vulkan instance info
            InstanceCreateInfo createInfo = new InstanceCreateInfo
            {
                SType = StructureType.InstanceCreateInfo,
                PApplicationInfo = &_appInfo,
                EnabledExtensionCount = _glfwExtensionCount + 1,
                PpEnabledExtensionNames = (byte**)SilkMarshal.StringArrayToPtr(_extensions)
            };

            if (_isValidationLayers)
            {
                createInfo.EnabledLayerCount = (uint)_validationLayers.Length;
                createInfo.PpEnabledLayerNames = (byte**)SilkMarshal.StringArrayToPtr(_validationLayers);
                DebugUtilsMessengerCreateInfoEXT debugCreateInfo = new();
                PopulateDebugMessengerCreateInfo(ref  debugCreateInfo);
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

        private void SetupDebugMessenger()
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

        private uint DebugCallback(DebugUtilsMessageSeverityFlagsEXT messageSeverity, DebugUtilsMessageTypeFlagsEXT messageTypes, DebugUtilsMessengerCallbackDataEXT* pCallbackData, void* pUserData)
        {
            Console.WriteLine($"validation layer:" + Marshal.PtrToStringAnsi((nint)pCallbackData->PMessage));

            return Vk.False;
        }

        private void ChoosePhysicalDevice()
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

        private int FindQueueFamilyIndex(QueueFlags _qType)
        {
            uint _propertyCount = 0;
            _vulkan.GetPhysicalDeviceQueueFamilyProperties(_gpu, &_propertyCount, null);
            _qfm = new QueueFamilyProperties[_propertyCount];

            _vulkan.GetPhysicalDeviceQueueFamilyProperties(_gpu, &_propertyCount, _qfm);
            for (int i = 0; i < _propertyCount; i++)
                if ((_qfm[i].QueueFlags & _qType) == _qType)
                    return i;

            return int.MaxValue;
        }

        private void CreateLogicalDevice()
        {
            int _graphicsIndex = FindQueueFamilyIndex(QueueFlags.GraphicsBit);
            float _qPriority = 1.0f;
            DeviceQueueCreateInfo _qCreateInfo = new DeviceQueueCreateInfo
            {
                SType = StructureType.DeviceQueueCreateInfo,
                QueueFamilyIndex = (uint)_graphicsIndex,
                QueueCount = 1,
                PQueuePriorities = &_qPriority
            };

            PhysicalDeviceFeatures _deviceFeatures = new PhysicalDeviceFeatures()
            {
                SamplerAnisotropy = true,
            };
            PhysicalDeviceVulkan12Features _vulkan12FT = new PhysicalDeviceVulkan12Features()
            {
                SType = StructureType.PhysicalDeviceVulkan12Features,
                RuntimeDescriptorArray = true
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

            string[] requiredExtensions = { "VK_KHR_swapchain" };

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
                    PEnabledFeatures = &_deviceFeatures,
                    PNext = &_vulkan12FT
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

        private void RecreateSwapChain()
        {
            //cleanup
            _glWindow.UpdateWindowSize(ref _extent);
            _vulkan.DeviceWaitIdle(_logicalDevice);
            CleanUpSwapChain();
            //visuals
            _swapchain.CreateSwapchain(ref _extent);
            _swapchain.CreateImageView();
            _swapchain.CreateRenderPass();
            _swapchain.CreateShadowmapRenderPass();
            CreateGraphicsPipeline();
            CreateFrameBuffers();
            //api calls
            CreateDescriptorPool();
            CreateShadowDescriptorPool();
            for (int i = 0; i < _entitiesToRender.Count; i++) 
            {
                _entitiesToRender[i].GetComponent<AVulkanMeshComponent>().CreateDescriptorSet();
                _entitiesToRender[i].GetComponent<AVulkanMeshComponent>().CreateShadowDescriptorSet();
            }
            RecreateCommandBuffers();

            _imagesInFlight = new Fence[_swapchain._swapchainImages.Length];
        }

        private void CreateGraphicsPipeline()
        {
            _pipeline = new AVulkanGraphicsPipeline();
            _pipeline.CreateGraphicsPipeline("vulkan.vert.spv", "vulkan.frag.spv", _extent, ref _descriptorSetLayout);
            _pipeline.CreateShadwomapPipeline("Shadowmap.vert.spv", "Shadowmap.frag.spv", new Extent2D(1000,1000), ref _descriptorSetLayoutShadow);
        }

        private void CreateFrameBuffers()
        {
            _framebuffer = new Framebuffer[_swapchain!._imageViews.Length];
            for (int i = 0; i < _swapchain._imageViews.Length; i++)
            {
                var _attachment = new[] { _swapchain._imageViews[i], _swapchain._depthView };

                fixed (ImageView* _imAttachmentPtr = _attachment)
                {
                    FramebufferCreateInfo _framebufferInfo = new FramebufferCreateInfo()
                    {
                        SType = StructureType.FramebufferCreateInfo,
                        RenderPass = _swapchain._renderPass,
                        AttachmentCount = (uint)_attachment.Length,
                        PAttachments = _imAttachmentPtr,
                        Width = _extent.Width,
                        Height = _extent.Height,
                        Layers = 1
                    };
                    if (_vulkan.CreateFramebuffer(_logicalDevice, _framebufferInfo, null, out _framebuffer[i]) != Result.Success)
                    {
                        throw new Exception("Failed to create frame buffer");
                    }
                }
            }
        }

        private void CreateCommandPool()
        {
            int _queueFamilyIndex = FindQueueFamilyIndex(QueueFlags.GraphicsBit);
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

        public void RecreateCommandBuffers()
        {
            fixed (CommandBuffer* CBPtr = _commandBuffer)
            {
                _vulkan.FreeCommandBuffers(_logicalDevice, _commandPool, (uint)_commandBuffer.Length, CBPtr);
            }
            CreateCommandBuffers();
        }

        private void CreateCommandBuffers()
        {
            _commandBuffer = new CommandBuffer[_framebuffer.Length];

            CommandBufferAllocateInfo _allocInfo = new CommandBufferAllocateInfo()
            {
                SType = StructureType.CommandBufferAllocateInfo,
                CommandPool = _commandPool,
                Level = CommandBufferLevel.Primary,
                CommandBufferCount = (uint)_commandBuffer.Length
            };
            fixed (CommandBuffer* _commandBufferPtr = _commandBuffer)
            {
                Result r = _vulkan.AllocateCommandBuffers(_logicalDevice, _allocInfo, _commandBufferPtr);
                if (r != Result.Success)
                {
                    throw new Exception("Failed to allocate command buffer with error " + r);
                }
            }
            for (int i = 0; i < _commandBuffer.Length; i++)
            {
                CommandBufferBeginInfo _beginInfo = new CommandBufferBeginInfo()
                {
                    SType = StructureType.CommandBufferBeginInfo
                };

                if (_vulkan.BeginCommandBuffer(_commandBuffer[i], _beginInfo) != Result.Success)
                {
                    throw new Exception("Failed to create BEGIN command buffer at index " + i);
                }
                //normal render pass info
                RenderPassBeginInfo _renderPassInfo = new RenderPassBeginInfo()
                {
                    SType = StructureType.RenderPassBeginInfo,
                    RenderPass = _swapchain._renderPass,
                    Framebuffer = _framebuffer[i],
                    RenderArea =
                    {
                        Offset = { X = 0, Y=0 },
                        Extent = _extent
                    }
                };

                var _clearValues = new ClearValue[]
                {
                    new ClearValue()
                    {
                        Color = new ClearColorValue() { Float32_0 = 0.05f, Float32_1 = 0.05f, Float32_2 = 0.05f, Float32_3 = 1f },
                    },
                    new ClearValue()
                    {
                        DepthStencil = new ClearDepthStencilValue() { Depth = 1f, Stencil = 0 }
                    },
                };

                fixed(ClearValue* _clrValuesPtr = _clearValues)
                {
                    _renderPassInfo.ClearValueCount = (uint)_clearValues.Length;
                    _renderPassInfo.PClearValues = _clrValuesPtr;
                }
                //shadow render pass info
                var _shadowClearValues = new ClearValue[]
                {
                    new ClearValue()
                    {
                        DepthStencil = new ClearDepthStencilValue() { Depth = 1f, Stencil = 0 }
                    }
                };

                //render code
                Buffer[] _vertBuffers = new Buffer[_entitiesToRender.Count + _entitiesToRender.Count * _lightsToRender.Count];
                _vulkan.CmdBindPipeline(_commandBuffer[i], PipelineBindPoint.Graphics, _pipeline._shadowPipeline);
                for (int j = 0; j < _lightsToRender.Count; j++)
                {
                    RenderPassBeginInfo _shadowPassInfo = new RenderPassBeginInfo()
                    {
                        SType = StructureType.RenderPassBeginInfo,
                        RenderPass = _swapchain._shadowmapRenderPass,
                        Framebuffer = _lightsToRender[j].GetComponent<AVulkanLightsourceComponent>()._shadowFramebuffer,
                        RenderArea =
                        {
                            Offset = { X = 0, Y=0 },
                            Extent = new Extent2D(1000, 1000)
                        },
                    };
                    fixed (ClearValue* _clrValuesPtr = _shadowClearValues)
                    {
                        _shadowPassInfo.ClearValueCount = (uint)_shadowClearValues.Length;
                        _shadowPassInfo.PClearValues = _clrValuesPtr;

                        _vulkan.CmdBeginRenderPass(_commandBuffer[i], &_shadowPassInfo, SubpassContents.Inline);
                    }
                    //shadow maps
                    for (int e = 0; e < _entitiesToRender.Count; e++)
                    {
                        _vertBuffers[e] = _entitiesToRender[e].GetComponent<AVulkanMeshComponent>()._vertexBuffer;
                        var _offset = new ulong[] { 0 };
                        _entitiesToRender[e].GetComponent<AVulkanMeshComponent>().EnqueuShadowDrawCommands(_offset, i, ref _commandBuffer[i], e);
                    }
                    _vulkan.CmdEndRenderPass(_commandBuffer[i]);
                }

                //player view
                _vulkan.CmdBindPipeline(_commandBuffer[i], PipelineBindPoint.Graphics, _pipeline._graphicsPipeline);
                _vulkan.CmdBeginRenderPass(_commandBuffer[i], &_renderPassInfo, SubpassContents.Inline);
                for (int e = 0; e < _entitiesToRender.Count; e++)
                {
                    _vertBuffers[e] = _entitiesToRender[e].GetComponent<AVulkanMeshComponent>()._vertexBuffer;
                    var _offset = new ulong[] { 0 };
                    _entitiesToRender[e].GetComponent<AVulkanMeshComponent>().EnqueueDrawCommands(_offset, i, ref _commandBuffer[i]);
                }

                //end of for loop
                _vulkan.CmdEndRenderPass(_commandBuffer[i]);
                //done rendering

                if (_vulkan.EndCommandBuffer(_commandBuffer[i]) != Result.Success)
                {
                    throw new Exception("Failed to record command buffer");
                }
            }
        }

        private void CreateDescriptorSetLayout()
        {
            DescriptorSetLayoutBinding _uboLayoutBinding = new DescriptorSetLayoutBinding()
            {
                Binding = 0,
                DescriptorCount = 1,
                DescriptorType = DescriptorType.UniformBuffer,
                PImmutableSamplers = null,
                StageFlags = ShaderStageFlags.VertexBit
            };

            DescriptorSetLayoutBinding _matrixLayoutBinding = new DescriptorSetLayoutBinding()
            {
                Binding = 1,
                DescriptorCount = 1,
                DescriptorType = DescriptorType.StorageBuffer,
                PImmutableSamplers = null,
                StageFlags = ShaderStageFlags.VertexBit
            };

            DescriptorSetLayoutBinding _lightLayoutBinding = new DescriptorSetLayoutBinding()
            {
                Binding = 2,
                DescriptorCount = 1,
                DescriptorType = DescriptorType.StorageBuffer,
                PImmutableSamplers = null,
                StageFlags = ShaderStageFlags.FragmentBit
            };

            DescriptorSetLayoutBinding _samplerLayoutBinding = new DescriptorSetLayoutBinding()
            {
                Binding = 3,
                DescriptorCount = 1,
                DescriptorType = DescriptorType.CombinedImageSampler,
                PImmutableSamplers = null,
                StageFlags = ShaderStageFlags.FragmentBit
            };

            var _bindings = new DescriptorSetLayoutBinding[] { _uboLayoutBinding, _matrixLayoutBinding, _lightLayoutBinding, _samplerLayoutBinding};
            fixed(DescriptorSetLayoutBinding* _bindingsPtr = _bindings)
            fixed (DescriptorSetLayout* _descSetLayoutPtr = &_descriptorSetLayout)
            {
                DescriptorSetLayoutCreateInfo _layoutCreateInfo = new DescriptorSetLayoutCreateInfo()
                {
                    SType = StructureType.DescriptorSetLayoutCreateInfo,
                    BindingCount = (uint)_bindings.Length,
                    PBindings = _bindingsPtr,
                };
                if (_vulkan.CreateDescriptorSetLayout(_logicalDevice, _layoutCreateInfo, null, _descSetLayoutPtr) != Result.Success)
                {
                    throw new Exception("Failed to create descriptor set layout");
                }
            }
        }

        private void CreateShadowDescriptorSetLayout()
        {
            DescriptorSetLayoutBinding _uboLayoutBinding = new DescriptorSetLayoutBinding()
            {
                Binding = 0,
                DescriptorCount = 1,
                DescriptorType = DescriptorType.StorageBuffer,
                PImmutableSamplers = null,
                StageFlags = ShaderStageFlags.VertexBit
            };

            DescriptorSetLayoutBinding _matrixLayoutBinding = new DescriptorSetLayoutBinding()
            {
                Binding = 1,
                DescriptorCount = 1,
                DescriptorType = DescriptorType.StorageBuffer,
                PImmutableSamplers = null,
                StageFlags = ShaderStageFlags.VertexBit
            };

            var _bindings = new DescriptorSetLayoutBinding[] { _uboLayoutBinding, _matrixLayoutBinding };
            fixed (DescriptorSetLayoutBinding* _bindingsPtr = _bindings)
            fixed (DescriptorSetLayout* _descSetLayoutPtr = &_descriptorSetLayoutShadow)
            {
                DescriptorSetLayoutCreateInfo _layoutCreateInfo = new DescriptorSetLayoutCreateInfo()
                {
                    SType = StructureType.DescriptorSetLayoutCreateInfo,
                    BindingCount = (uint)_bindings.Length,
                    PBindings = _bindingsPtr,
                };
                if (_vulkan.CreateDescriptorSetLayout(_logicalDevice, _layoutCreateInfo, null, _descSetLayoutPtr) != Result.Success)
                {
                    throw new Exception("Failed to create descriptor set layout");
                }
            }
        }

        private void CreateDescriptorPool()
        {
            var _poolSizes = new DescriptorPoolSize[]
            {
                new DescriptorPoolSize()
                {
                    Type = DescriptorType.UniformBuffer,
                    DescriptorCount = (uint)_swapchain._swapchainImages.Length
                },
                new DescriptorPoolSize()
                {
                    Type = DescriptorType.CombinedImageSampler,
                    DescriptorCount = (uint)_swapchain._swapchainImages.Length
                }
            };

            fixed (DescriptorPoolSize* _poolSizesPtr = _poolSizes)
            fixed (DescriptorPool* _descPoolPtr = &_descriptorPool)
            {
                DescriptorPoolCreateInfo _poolInfo = new DescriptorPoolCreateInfo()
                {
                    SType = StructureType.DescriptorPoolCreateInfo,
                    PoolSizeCount = (uint)_poolSizes.Length,
                    PPoolSizes = _poolSizesPtr,
                    MaxSets = (uint)_swapchain._swapchainImages.Length,
                    Flags = DescriptorPoolCreateFlags.FreeDescriptorSetBit
                };
                if (_vulkan.CreateDescriptorPool(_logicalDevice, _poolInfo, null, _descPoolPtr) != Result.Success)
                {
                    throw new Exception("Failed to create descriptor pool");
                }
            }
        }

        private void CreateShadowDescriptorPool()
        {
            var _poolSizes = new DescriptorPoolSize[]
            {
                new DescriptorPoolSize()
                {
                    Type = DescriptorType.StorageBuffer,
                    DescriptorCount = (uint)_swapchain!._swapchainImages.Length
                }
            };

            fixed (DescriptorPoolSize* _poolSizesPtr = _poolSizes)
            fixed (DescriptorPool* _descPoolPtr = &_descriptorPoolShadow)
            {
                DescriptorPoolCreateInfo _poolInfo = new DescriptorPoolCreateInfo()
                {
                    SType = StructureType.DescriptorPoolCreateInfo,
                    PoolSizeCount = (uint)_poolSizes.Length,
                    PPoolSizes = _poolSizesPtr,
                    MaxSets = (uint)_swapchain!._swapchainImages.Length,
                    Flags = DescriptorPoolCreateFlags.FreeDescriptorSetBit
                };
                if (_vulkan.CreateDescriptorPool(_logicalDevice, _poolInfo, null, _descPoolPtr) != Result.Success)
                {
                    throw new Exception("Failed to create descriptor pool");
                }
            }
        }

        private void CreateImageSampler()
        {
            _vulkan.GetPhysicalDeviceProperties(_gpu, out PhysicalDeviceProperties _properties);
            SamplerCreateInfo _createInfo = new SamplerCreateInfo()
            {
                SType = StructureType.SamplerCreateInfo,
                MagFilter = Filter.Linear,
                MinFilter = Filter.Linear,
                AddressModeU = SamplerAddressMode.Repeat,
                AddressModeV = SamplerAddressMode.Repeat,
                AddressModeW = SamplerAddressMode.Repeat,
                AnisotropyEnable = true,
                MaxAnisotropy = _properties.Limits.MaxSamplerAnisotropy,
                BorderColor = BorderColor.IntOpaqueBlack,
                UnnormalizedCoordinates = false,
                CompareEnable = false,
                CompareOp = CompareOp.Always,
                MipmapMode = SamplerMipmapMode.Linear
            };

            fixed (Sampler* _textureSamplerPtr = &_textureSampler)
            {
                Result r = _vulkan.CreateSampler(_logicalDevice, _createInfo, null, _textureSamplerPtr);
                if (r != Result.Success)
                {
                    throw new Exception("Failed to create a texture sampler with error: " + r);
                }
            }
        }

        internal void Draw()
        {
            _camera.ProcessKeyboard();
            _vulkan.WaitForFences(_logicalDevice, 1, _fencesInFlight[_currentFrame], true, ulong.MaxValue);
            uint _imageIndex = 0;
            Result r = _swapchain._driverSwapchain.AcquireNextImage(_logicalDevice, _swapchain._swapchainKHR, ulong.MaxValue, _imageAvailableSemaphores[_currentFrame], default, ref _imageIndex);

            if (r == Result.ErrorOutOfDateKhr)
            {
                RecreateSwapChain();
                return;
            }
            else if (r != Result.Success && r != Result.SuboptimalKhr)
            {
                throw new Exception("Failed to acquire swapchain image");
            }

            _camera.UpdateCameraMatrix(_extent, _imageIndex);
            if (_lightsToRender.Count > 0)
            {
                _bufferHandlerHelper.UpdateLightsBuffer(ref _lightsToRender, ref _lightBufferMemory);
                _bufferHandlerHelper.UpdateLightUniforms(ref _lightsToRender, _imageIndex, ref _lightUBOMemory);
            }
            //update uniforms
            foreach (Entity e in _lightsToRender)
            {
                e.GetComponent<AVulkanLightsourceComponent>().UpdateVPMatrices(_imageIndex);
            }
            foreach (Entity e in _entitiesToRender)
            {
                e.GetComponent<AVulkanMeshComponent>().UpdateMatrices();
            }

            if (_imagesInFlight[_imageIndex].Handle != default)
            {
                _vulkan.WaitForFences(_logicalDevice, 1, _imagesInFlight[_imageIndex], true, ulong.MaxValue);
            }
            _imagesInFlight[_imageIndex] = _fencesInFlight[_currentFrame];

            SubmitInfo _submitInfo = new SubmitInfo()
            {
                SType = StructureType.SubmitInfo
            };

            var _waitSemaphores = stackalloc[]
            {
                _imageAvailableSemaphores[_currentFrame]
            };
            var _waitStages = stackalloc[]
            {
                PipelineStageFlags.ColorAttachmentOutputBit
            };

            CommandBuffer _buffer = _commandBuffer[_imageIndex];
            _submitInfo = _submitInfo with
            {
                WaitSemaphoreCount = 1,
                PWaitSemaphores = _waitSemaphores,
                PWaitDstStageMask = _waitStages,

                CommandBufferCount = 1,
                PCommandBuffers = &_buffer
            };

            var _signalSemaphores = stackalloc[]
            {
                _renderFinishedSemaphores[_currentFrame]
            };

            _submitInfo = _submitInfo with
            {
                SignalSemaphoreCount = 1,
                PSignalSemaphores = _signalSemaphores
            };

            _vulkan.ResetFences(_logicalDevice, 1, _fencesInFlight[_currentFrame]);
            r = _vulkan.QueueSubmit(_graphicsQueue, 1, _submitInfo, _fencesInFlight[_currentFrame]);
            if (r != Result.Success)
            {
                throw new Exception("Failed to send command buffer to the GPU with error code:" + r);
            }

            var _swapChains = stackalloc[] { _swapchain._swapchainKHR };
            PresentInfoKHR _presentInfo = new PresentInfoKHR()
            {
                SType = StructureType.PresentInfoKhr,
                WaitSemaphoreCount = 1,
                PWaitSemaphores = _signalSemaphores,
                SwapchainCount = 1,
                PSwapchains = _swapChains,
                PImageIndices = &_imageIndex
            };
            r = _swapchain._driverSwapchain.QueuePresent(_presentQueue, _presentInfo);
            if (r == Result.ErrorOutOfDateKhr || r == Result.SuboptimalKhr || _glWindow._frameBufferResized)
            {
                _glWindow._frameBufferResized = false;
                RecreateSwapChain();
            }
            else if (r != Result.Success)
            {
                throw new Exception("Failed to present swap chain image");
            }

            _currentFrame = (_currentFrame + 1) % MAX_FRAMES_IN_FLIGHT;
        }

        private void CreateSyncObjects()
        {
            _imageAvailableSemaphores = new Silk.NET.Vulkan.Semaphore[MAX_FRAMES_IN_FLIGHT];
            _renderFinishedSemaphores = new Silk.NET.Vulkan.Semaphore[MAX_FRAMES_IN_FLIGHT];
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
                if (_vulkan.CreateSemaphore(_logicalDevice, _semaphoreCreateInfo, null, out _imageAvailableSemaphores[i]) != Result.Success ||
                    _vulkan.CreateSemaphore(_logicalDevice, _semaphoreCreateInfo, null, out _renderFinishedSemaphores[i]) != Result.Success ||
                    _vulkan.CreateFence(_logicalDevice, _fenceCreateInfo, null, out _fencesInFlight[i]) != Result.Success)
                {
                    throw new Exception("Failed to create synch objects for a frame at index " + i);
                }
            }
        }

        private void CleanUpSwapChain()
        {
            foreach (var fb in _framebuffer)
            {
                _vulkan.DestroyFramebuffer(_logicalDevice, fb, null);
            }
            fixed (CommandBuffer* CBPtr = _commandBuffer)
            {
                _vulkan.FreeCommandBuffers(_logicalDevice, _commandPool, (uint)_commandBuffer.Length, CBPtr);
            }

            _pipeline.DestroyPipeline();
            _vulkan.DestroyRenderPass(_logicalDevice, _swapchain._renderPass, null);
            _vulkan.DestroyRenderPass(_logicalDevice, _swapchain._shadowmapRenderPass, null);
            _swapchain.DestroySwapchain();
        }

        private static uint VkVersion(uint major, uint minor, uint patch)
        {
            return major << 22 | minor << 12 | patch;
        }
    }
}