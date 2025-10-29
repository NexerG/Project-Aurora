using ArctisAurora.EngineWork.Rendering.RendererTypes;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Buffer = Silk.NET.Vulkan.Buffer;
using Format = Silk.NET.Vulkan.Format;
using Image = SixLabors.ImageSharp.Image;
using ImageLayout = Silk.NET.Vulkan.ImageLayout;

namespace ArctisAurora.EngineWork.Rendering.Helpers
{
    public struct UBO
    {
        public Matrix4X4<float> _view;
        public Matrix4X4<float> _projection;
        //public Matrix4X4<float> _lightProjection;
        //public Matrix4X4<float> _lightView;
        //public Vector3D<float> _camPos;
    }

    internal static unsafe class AVulkanBufferHandler
    {
        internal static void CreateTextureBuffer(ref Silk.NET.Vulkan.Image _textureImage, ref DeviceMemory _textureBufferMemory, string pathToImage, Format imageFormat)
        {
            using var _image = Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(pathToImage);
            ulong _imageSize = (ulong)(_image.Width * _image.Height * _image.PixelType.BitsPerPixel / 8);

            Buffer _stagingBuffer = default;
            DeviceMemory _stagingBufferMemory = default;
            CreateBuffer(_imageSize, ref _stagingBuffer, ref _stagingBufferMemory, BufferUsageFlags.TransferSrcBit, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);

            void* _data;
            Renderer.vk.MapMemory(Renderer.logicalDevice, _stagingBufferMemory, 0, _imageSize, 0, &_data);
            _image.CopyPixelDataTo(new Span<byte>(_data, (int)_imageSize));
            Renderer.vk.UnmapMemory(Renderer.logicalDevice, _stagingBufferMemory);

            CreateImage((uint)_image.Width, (uint)_image.Height, imageFormat, ImageTiling.Optimal, ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit, MemoryPropertyFlags.DeviceLocalBit, ref _textureImage, ref _textureBufferMemory);

            TransitionImageLayout(_textureImage, ImageLayout.Undefined, ImageLayout.TransferDstOptimal);
            CopyBufferToImage(_stagingBuffer, _textureImage, (uint)_image.Width, (uint)_image.Height);
            TransitionImageLayout(_textureImage, ImageLayout.TransferDstOptimal, ImageLayout.ShaderReadOnlyOptimal);

            Renderer.vk.DestroyBuffer(Renderer.logicalDevice, _stagingBuffer, null);
            Renderer.vk.FreeMemory(Renderer.logicalDevice, _stagingBufferMemory, null);
        }

        internal static void CreateTextureBuffer(ref Silk.NET.Vulkan.Image _textureImage, ref DeviceMemory _textureBufferMemory, ref Image<Rgba32> image, Format imageFormat)
        {
            using var _image = image;
            ulong _imageSize = (ulong)(_image.Width * _image.Height * _image.PixelType.BitsPerPixel);

            Buffer _stagingBuffer = default;
            DeviceMemory _stagingBufferMemory = default;
            CreateBuffer(_imageSize, ref _stagingBuffer, ref _stagingBufferMemory, BufferUsageFlags.TransferSrcBit, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);

            void* _data;
            Renderer.vk.MapMemory(Renderer.logicalDevice, _stagingBufferMemory, 0, _imageSize, 0, &_data);
            _image.CopyPixelDataTo(new Span<byte>(_data, (int)_imageSize));
            Renderer.vk.UnmapMemory(Renderer.logicalDevice, _stagingBufferMemory);

            CreateImage((uint)_image.Width, (uint)_image.Height, imageFormat, ImageTiling.Optimal, ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit, MemoryPropertyFlags.DeviceLocalBit, ref _textureImage, ref _textureBufferMemory);

            TransitionImageLayout(_textureImage, ImageLayout.Undefined, ImageLayout.TransferDstOptimal);
            CopyBufferToImage(_stagingBuffer, _textureImage, (uint)_image.Width, (uint)_image.Height);
            TransitionImageLayout(_textureImage, ImageLayout.TransferDstOptimal, ImageLayout.ShaderReadOnlyOptimal);

            Renderer.vk.DestroyBuffer(Renderer.logicalDevice, _stagingBuffer, null);
            Renderer.vk.FreeMemory(Renderer.logicalDevice, _stagingBufferMemory, null);
        }

