using Silk.NET.Core.Native;
using Silk.NET.GLFW;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using System.Runtime.InteropServices;
using Image = Silk.NET.Vulkan.Image;
using ImageLayout = Silk.NET.Vulkan.ImageLayout;
using Silk.NET.Maths;
using System.Runtime.CompilerServices;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace ArctisAurora.EngineWork.Rendering.Renderers.Vulkan
{
    struct UBO
    {
        public Matrix4X4<float> _model;
        public Matrix4X4<float> _view;
        public Matrix4X4<float> _projection;
    }

    struct Vertex
    {
        public Silk.NET.Maths.Vector2D<float> _pos;
        public Silk.NET.Maths.Vector3D<float> _color;
        //public Silk.NET.Maths.Vector3D<float> _normal;
        //public Silk.NET.Maths.Vector2D<float> _uv;

        public static VertexInputBindingDescription GetBindingDescription()
        {
            VertexInputBindingDescription _description = new VertexInputBindingDescription()
            {
                Binding = 0,
                Stride = (uint)Unsafe.SizeOf<Vertex>(),
                InputRate = VertexInputRate.Vertex
            };
            return _description;
        }

        public static VertexInputAttributeDescription[] GetVertexInputAttributeDescriptions()
        {
            VertexInputAttributeDescription[] _descriptions = new VertexInputAttributeDescription[]
            {
                new VertexInputAttributeDescription()
                {
                    Binding = 0,
                    Location = 0,
                    Format = Format.R32G32Sfloat,
                    Offset = (uint)Marshal.OffsetOf<Vertex>(nameof(_pos))
                },
                new VertexInputAttributeDescription()
                {
                    Binding = 0,
                    Location = 1,
                    Format = Format.R32G32Sfloat,
                  Offset = (uint)Marshal.OffsetOf<Vertex>(nameof(_color)),
                }
            };
            return _descriptions;
        }
    }

    internal unsafe class VulkanRenderer
    {
        private Vertex[] _vertices = new Vertex[]
        {
            new Vertex { _pos = new Vector2D<float>(-0.5f,-0.5f), _color = new Vector3D<float>(1.0f, 0.0f, 0.0f) },
            new Vertex { _pos = new Vector2D<float>(0.5f,-0.5f), _color = new Vector3D<float>(0.0f, 1.0f, 0.0f) },
            new Vertex { _pos = new Vector2D<float>(0.5f,0.5f), _color = new Vector3D<float>(0.0f, 0.0f, 1.0f) },
            new Vertex { _pos = new Vector2D<float>(-0.5f,0.5f), _color = new Vector3D<float>(1.0f, 1.0f, 1.0f) },
        };
        private ushort[] _indices = new ushort[]
        {
            0,1,2,2,3,0
        };
        internal int _width = 1280;
        internal int _height = 720;
        internal static VulkanRenderer _rendererInstance = null;
        //window & vulkan setup
        internal AGlfwWindow _glWindow = new AGlfwWindow();
        private static Vk _vulkan = Vk.GetApi();    //vulkan api

        private static Instance _instance;          //vulkan instance
        private AVulkanSwapchain _swapchain;
        ImageView[] _imageViews;
        Extent2D _extent;
        RenderPass _renderPass;
        Pipeline _graphicsPipeline;
        Framebuffer[] _framebuffer;
        CommandBuffer[] _commandBuffer;
        CommandPool _commandPool;

        //VAO
        private Buffer _vertexBuffer;
        private DeviceMemory _vertexBufferMemory;
        private Buffer _indexBuffer;
        private DeviceMemory _indexBufferMemory;

        //uniform buffers
        Buffer[] _uniformBuffers;
        DeviceMemory[] _uniformBuffersMemory;

        private DescriptorSetLayout _descriptorSetLayout;
        DescriptorPool _descriptorPool;
        DescriptorSet[] _descriptorSets;

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
            _extent = new Extent2D() { Height = (uint)_height, Width = (uint)_width };
            _glWindow.CreateWindow(ref _extent);                //create glfw window
            CreateVulkanInstance();                             //create Vulkan instance
            _glWindow.CreateSurface(ref _vulkan, ref _instance);//create window surface
            ChoosePhysicalDevice();
            CreateLogicalDevice(_gpu);

            int _graphicsQFamilyIndex = FindQueueFamilyIndex(_gpu, QueueFlags.GraphicsBit);
            uint _presentSupportIndex = _glWindow.FindPresentSupportIndex(ref _qfm, ref _gpu);
            _graphicsQueue = _vulkan.GetDeviceQueue(_logicalDevice, (uint)_graphicsQFamilyIndex, 0);
            _presentQueue = _vulkan.GetDeviceQueue(_logicalDevice, _presentSupportIndex, 0);

            _swapchain = new AVulkanSwapchain(ref _vulkan, ref _glWindow._driverSurface, ref _glWindow._surface, ref _extent, ref _instance,ref _logicalDevice);
            _swapchain.CreateSwapchain(ref _gpu);

            CreateImageView();
            CreateRenderPass();
            CreateDescriptorSetLayout();
            CreateGraphicsPipeline();
            CreateFrameBuffers();
            CreateCommandPool();
            CreateVertexBuffer();
            CreateIndexBuffer();
            CreateUniformBuffers();
            CreateDescriptorPool();
            CreateDescriptorSets();
            CreateCommandBuffers();
            CreateSyncObjects();
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

        private void RecreateSwapChain()
        {
            _glWindow.UpdateWindowSize(ref _extent);
            _vulkan.DeviceWaitIdle(_logicalDevice);
            CleanUpSwapChain();

            _swapchain.CreateSwapchain(ref _gpu);
            CreateImageView();
            CreateRenderPass();
            CreateGraphicsPipeline();
            CreateFrameBuffers();
            CreateUniformBuffers();
            CreateDescriptorPool();
            CreateDescriptorSets();
            CreateCommandBuffers();

            _imagesInFlight = new Fence[_swapchain._swapchainImages.Length];
        }

        private void CreateImageView()
        {
            _imageViews = new ImageView[_swapchain._swapchainImages.Length];
            for (int i = 0; i < _swapchain._swapchainImages.Length; i++)
            {
                ImageViewCreateInfo _createInfo = new ImageViewCreateInfo
                {
                    Image = _swapchain._swapchainImages[i],
                    Format = _swapchain._surfaceFormat.Format,
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

                if (_vulkan!.CreateImageView(_logicalDevice, _createInfo, null, out _imageViews[i]) != Result.Success)
                {
                    throw new Exception("failed to create image views!");
                }
            }
        }

        private void CreateRenderPass()
        {
            AttachmentDescription _colorAttachment = new AttachmentDescription()
            {
                Format = _swapchain._surfaceFormat.Format,
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

            SubpassDependency _subDepend = new SubpassDependency()
            {
                SrcSubpass = Vk.SubpassExternal,
                DstSubpass = 0,
                SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
                SrcAccessMask = 0,
                DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
                DstAccessMask = AccessFlags.ColorAttachmentWriteBit
            };

            RenderPassCreateInfo _renderPassInfo = new RenderPassCreateInfo()
            {
                SType = StructureType.RenderPassCreateInfo,
                AttachmentCount = 1,
                PAttachments = &_colorAttachment,
                SubpassCount = 1,
                PSubpasses = &_subpass,
                PDependencies = &_subDepend
            };

            if (_vulkan.CreateRenderPass(_logicalDevice, _renderPassInfo, null, out _renderPass) != Result.Success)
            {
                throw new Exception("failed to create render pass!");
            }
        }

        private void CreateGraphicsPipeline()
        {
            _pipeline = new V_Shader();
            _pipeline.CreateGraphicsPipeline("vulkan.vert.spv", "vulkan.frag.spv", _logicalDevice, _vulkan, _extent, ref _renderPass, ref _graphicsPipeline, ref _descriptorSetLayout);
        }

        private void CreateFrameBuffers()
        {
            _framebuffer = new Framebuffer[_imageViews.Length];
            for (int i = 0; i < _imageViews.Length; i++)
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

                if (_vulkan.CreateFramebuffer(_logicalDevice, _framebufferInfo, null, out _framebuffer[i]) != Result.Success)
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
            if (_vulkan.CreateCommandPool(_logicalDevice, _createInfo, null, out _commandPool) != Result.Success)
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
                    Color = new() { Float32_0 = 0.05f, Float32_1 = 0.05f, Float32_2 = 0.05f, Float32_3 = 1f }
                };

                _renderPassInfo.ClearValueCount = 1;
                _renderPassInfo.PClearValues = &_clearColor;

                //render command code
                _vulkan.CmdBeginRenderPass(_commandBuffer[i], &_renderPassInfo, SubpassContents.Inline);
                _vulkan.CmdBindPipeline(_commandBuffer[i], PipelineBindPoint.Graphics, _graphicsPipeline);

                Buffer[] _vertBuffers = new Buffer[] { _vertexBuffer };
                var _offset = new ulong[] { 0 };

                fixed (ulong* _offsetsPtr = _offset)
                fixed (Buffer* _vertBuffersPtr = _vertBuffers)
                {
                    _vulkan.CmdBindVertexBuffers(_commandBuffer[i], 0, 1, _vertBuffersPtr, _offsetsPtr);
                }
                _vulkan.CmdBindIndexBuffer(_commandBuffer[i], _indexBuffer, 0, IndexType.Uint16);
                _vulkan.CmdBindDescriptorSets(_commandBuffer[i], PipelineBindPoint.Graphics, _pipeline._pipelineLayout, 0, 1, _descriptorSets[i], 0, null);
                _vulkan.CmdDrawIndexed(_commandBuffer[i], (uint)_indices.Length, 1, 0, 0, 0);
                _vulkan.CmdEndRenderPass(_commandBuffer[i]);
                //done rendering

                if (_vulkan.EndCommandBuffer(_commandBuffer[i]) != Result.Success)
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

        internal void Draw()
        {
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
                throw new Exception("Failed to acquire swap chain image");
            }

            UpdateUniformBuffer(_imageIndex);
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

            if (_vulkan.QueueSubmit(_graphicsQueue, 1, _submitInfo, _fencesInFlight[_currentFrame]) != Result.Success)
            {
                throw new Exception("Failed to send command buffer to the GPU");
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

            _vulkan.DestroyPipeline(_logicalDevice, _graphicsPipeline, null);
            _vulkan.DestroyPipelineLayout(_logicalDevice, _pipeline._pipelineLayout, null);
            _vulkan.DestroyRenderPass(_logicalDevice, _renderPass, null);

            foreach (var iv in _imageViews)
            {
                _vulkan.DestroyImageView(_logicalDevice, iv, null);
            }
            _swapchain.DestroySwapchain();
        }

        private void CreateVertexBuffer()
        {
            ulong _bufferSize = (ulong)(Unsafe.SizeOf<Vertex>() * _vertices.Length);
            Buffer _stagingBuffer = default;
            DeviceMemory _stagingBufferMemory = default;
            CreateBuffer(_bufferSize,BufferUsageFlags.TransferSrcBit, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCachedBit, ref _stagingBuffer, ref _stagingBufferMemory);

            void* _data;
            _vulkan.MapMemory(_logicalDevice, _stagingBufferMemory, 0, _bufferSize, 0, &_data);
            _vertices.AsSpan().CopyTo(new Span<Vertex>(_data, _vertices.Length));
            _vulkan.UnmapMemory(_logicalDevice, _stagingBufferMemory);

            CreateBuffer(_bufferSize, BufferUsageFlags.TransferDstBit | BufferUsageFlags.VertexBufferBit, MemoryPropertyFlags.DeviceLocalBit, ref _vertexBuffer, ref _vertexBufferMemory);

            CopyBuffer(_stagingBuffer, _vertexBuffer, _bufferSize);
            _vulkan.DestroyBuffer(_logicalDevice, _stagingBuffer, null);
            _vulkan.FreeMemory(_logicalDevice, _stagingBufferMemory, null);
        }

        private void CopyBuffer(Buffer _sourceBuffer, Buffer _dstBuffer, ulong bufferSize)
        {
            CommandBufferAllocateInfo _allocInfo = new CommandBufferAllocateInfo()
            {
                SType = StructureType.CommandBufferAllocateInfo,
                Level = CommandBufferLevel.Primary,
                CommandPool = _commandPool,
                CommandBufferCount = 1
            };
            CommandBuffer _localCommandBuffer;
            _vulkan.AllocateCommandBuffers(_logicalDevice, _allocInfo, out _localCommandBuffer);

            CommandBufferBeginInfo _cBBeginInfo = new CommandBufferBeginInfo()
            {
                SType = StructureType.CommandBufferBeginInfo,
                Flags = CommandBufferUsageFlags.OneTimeSubmitBit
            };
            _vulkan.BeginCommandBuffer(_localCommandBuffer, _cBBeginInfo);

            BufferCopy _copyRegion = new BufferCopy()
            {
                Size = bufferSize
            };
            _vulkan.CmdCopyBuffer(_localCommandBuffer, _sourceBuffer, _dstBuffer, 1, _copyRegion);
            _vulkan.EndCommandBuffer(_localCommandBuffer);

            SubmitInfo _subInfo = new SubmitInfo()
            {
                SType = StructureType.SubmitInfo,
                CommandBufferCount= 1,
                PCommandBuffers = &_localCommandBuffer
            };
            _vulkan.QueueSubmit(_graphicsQueue, 1, _subInfo, default);
            _vulkan.QueueWaitIdle(_graphicsQueue);
            _vulkan.FreeCommandBuffers(_logicalDevice, _commandPool, 1, _localCommandBuffer);
        }

        private void CreateBuffer(ulong _size, BufferUsageFlags _usage, MemoryPropertyFlags _properties, ref Buffer _buffer, ref DeviceMemory _bufferMemory)
        {
            BufferCreateInfo _bufferCreateInfo = new BufferCreateInfo()
            {
                SType = StructureType.BufferCreateInfo,
                Size = _size,
                Usage = _usage,
                SharingMode = SharingMode.Exclusive
            };

            fixed (Buffer* _bufferPtr = &_buffer)
            {
                if (_vulkan.CreateBuffer(_logicalDevice, _bufferCreateInfo, null, _bufferPtr) != Result.Success)
                {
                    throw new Exception("Failed to create a VAO");
                }
            }
            MemoryRequirements _memReqs = new MemoryRequirements();
            _vulkan.GetBufferMemoryRequirements(_logicalDevice, _buffer, out _memReqs);

            MemoryAllocateInfo _allocateInfo = new MemoryAllocateInfo()
            {
                SType = StructureType.MemoryAllocateInfo,
                AllocationSize = _memReqs.Size,
                MemoryTypeIndex = FindMemoryType(_memReqs.MemoryTypeBits, _properties)
            };

            fixed (DeviceMemory* _bufferMemoryPtr = &_bufferMemory)
            {
                if (_vulkan.AllocateMemory(_logicalDevice, _allocateInfo, null, _bufferMemoryPtr) != Result.Success)
                {
                    throw new Exception("Failed to allocate vertex buffer memory");
                }
            }

            _vulkan.BindBufferMemory(_logicalDevice, _buffer, _bufferMemory, 0);
        }

        private void CreateIndexBuffer()
        {
            ulong _bufferSize = ((ulong)(Unsafe.SizeOf<ushort>() * _indices.Length));
            Buffer _stagingBuffer = default;
            DeviceMemory _stagingBufferMemory = default;
            CreateBuffer(_bufferSize, BufferUsageFlags.TransferSrcBit, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit, ref _stagingBuffer, ref _stagingBufferMemory);
            void* _data;
            _vulkan.MapMemory(_logicalDevice, _stagingBufferMemory, 0, _bufferSize, 0, &_data);
            _indices.AsSpan().CopyTo(new Span<ushort>(_data, _indices.Length));
            _vulkan.UnmapMemory(_logicalDevice, _stagingBufferMemory);
            CreateBuffer(_bufferSize, BufferUsageFlags.TransferDstBit | BufferUsageFlags.IndexBufferBit, MemoryPropertyFlags.DeviceLocalBit, ref _indexBuffer, ref _indexBufferMemory);
            CopyBuffer(_stagingBuffer, _indexBuffer, _bufferSize);
            _vulkan.DestroyBuffer(_logicalDevice, _stagingBuffer, null);
            _vulkan.FreeMemory(_logicalDevice, _stagingBufferMemory, null);
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
            DescriptorSetLayoutCreateInfo _layoutCreateInfo = new DescriptorSetLayoutCreateInfo()
            {
                SType = StructureType.DescriptorSetLayoutCreateInfo,
                BindingCount = 1,
                PBindings = &_uboLayoutBinding
            };
            fixed (DescriptorSetLayout* _descSetLayoutPtr = &_descriptorSetLayout)
            {
                if (_vulkan.CreateDescriptorSetLayout(_logicalDevice, _layoutCreateInfo, null, _descSetLayoutPtr) != Result.Success)
                {
                    throw new Exception("Failed to create UBO");
                }
            }
        }

        private uint FindMemoryType(uint _typeFilter, MemoryPropertyFlags _properties)
        {
            PhysicalDeviceMemoryProperties _memProperties;
            _vulkan.GetPhysicalDeviceMemoryProperties(_gpu, out _memProperties);

            for(int i=0;i<_memProperties.MemoryTypeCount;i++)
            {
                if ((_typeFilter & (1<<i)) != 0 && (_memProperties.MemoryTypes[i].PropertyFlags & _properties) == _properties)
                {
                    return (uint)i;
                }
            }
            throw new Exception("Failed to find suitable memory type");
        }

        private void CreateUniformBuffers()
        {
            ulong _bufferSize = (ulong)Unsafe.SizeOf<UBO>();
            _uniformBuffers = new Buffer[_swapchain._swapchainImages.Length];
            _uniformBuffersMemory = new DeviceMemory[_swapchain._swapchainImages.Length];

            for(int i=0;i< _swapchain._swapchainImages.Length;i++)
            {
                CreateBuffer(_bufferSize, BufferUsageFlags.UniformBufferBit, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit, ref _uniformBuffers[i], ref _uniformBuffersMemory[i]);
            }
        }

        private void UpdateUniformBuffer(uint _currentImage)
        {
            float time = (float)_glWindow._glfw.GetTime();

            UBO _ubo = new UBO()
            {
                _model = Matrix4X4<float>.Identity * Matrix4X4.CreateFromAxisAngle<float>(new Vector3D<float>(1,0,0), time * Scalar.DegreesToRadians(90.0f)),
                _view = Matrix4X4.CreateLookAt(new Vector3D<float>(2,2,2), new Vector3D<float>(0,0,0), new Vector3D<float>(0,0,1)),
                _projection = Matrix4X4.CreatePerspectiveFieldOfView(Scalar.DegreesToRadians(45.0f),_extent.Width/_extent.Height,0.1f,10f)
            };
            _ubo._projection.M22 *= -1;

            void* _data;
            _vulkan.MapMemory(_logicalDevice, _uniformBuffersMemory[_currentImage], 0, (ulong)Unsafe.SizeOf<UBO>(), 0, &_data);
            new Span<UBO>(_data, 1)[0] = _ubo;
            _vulkan.UnmapMemory(_logicalDevice, _uniformBuffersMemory[_currentImage]);
        }

        private void CreateDescriptorPool()
        {
            DescriptorPoolSize _poolSize = new DescriptorPoolSize()
            {
                Type = DescriptorType.UniformBuffer,
                DescriptorCount = (uint)_swapchain._swapchainImages.Length
            };

            DescriptorPoolCreateInfo _poolInfo = new DescriptorPoolCreateInfo()
            {
                SType = StructureType.DescriptorPoolCreateInfo,
                PoolSizeCount = 1,
                PPoolSizes = &_poolSize,
                MaxSets = (uint)_swapchain._swapchainImages.Length
            };

            fixed(DescriptorPool* _descPoolPtr= &_descriptorPool)
            {
                if(_vulkan.CreateDescriptorPool(_logicalDevice,_poolInfo,null,_descPoolPtr) != Result.Success)
                {
                    throw new Exception("Failed to create descriptor pool");
                }
            }
        }

        private void CreateDescriptorSets()
        {
            DescriptorSetLayout[] _layouts = new DescriptorSetLayout[_swapchain._swapchainImages.Length];
            Array.Fill(_layouts, _descriptorSetLayout);

            fixed(DescriptorSetLayout* _layoutsPtr= _layouts)
            {
                DescriptorSetAllocateInfo _allocateInfo = new DescriptorSetAllocateInfo()
                {
                    SType = StructureType.DescriptorSetAllocateInfo,
                    DescriptorPool = _descriptorPool,
                    DescriptorSetCount = (uint)_swapchain._swapchainImages.Length,
                    PSetLayouts = _layoutsPtr
                };

                _descriptorSets = new DescriptorSet[_swapchain._swapchainImages.Length];
                fixed(DescriptorSet* _descriptorSetsPtr= _descriptorSets)
                {
                    if (_vulkan.AllocateDescriptorSets(_logicalDevice, _allocateInfo, _descriptorSetsPtr) != Result.Success)
                    {
                        throw new Exception("Failed to allocate descriptor set");
                    }
                }
            }
            for (int i = 0; i < _swapchain._swapchainImages.Length;i++)
            {
                DescriptorBufferInfo _bufferInfo = new DescriptorBufferInfo()
                {
                    Buffer = _uniformBuffers[i],
                    Offset = 0,
                    Range = (ulong)Unsafe.SizeOf<UBO>()
                };
                WriteDescriptorSet _descriptorWrite = new WriteDescriptorSet()
                {
                    SType = StructureType.WriteDescriptorSet,
                    DstSet = _descriptorSets[i],
                    DstBinding = 0,
                    DstArrayElement = 0,
                    DescriptorType = DescriptorType.UniformBuffer,
                    DescriptorCount = 1,
                    PBufferInfo = &_bufferInfo
                };
                _vulkan.UpdateDescriptorSets(_logicalDevice, 1, _descriptorWrite, 0, null);
            }
        }
    }
}