using Silk.NET.Core.Native;
using Silk.NET.GLFW;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using Silk.NET.Vulkan.Extensions.EXT;
using System.Runtime.InteropServices;
using System.Text;
using Image = Silk.NET.Vulkan.Image;
using OpenTK.Platform.Windows;
using Silk.NET.Windowing;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.ListView;
using ImageLayout = Silk.NET.Vulkan.ImageLayout;

namespace ArctisAurora.EngineWork.Rendering.Renderers.Vulkan
{
    struct SwapChainSupportDetails
    {
        public SurfaceCapabilitiesKHR Capabilities;
        public SurfaceFormatKHR[] Formats;
        public PresentModeKHR[] PresentModes;
    }

    internal unsafe class VulkanRenderer
    {
        internal int _width = 1280;
        internal int _height = 720;
        internal static VulkanRenderer _rendererInstance = null;
        //window & vulkan setup
        private static Glfw _glfw = Glfw.GetApi();  //window(GLFW) api
        private static Vk _vulkan = Vk.GetApi();    //vulkan api

        private static Instance _instance;          //vulkan instance
        private KhrSurface _vkSurface;              //vulkan driver extension surface
        private SurfaceKHR _surface;                //vulkan surface
        private static WindowHandle* _windowHandle; //window handle to interact with vulkan
        private IWindow _window;
        private SwapchainKHR _swapchain;            //swapchain reference
        KhrSwapchain _swapchainExtension;           //swapchain driver extension
        Image[] _swapChainImages;
        ImageView[] _imageViews;
        SurfaceFormatKHR _surfaceFormat;
        Extent2D _extent;
        RenderPass _renderPass;
        Pipeline _graphicsPipeline;
        Framebuffer[] _framebuffer;
        CommandBuffer[] _commandBuffer;
        CommandPool _commandPool;

        int MAX_FRAMES_IN_FLIGHT = 2;
        int _currentFrame = 0;
        Silk.NET.Vulkan.Semaphore[] _imageAvailableSemaphores;
        Silk.NET.Vulkan.Semaphore[] _renderFinishedSemaphores;
        Fence[] _fencesInFlight;
        Fence[] _imagesInFlight;

        QueueFamilyProperties[] _qfm;               //api queue properties
        Device _logicalDevice;                      //the interface that will interact with the GPU
        PhysicalDevice _gpu;                        //gpu reference
        Queue _graphicsQueue;                       //api queue
        Queue _presentQueue;

        V_Shader _pipeline;
        public VulkanRenderer()
        {
            //The Window
            CreateGLFWInstance();
            //Vulkan
            CreateVulkanInstance();

            VkNonDispatchableHandle _surfaceHandle;
            _glfw.CreateWindowSurface(_instance.ToHandle(), _windowHandle, null, &_surfaceHandle);
            _surface = _surfaceHandle.ToSurface();
            CreateSurface();

            ChoosePhysicalDevice();
            CreateLogicalDevice(_gpu);
            int _graphicsQFamilyIndex = FindQueueFamilyIndex(_gpu, QueueFlags.GraphicsBit);
            uint _presentSupportIndex = FindPresentSuppotIndex();
            _graphicsQueue = _vulkan.GetDeviceQueue(_logicalDevice, (uint)_graphicsQFamilyIndex, 0);
            _presentQueue = _vulkan.GetDeviceQueue(_logicalDevice, _presentSupportIndex, 0);
            CreateSwapChain();

            uint _imageCount = 0;
            //Span<Image> images = new Span<Image>();
            _swapchainExtension.GetSwapchainImages(_logicalDevice, _swapchain, &_imageCount, null);
            _swapChainImages = new Image[_imageCount];
            fixed (Image* _imagePtr = _swapChainImages)
            {
                _swapchainExtension.GetSwapchainImages(_logicalDevice, _swapchain, &_imageCount, _imagePtr);
            }
            CreateImageView();
            CreateRenderPass();
            CreateGraphicsPipeline();
            CreateFrameBuffers();
            CreateCommandPool();
            CreateCommandBuffers();
            CreateSyncObjects();
        }