        private static void CopyBufferToImage(Buffer _buffer, Silk.NET.Vulkan.Image _image, uint _width, uint _height)
        {
            CommandBuffer _commandBuffer = BeginSingleTimeCommands();

            BufferImageCopy _bufferImageCopy = new BufferImageCopy()
            {
                BufferOffset = 0,
                BufferRowLength = 0,
                BufferImageHeight = 0,
                ImageSubresource =
            {
                AspectMask = ImageAspectFlags.ColorBit,
                MipLevel = 0,
                BaseArrayLayer = 0,
                LayerCount = 1,
            },
                ImageOffset = new Offset3D(0, 0, 0),
                ImageExtent = new Extent3D(_width, _height, 1),

            };

            Renderer.vk!.CmdCopyBufferToImage(_commandBuffer, _buffer, _image, ImageLayout.TransferDstOptimal, 1, ref _bufferImageCopy);
            EndSingleTimeCommands(ref _commandBuffer);
        }

        private static void TransitionImageLayout(Silk.NET.Vulkan.Image _image, ImageLayout _oldLayout, ImageLayout _newLayout)
        {
            CommandBuffer _commandBuffer = BeginSingleTimeCommands();

            ImageMemoryBarrier _barrier = new()
            {
                SType = StructureType.ImageMemoryBarrier,
                OldLayout = _oldLayout,
                NewLayout = _newLayout,
                SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
                DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
                Image = _image,
                SubresourceRange =
                {
                    AspectMask = ImageAspectFlags.ColorBit,
                    BaseMipLevel = 0,
                    LevelCount = 1,
                    BaseArrayLayer = 0,
                    LayerCount = 1,
                }
            };
            PipelineStageFlags sourceStage;
            PipelineStageFlags destinationStage;

            if (_oldLayout == ImageLayout.Undefined && _newLayout == ImageLayout.TransferDstOptimal)
            {
                _barrier.SrcAccessMask = 0;
                _barrier.DstAccessMask = AccessFlags.TransferWriteBit;

                sourceStage = PipelineStageFlags.TopOfPipeBit;
                destinationStage = PipelineStageFlags.TransferBit;
            }
            else if (_oldLayout == ImageLayout.TransferDstOptimal && _newLayout == ImageLayout.ShaderReadOnlyOptimal)
            {
                _barrier.SrcAccessMask = AccessFlags.TransferWriteBit;
                _barrier.DstAccessMask = AccessFlags.ShaderReadBit;

                sourceStage = PipelineStageFlags.TransferBit;
                destinationStage = PipelineStageFlags.FragmentShaderBit;
            }
            else
            {
                throw new Exception("unsupported layout transition!");
            }

            Renderer.vk!.CmdPipelineBarrier(_commandBuffer, sourceStage, destinationStage, 0, 0, null, 0, null, 1, ref _barrier);
            EndSingleTimeCommands(ref _commandBuffer);
        }

        internal static void CreateImage(uint _width, uint _height, Format _format, ImageTiling _tiling, ImageUsageFlags _usage, MemoryPropertyFlags _properties, ref Silk.NET.Vulkan.Image _im, ref DeviceMemory _devMemory)
        {
            ImageCreateInfo _imageInfo = new()
            {
                SType = StructureType.ImageCreateInfo,
                ImageType = ImageType.Type2D,
                Extent =
                {
                    Width = _width,
                    Height = _height,
                    Depth = 1,
                },
                MipLevels = 1,
                ArrayLayers = 1,
                Format = _format,
                Tiling = _tiling,
                InitialLayout = ImageLayout.Undefined,
                Usage = _usage,
                Samples = SampleCountFlags.Count1Bit,
                SharingMode = SharingMode.Exclusive,
            };

            fixed (Silk.NET.Vulkan.Image* imagePtr = &_im)
            {
                if (Renderer.vk!.CreateImage(Renderer.logicalDevice, ref _imageInfo, null, imagePtr) != Result.Success)
                {
                    throw new Exception("failed to create image!");
                }
            }

            Renderer.vk.GetImageMemoryRequirements(Renderer.logicalDevice, _im, out MemoryRequirements _memReqs);

            MemoryAllocateInfo allocInfo = new()
            {
                SType = StructureType.MemoryAllocateInfo,
                AllocationSize = _memReqs.Size,
                MemoryTypeIndex = FindMemoryType(_memReqs.MemoryTypeBits, _properties),
            };

            fixed (DeviceMemory* imageMemoryPtr = &_devMemory)
            {
                if (Renderer.vk!.AllocateMemory(Renderer.logicalDevice, ref allocInfo, null, imageMemoryPtr) != Result.Success)
                {
                    throw new Exception("failed to allocate image memory!");
                }
            }

            Renderer.vk!.BindImageMemory(Renderer.logicalDevice, _im, _devMemory, 0);
        }
        internal static void CreateImage(Vk vk, Device logicalDevice, PhysicalDevice gpu, uint _width, uint _height, Format _format, ImageTiling _tiling, ImageUsageFlags _usage, MemoryPropertyFlags _properties, ref Silk.NET.Vulkan.Image _im, ref DeviceMemory _devMemory)
        {
            ImageCreateInfo _imageInfo = new()
            {
                SType = StructureType.ImageCreateInfo,
                ImageType = ImageType.Type2D,
                Extent =
                {
                    Width = _width,
                    Height = _height,
                    Depth = 1,
                },
                MipLevels = 1,
                ArrayLayers = 1,
                Format = _format,
                Tiling = _tiling,
                InitialLayout = ImageLayout.Undefined,
                Usage = _usage,
                Samples = SampleCountFlags.Count1Bit,
                SharingMode = SharingMode.Exclusive,
            };

            fixed (Silk.NET.Vulkan.Image* imagePtr = &_im)
            {
                if (vk!.CreateImage(logicalDevice, ref _imageInfo, null, imagePtr) != Result.Success)
                {
                    throw new Exception("failed to create image!");
                }
            }

            vk.GetImageMemoryRequirements(logicalDevice, _im, out MemoryRequirements _memReqs);

            MemoryAllocateInfo allocInfo = new()
            {
                SType = StructureType.MemoryAllocateInfo,
                AllocationSize = _memReqs.Size,
                MemoryTypeIndex = FindMemoryType(vk, gpu, _memReqs.MemoryTypeBits, _properties),
            };

            fixed (DeviceMemory* imageMemoryPtr = &_devMemory)
            {
                if (vk!.AllocateMemory(logicalDevice, ref allocInfo, null, imageMemoryPtr) != Result.Success)
                {
                    throw new Exception("failed to allocate image memory!");
                }
            }

            vk.BindImageMemory(logicalDevice, _im, _devMemory, 0);
        }

        internal static void CreateImageView(ref Vk vk, ref Device logicalDevice, ref Silk.NET.Vulkan.Image _textureImage, ref ImageView _imageView, Format imageFormat, ImageAspectFlags aspectFlags)
        {
            ImageViewCreateInfo _createInfo = new ImageViewCreateInfo
            {
                SType = StructureType.ImageViewCreateInfo,
                Image = _textureImage,
                Format = imageFormat,
                ViewType = ImageViewType.Type2D
            };

            _createInfo.SubresourceRange.AspectMask = aspectFlags;
            _createInfo.SubresourceRange.BaseMipLevel = 0;
            _createInfo.SubresourceRange.LevelCount = 1;
            _createInfo.SubresourceRange.BaseArrayLayer = 0;
            _createInfo.SubresourceRange.LayerCount = 1;

            if (vk!.CreateImageView(logicalDevice, ref _createInfo, null, out _imageView) != Result.Success)
            {
                throw new Exception("failed to create texture image view!");
            }
        }





        internal static BufferUsageFlags vertexBufferFlags = BufferUsageFlags.VertexBufferBit;
        internal static BufferUsageFlags indexBufferFlags =  BufferUsageFlags.IndexBufferBit;
        internal static BufferUsageFlags raytracingBufferFlags = BufferUsageFlags.AccelerationStructureBuildInputReadOnlyBitKhr;
        private static BufferUsageFlags defaultBufferFlags = BufferUsageFlags.TransferDstBit;