        private void CreateSurface()
        {
            if (!_vulkan!.TryGetInstanceExtension(_instance, out _vkSurface))
            {
                throw new NotSupportedException("KHR_surface extension not found.");
            }
        }

        private void CreateGLFWInstance()
        {
            if (!_glfw.Init())
                Console.WriteLine("Failed to initialize GLFW");

            _glfw.WindowHint(WindowHintClientApi.ClientApi, ClientApi.NoApi);
            _windowHandle = _glfw.CreateWindow(_width, _height, "Arctis Auora", null, null);

            if (_windowHandle == null)
            {
                Console.WriteLine("Failed to create window");
                _glfw.Terminate();
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
            byte** _glfwExtensions = _glfw.GetRequiredInstanceExtensions(out _glfwExtensionCount);

            // Create Vulkan instance info
            InstanceCreateInfo createInfo = new InstanceCreateInfo
            {
                SType = StructureType.InstanceCreateInfo,
                PApplicationInfo = &_appInfo,
                EnabledExtensionCount = _glfwExtensionCount,
                PpEnabledExtensionNames = _glfwExtensions
            };

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
            SilkMarshal.Free((nint)createInfo.PpEnabledLayerNames);
        }

        private static uint VkVersion(uint major, uint minor, uint patch)
        {
            return major << 22 | minor << 12 | patch;
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

        private bool DoesSupport(PhysicalDevice _gpu)
        {
            ExtensionProperties _extentionProperties;
            uint _extensionCount = 0;
            byte _layerName = 0;
            _vulkan.EnumerateDeviceExtensionProperties(_gpu, &_layerName, &_extensionCount, null);
            if (_extensionCount == 0)
            {
                Console.WriteLine("No device extensions found");
                return false;
            }
            return true;
        }

        private int FindQueueFamilyIndex(PhysicalDevice _gpu, QueueFlags _qType)
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
        private uint FindPresentSuppotIndex()
        {
            uint i = 0;
            foreach(var _qf in _qfm)
            {
                if(_qf.QueueFlags.HasFlag(QueueFlags.GraphicsBit))
                {
                    _vkSurface.GetPhysicalDeviceSurfaceSupport(_gpu, i, _surface, out var _presentSupport);
                    if(_presentSupport)
                    {
                        return i;
                    }
                }
                i++;
            }
            return int.MaxValue;
        }

        private void CreateLogicalDevice(PhysicalDevice _gpu)
        {
            int _graphicsIndex = FindQueueFamilyIndex(_gpu, QueueFlags.GraphicsBit);
            float _qPriority = 1.0f;
            DeviceQueueCreateInfo _qCreateInfo = new DeviceQueueCreateInfo
            {
                SType = StructureType.DeviceQueueCreateInfo,
                QueueFamilyIndex = (uint)_graphicsIndex,
                QueueCount = 1,
                PQueuePriorities = &_qPriority
            };

            PhysicalDeviceFeatures _deviceFeatures = new PhysicalDeviceFeatures();
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
                    PpEnabledExtensionNames = (byte**)ppEnabledExtensions
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

        private void CreateSwapChain()
        {
            SwapChainSupportDetails _support = QuerySwapChainSupport(_gpu);

            _surfaceFormat = ChooseSwapSurfaceFormat(_support.Formats);
            PresentModeKHR _presentMode = ChoosePresentMode(_support.PresentModes);
            _extent = new Extent2D
            {
                Width = (uint)_width,
                Height = (uint)_height
            };
            uint _imageCount = _support.Capabilities.MinImageCount + 1;


            SwapchainCreateInfoKHR _swapChainInfo = new SwapchainCreateInfoKHR()
            {
                SType = StructureType.SwapchainCreateInfoKhr,
                Surface = _surface,

                MinImageCount = _imageCount,
                ImageFormat = _surfaceFormat.Format,
                ImageColorSpace = _surfaceFormat.ColorSpace,
                ImageExtent = _extent,
                ImageArrayLayers = 1,
                ImageUsage = ImageUsageFlags.ColorAttachmentBit,
                ImageSharingMode = SharingMode.Exclusive,
                PresentMode = _presentMode,
                Clipped = true,
                OldSwapchain = default,
                CompositeAlpha = CompositeAlphaFlagsKHR.OpaqueBitKhr,
                PreTransform = _support.Capabilities.CurrentTransform
            };

            if (!_vulkan!.TryGetDeviceExtension(_instance, _logicalDevice, out _swapchainExtension))
            {
                throw new NotSupportedException("VK_KHR_swapchain extension not found."); ;
            }

            Result r = _swapchainExtension!.CreateSwapchain(_logicalDevice, _swapChainInfo, null, out _swapchain);
            if (r != Result.Success)
            {
                throw new Exception("Failed to create swapchain " + r);
            }
        }

        private SwapChainSupportDetails QuerySwapChainSupport(PhysicalDevice _physicalDevice)
        {
            var _details = new SwapChainSupportDetails();

            _vkSurface!.GetPhysicalDeviceSurfaceCapabilities(_physicalDevice, _surface, out _details.Capabilities);

            //surface formats
            uint _formatCount = 0;
            _vkSurface.GetPhysicalDeviceSurfaceFormats(_physicalDevice, _surface, ref _formatCount, null);
            if (_formatCount != 0)
            {
                _details.Formats = new SurfaceFormatKHR[_formatCount];
                fixed (SurfaceFormatKHR* _fPtr = _details.Formats)
                {
                    _vkSurface.GetPhysicalDeviceSurfaceFormats(_physicalDevice, _surface, ref _formatCount, _fPtr);
                }
            }
            else _details.Formats = Array.Empty<SurfaceFormatKHR>();

            //present modes
            uint _presentModeCount = 0;
            _vkSurface.GetPhysicalDeviceSurfacePresentModes(_physicalDevice, _surface, ref _presentModeCount, null);
            if (_presentModeCount != 0)
            {
                _details.PresentModes = new PresentModeKHR[_presentModeCount];
                fixed (PresentModeKHR* _formatsPtr = _details.PresentModes)
                {
                    _vkSurface.GetPhysicalDeviceSurfacePresentModes(_physicalDevice, _surface, ref _presentModeCount, _formatsPtr);
                }
            }
            else _details.PresentModes = Array.Empty<PresentModeKHR>();

            return _details;
        }

        private SurfaceFormatKHR ChooseSwapSurfaceFormat(IReadOnlyList<SurfaceFormatKHR> availableFormats)
        {
            foreach (var availableFormat in availableFormats)
            {
                if (availableFormat.Format == Format.B8G8R8A8Srgb && availableFormat.ColorSpace == ColorSpaceKHR.SpaceSrgbNonlinearKhr)
                {
                    return availableFormat;
                }
            }
            return availableFormats[0];
        }

        private PresentModeKHR ChoosePresentMode(IReadOnlyList<PresentModeKHR> availablePresentModes)
        {
            foreach (var availablePresentMode in availablePresentModes)
            {
                if (availablePresentMode == PresentModeKHR.MailboxKhr)
                {
                    return availablePresentMode;
                }
            }
            return PresentModeKHR.FifoKhr;
        }

        private void CreateImageView()
        {
            _imageViews = new ImageView[_swapChainImages.Length];
            for(int i=0;i< _swapChainImages.Length;i++)
            {
                ImageViewCreateInfo _createInfo = new ImageViewCreateInfo
                {
                    Image = _swapChainImages[i],
                    Format = _surfaceFormat.Format,
                    ViewType = ImageViewType.Type2D
                };
                _createInfo.Components.R = ComponentSwizzle.Identity;
                _createInfo.Components.G = ComponentSwizzle.Identity;
                _createInfo.Components.B = ComponentSwizzle.Identity;
                _createInfo.Components.A = ComponentSwizzle.Identity;

                _createInfo.SubresourceRange.AspectMask = ImageAspectFlags.ColorBit;
                _createInfo.SubresourceRange.BaseMipLevel = 0;
                _createInfo.SubresourceRange.LevelCount = 1;
                _createInfo.SubresourceRange.BaseArrayLayer = 0;
                _createInfo.SubresourceRange.LayerCount = 1;

                if(_vulkan!.CreateImageView(_logicalDevice, _createInfo, null, out _imageViews[i])!= Result.Success)
                {
                    throw new Exception("failed to create image views!");
                }
            }
        }

        private void CreateRenderPass()
        {
            AttachmentDescription _colorAttachment = new AttachmentDescription()
            {
                Format = _surfaceFormat.Format,
                Samples = SampleCountFlags.Count1Bit,
                LoadOp = AttachmentLoadOp.Clear,
                StoreOp = AttachmentStoreOp.Store,
                StencilLoadOp = AttachmentLoadOp.DontCare,
                InitialLayout = ImageLayout.Undefined,
                FinalLayout = ImageLayout.PresentSrcKhr,
            };

            AttachmentReference _colorAttachmentRef = new AttachmentReference()
            {
                Attachment = 0,
                Layout = ImageLayout.ColorAttachmentOptimal,
            };

            SubpassDescription _subpass = new SubpassDescription()
            {
                PipelineBindPoint = PipelineBindPoint.Graphics,
                ColorAttachmentCount = 1,
                PColorAttachments = &_colorAttachmentRef,
            };

            RenderPassCreateInfo _renderPassInfo = new RenderPassCreateInfo()
            {
                SType = StructureType.RenderPassCreateInfo,
                AttachmentCount = 1,
                PAttachments = &_colorAttachment,
                SubpassCount = 1,
                PSubpasses = &_subpass,
            };

            if (_vulkan.CreateRenderPass(_logicalDevice, _renderPassInfo, null, out _renderPass) != Result.Success)
            {
                throw new Exception("failed to create render pass!");
            }
        }

        private void CreateGraphicsPipeline()
        {
            _pipeline = new V_Shader();
            _pipeline.CreateGraphicsPipeline("vert.spv", "frag.spv", _logicalDevice, _vulkan, _extent, _renderPass,ref _graphicsPipeline);
        }

        private void CreateFrameBuffers()
        {
            _framebuffer = new Framebuffer[_imageViews.Length];
            for(int i=0; i < _imageViews.Length; i++)
            {
                var _attachment = _imageViews[i];

                FramebufferCreateInfo _framebufferInfo = new FramebufferCreateInfo()
                {
                    SType = StructureType.FramebufferCreateInfo,
                    RenderPass = _renderPass,
                    AttachmentCount = 1,
                    PAttachments = &_attachment,
                    Width = _extent.Width,
                    Height = _extent.Height,
                    Layers = 1
                };

                if(_vulkan.CreateFramebuffer(_logicalDevice,_framebufferInfo,null,out _framebuffer[i]) != Result.Success)
                {
                    throw new Exception("Failed to create frame buffer");
                }
            }
        }

        private void CreateCommandPool()
        {
            int _queueFamilyIndex = FindQueueFamilyIndex(_gpu, QueueFlags.GraphicsBit);
            CommandPoolCreateInfo _createInfo = new CommandPoolCreateInfo()
            {
                SType = StructureType.CommandPoolCreateInfo,
                QueueFamilyIndex = (uint)_queueFamilyIndex,
            };
            if(_vulkan.CreateCommandPool(_logicalDevice, _createInfo,null,out _commandPool) != Result.Success)
            {
                throw new Exception("Failed to create command pool");
            }
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
            fixed(CommandBuffer* _commandBufferPtr = _commandBuffer)
            {
                Result r = _vulkan.AllocateCommandBuffers(_logicalDevice, _allocInfo, _commandBufferPtr);
                if (r !=Result.Success)
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

                if (_vulkan.BeginCommandBuffer(_commandBuffer[i],_beginInfo)!=Result.Success)
                {
                    throw new Exception("Failed to create BEGIN command buffer at index " + i);
                }

                RenderPassBeginInfo _renderPassInfo = new RenderPassBeginInfo()
                {
                    SType = StructureType.RenderPassBeginInfo,
                    RenderPass = _renderPass,
                    Framebuffer = _framebuffer[i],
                    RenderArea =
                    {
                        Offset = { X = 0, Y=0 },
                        Extent = _extent
                    }
                };

                ClearValue _clearColor = new ClearValue()
                {
                    Color = new() { Float32_0 = 0.12f, Float32_1 = 0.12f, Float32_2 = 0.12f, Float32_3 = 1 }
                };

                _renderPassInfo.ClearValueCount = 1;
                _renderPassInfo.PClearValues = &_clearColor;

                _vulkan.CmdBeginRenderPass(_commandBuffer[i], &_renderPassInfo, SubpassContents.Inline);
                _vulkan.CmdBindPipeline(_commandBuffer[i], PipelineBindPoint.Graphics, _graphicsPipeline);
                _vulkan.CmdDraw(_commandBuffer[i], 3, 1, 0, 0);
                _vulkan.CmdEndRenderPass(_commandBuffer[i]);

                if (_vulkan.EndCommandBuffer(_commandBuffer[i])!= Result.Success)
                {
                    throw new Exception("Failed to record command buffer");
                }
            }
        }

        private void CreateSyncObjects()
        {
            _imageAvailableSemaphores = new Silk.NET.Vulkan.Semaphore[MAX_FRAMES_IN_FLIGHT];
            _renderFinishedSemaphores = new Silk.NET.Vulkan.Semaphore[MAX_FRAMES_IN_FLIGHT];
            _fencesInFlight = new Fence[MAX_FRAMES_IN_FLIGHT];
            _imagesInFlight = new Fence[_swapChainImages.Length];

            SemaphoreCreateInfo _semaphoreCreateInfo = new SemaphoreCreateInfo()
            {
                SType = StructureType.SemaphoreCreateInfo
            };

            FenceCreateInfo _fenceCreateInfo = new FenceCreateInfo()
            {
                SType = StructureType.FenceCreateInfo,
                Flags = FenceCreateFlags.SignaledBit
            };

            for(int i=0;i<MAX_FRAMES_IN_FLIGHT;i++)
            {
                if (_vulkan.CreateSemaphore(_logicalDevice, _semaphoreCreateInfo, null, out _imageAvailableSemaphores[i]) != Result.Success ||
                    _vulkan.CreateSemaphore(_logicalDevice, _semaphoreCreateInfo, null, out _renderFinishedSemaphores[i]) != Result.Success ||
                    _vulkan.CreateFence(_logicalDevice, _fenceCreateInfo, null, out _fencesInFlight[i]) != Result.Success)
                {
                    throw new Exception("Failed to create synch objects for a frame at index " + i);
                }
            }
        }

        internal void Draw()
        {
            _vulkan.WaitForFences(_logicalDevice, 1, _fencesInFlight[_currentFrame], true, ulong.MaxValue);
            uint _imageIndex = 0;
            _swapchainExtension.AcquireNextImage(_logicalDevice, _swapchain, ulong.MaxValue, _imageAvailableSemaphores[_currentFrame], default, ref _imageIndex);

            if (_imagesInFlight[_imageIndex].Handle !=default)
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

            var _buffer = _commandBuffer[_imageIndex];

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

            if (_vulkan.QueueSubmit(_graphicsQueue, 1, _submitInfo, _fencesInFlight[_currentFrame]) != Result.Success)
            {
                throw new Exception("Failed to send command buffer to the GPU");
            }

            var _swapChains = stackalloc[] { _swapchain };
            PresentInfoKHR _presentInfo = new PresentInfoKHR()
            {
                SType = StructureType.PresentInfoKhr,
                WaitSemaphoreCount = 1,
                PWaitSemaphores = _signalSemaphores,
                SwapchainCount =1,
                PSwapchains = _swapChains,
                PImageIndices = &_imageIndex
            };
            _swapchainExtension.QueuePresent(_presentQueue, _presentInfo);
            _currentFrame = (_currentFrame + 1) % MAX_FRAMES_IN_FLIGHT;
        }
    }
}