        private static BufferUsageFlags defaultStagingBufferFlags = BufferUsageFlags.TransferSrcBit;
        private static MemoryPropertyFlags defaultStagingMemoryFlags = MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCachedBit;


        internal static void CreateBuffer<T>(ref T[] data, ref Buffer buffer, ref DeviceMemory memory, BufferUsageFlags usageFlags) where T : unmanaged
        {
            ulong bufferSize = (ulong)(sizeof(T) * data.Length);

            Buffer _stagingBuffer = default;
            DeviceMemory _stagingBufferMemory = default;
            CreateBuffer(bufferSize, ref _stagingBuffer, ref _stagingBufferMemory, defaultStagingBufferFlags, defaultStagingMemoryFlags);

            void* _dataPtr;
            Renderer.vk.MapMemory(Renderer.logicalDevice, _stagingBufferMemory, 0, bufferSize, 0, &_dataPtr);
            data.AsSpan().CopyTo(new Span<T>(_dataPtr, data.Length));
            Renderer.vk.UnmapMemory(Renderer.logicalDevice, _stagingBufferMemory);

            CreateBuffer(bufferSize, ref buffer, ref memory, defaultBufferFlags | usageFlags, defaultStagingMemoryFlags);

            CopyBuffer(ref _stagingBuffer, ref buffer, bufferSize);
            Renderer.vk.DestroyBuffer(Renderer.logicalDevice, _stagingBuffer, null);
            Renderer.vk.FreeMemory(Renderer.logicalDevice, _stagingBufferMemory, null);
        }

        internal static void CreateBuffer<T>(ref T data, ref Buffer buffer, ref DeviceMemory memory, BufferUsageFlags usageFlags) where T : unmanaged
        {
            ulong bufferSize = (ulong)(sizeof(T));

            Buffer _stagingBuffer = default;
            DeviceMemory _stagingBufferMemory = default;
            CreateBuffer(bufferSize, ref _stagingBuffer, ref _stagingBufferMemory, defaultStagingBufferFlags, defaultStagingMemoryFlags);

            void* _dataPtr;
            Renderer.vk.MapMemory(Renderer.logicalDevice, _stagingBufferMemory, 0, bufferSize, 0, &_dataPtr);
            new Span<T>(_dataPtr, 1)[0] = data;
            Renderer.vk.UnmapMemory(Renderer.logicalDevice, _stagingBufferMemory);

            CreateBuffer(bufferSize, ref buffer, ref memory, defaultBufferFlags | usageFlags, defaultStagingMemoryFlags);

            CopyBuffer(ref _stagingBuffer, ref buffer, bufferSize);
            Renderer.vk.DestroyBuffer(Renderer.logicalDevice, _stagingBuffer, null);
            Renderer.vk.FreeMemory(Renderer.logicalDevice, _stagingBufferMemory, null);
        }

        internal static void UpdateBuffer<T>(ref T[] data, ref Buffer buffer, ref DeviceMemory memory, BufferUsageFlags usageFlags) where T : unmanaged
        {
            ulong bufferSize = (ulong)(sizeof(T) * data.Length);

            Buffer _stagingBuffer = default;
            DeviceMemory _stagingBufferMemory = default;
            CreateBuffer(bufferSize, ref _stagingBuffer, ref _stagingBufferMemory, defaultStagingBufferFlags, defaultStagingMemoryFlags);

            void* _dataPtr;
            Renderer.vk.MapMemory(Renderer.logicalDevice, _stagingBufferMemory, 0, bufferSize, 0, &_dataPtr);
            data.AsSpan().CopyTo(new Span<T>(_dataPtr, data.Length));
            Renderer.vk.UnmapMemory(Renderer.logicalDevice, _stagingBufferMemory);

            CopyBuffer(ref _stagingBuffer, ref buffer, bufferSize);
            Renderer.vk.DestroyBuffer(Renderer.logicalDevice, _stagingBuffer, null);
            Renderer.vk.FreeMemory(Renderer.logicalDevice, _stagingBufferMemory, null);
        }

        internal static void UpdateBuffer<T>(ref T data, ref Buffer buffer, ref DeviceMemory memory, BufferUsageFlags usageFlags) where T : unmanaged
        {
            ulong bufferSize = (ulong)(sizeof(T));

            Buffer _stagingBuffer = default;
            DeviceMemory _stagingBufferMemory = default;
            CreateBuffer(bufferSize, ref _stagingBuffer, ref _stagingBufferMemory, defaultStagingBufferFlags, defaultStagingMemoryFlags);

            void* _dataPtr;
            Renderer.vk.MapMemory(Renderer.logicalDevice, _stagingBufferMemory, 0, bufferSize, 0, &_dataPtr);
            new Span<T>(_dataPtr, 1)[0] = data;
            Renderer.vk.UnmapMemory(Renderer.logicalDevice, _stagingBufferMemory);

            CopyBuffer(ref _stagingBuffer, ref buffer, bufferSize);
            Renderer.vk.DestroyBuffer(Renderer.logicalDevice, _stagingBuffer, null);
            Renderer.vk.FreeMemory(Renderer.logicalDevice, _stagingBufferMemory, null);
        }

        internal static void CreateBuffer(ulong _size, ref Buffer _buffer, ref DeviceMemory _bufferMemory, BufferUsageFlags _usage, MemoryPropertyFlags _properties)
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
                if (Renderer.vk.CreateBuffer(Renderer.logicalDevice, ref _bufferCreateInfo, null, _bufferPtr) != Result.Success)
                {
                    throw new Exception("Failed to create a buffer");
                }
            }
            MemoryRequirements _memReqs = new MemoryRequirements();
            Renderer.vk.GetBufferMemoryRequirements(Renderer.logicalDevice, _buffer, out _memReqs);

            var allocateFlagsInfo = new MemoryAllocateFlagsInfo
            {
                SType = StructureType.MemoryAllocateFlagsInfo,
                Flags = MemoryAllocateFlags.AddressBitKhr // Enable device address for
            };
            MemoryAllocateInfo _allocateInfo = new MemoryAllocateInfo()
            {
                SType = StructureType.MemoryAllocateInfo,
                AllocationSize = _memReqs.Size,
                MemoryTypeIndex = FindMemoryType(_memReqs.MemoryTypeBits, _properties),
                PNext = &allocateFlagsInfo
            };

            fixed (DeviceMemory* _bufferMemoryPtr = &_bufferMemory)
            {
                if (Renderer.vk.AllocateMemory(Renderer.logicalDevice, ref _allocateInfo, null, _bufferMemoryPtr) != Result.Success)
                {
                    throw new Exception("Failed to allocate buffer memory");
                }
            }

            Renderer.vk.BindBufferMemory(Renderer.logicalDevice, _buffer, _bufferMemory, 0);
        }

        private static void CopyBuffer(ref Buffer _sourceBuffer, ref Buffer _dstBuffer, ulong bufferSize)
        {
            CommandBufferAllocateInfo _allocInfo = new CommandBufferAllocateInfo()
            {
                SType = StructureType.CommandBufferAllocateInfo,
                Level = CommandBufferLevel.Primary,
                CommandPool = Renderer.commandPool,
                CommandBufferCount = 1
            };
            CommandBuffer _localCommandBuffer;
            Renderer.vk.AllocateCommandBuffers(Renderer.logicalDevice, ref _allocInfo, out _localCommandBuffer);

            CommandBufferBeginInfo _cBBeginInfo = new CommandBufferBeginInfo()
            {
                SType = StructureType.CommandBufferBeginInfo,
                Flags = CommandBufferUsageFlags.OneTimeSubmitBit
            };
            Renderer.vk.BeginCommandBuffer(_localCommandBuffer, ref _cBBeginInfo);

            BufferCopy _copyRegion = new BufferCopy()
            {
                Size = bufferSize
            };
            Renderer.vk.CmdCopyBuffer(_localCommandBuffer, _sourceBuffer, _dstBuffer, 1, ref _copyRegion);
            Renderer.vk.EndCommandBuffer(_localCommandBuffer);

            SubmitInfo _subInfo = new SubmitInfo()
            {
                SType = StructureType.SubmitInfo,
                CommandBufferCount = 1,
                PCommandBuffers = &_localCommandBuffer
            };
            Result queue, wait;
            queue = Renderer.vk.QueueSubmit(Renderer.graphicsQueue, 1, ref _subInfo, default);
            wait = Renderer.vk.QueueWaitIdle(Renderer.graphicsQueue);
            if (queue != Result.Success && wait != Result.Success)
            {
                Console.WriteLine("Exception thrown");
                throw new Exception("failed to submit 'copy buffer' commands");
            }
            Renderer.vk.FreeCommandBuffers(Renderer.logicalDevice, Renderer.commandPool, 1, ref _localCommandBuffer);
        }

        internal static uint FindMemoryType(uint _typeFilter, MemoryPropertyFlags _properties)
        {
            PhysicalDeviceMemoryProperties _memProperties;
            Renderer.vk.GetPhysicalDeviceMemoryProperties(Renderer.gpu, out _memProperties);

            for (int i = 0; i < _memProperties.MemoryTypeCount; i++)
            {
                if ((_typeFilter & 1 << i) != 0 && (_memProperties.MemoryTypes[i].PropertyFlags & _properties) == _properties)
                {
                    return (uint)i;
                }
            }
            throw new Exception("Failed to find suitable memory type");
        }

        internal static uint FindMemoryType(Vk vk, PhysicalDevice gpu, uint _typeFilter, MemoryPropertyFlags _properties)
        {
            PhysicalDeviceMemoryProperties _memProperties;
            vk.GetPhysicalDeviceMemoryProperties(gpu, out _memProperties);

            for (int i = 0; i < _memProperties.MemoryTypeCount; i++)
            {
                if ((_typeFilter & 1 << i) != 0 && (_memProperties.MemoryTypes[i].PropertyFlags & _properties) == _properties)
                {
                    return (uint)i;
                }
            }
            throw new Exception("Failed to find suitable memory type");
        }

        internal static CommandBuffer BeginSingleTimeCommands()
        {
            CommandBufferAllocateInfo _allocInfo = new CommandBufferAllocateInfo()
            {
                SType = StructureType.CommandBufferAllocateInfo,
                Level = CommandBufferLevel.Primary,
                CommandPool = Renderer.commandPool,
                CommandBufferCount = 1,
            };

            Renderer.vk!.AllocateCommandBuffers(Renderer.logicalDevice, ref _allocInfo, out CommandBuffer _commandBuffer);

            CommandBufferBeginInfo beginInfo = new CommandBufferBeginInfo()
            {
                SType = StructureType.CommandBufferBeginInfo,
                Flags = CommandBufferUsageFlags.OneTimeSubmitBit,
            };

            Renderer.vk!.BeginCommandBuffer(_commandBuffer, ref beginInfo);

            return _commandBuffer;
        }

        internal static void EndSingleTimeCommands(ref CommandBuffer commandBuffer)
        {
            Renderer.vk!.EndCommandBuffer(commandBuffer);

            fixed (CommandBuffer* _cptr = &commandBuffer)
            {
                SubmitInfo submitInfo = new()
                {
                    SType = StructureType.SubmitInfo,
                    CommandBufferCount = 1,
                    PCommandBuffers = _cptr,
                };
                Result queue, queuewait, devicewait;
                queue = Renderer.vk!.QueueSubmit(Renderer.graphicsQueue, 1, ref submitInfo, default);
                queuewait = Renderer.vk!.QueueWaitIdle(Renderer.graphicsQueue);
                devicewait = Renderer.vk!.DeviceWaitIdle(Renderer.logicalDevice);
                if (queue != Result.Success && queue != Result.Success && queue != Result.Success)
                {
                    Console.WriteLine("Exception thrown");
                    throw new Exception("failed to submit single time commands");
                }

                Renderer.vk!.FreeCommandBuffers(Renderer.logicalDevice, Renderer.commandPool, 1, _cptr);
            }
        }
    }
}