using ArctisAurora.EngineWork.Renderer.RendererTypes;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Buffer = Silk.NET.Vulkan.Buffer;
using Format = Silk.NET.Vulkan.Format;
using Image = SixLabors.ImageSharp.Image;
using ImageLayout = Silk.NET.Vulkan.ImageLayout;

namespace ArctisAurora.EngineWork.Renderer.Helpers
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
            VulkanRenderer._vulkan.MapMemory(VulkanRenderer._logicalDevice, _stagingBufferMemory, 0, _imageSize, 0, &_data);
            _image.CopyPixelDataTo(new Span<byte>(_data, (int)_imageSize));
            VulkanRenderer._vulkan.UnmapMemory(VulkanRenderer._logicalDevice, _stagingBufferMemory);

            CreateImage((uint)_image.Width, (uint)_image.Height, imageFormat, ImageTiling.Optimal, ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit, MemoryPropertyFlags.DeviceLocalBit, ref _textureImage, ref _textureBufferMemory);

            TransitionImageLayout(_textureImage, ImageLayout.Undefined, ImageLayout.TransferDstOptimal);
            CopyBufferToImage(_stagingBuffer, _textureImage, (uint)_image.Width, (uint)_image.Height);
            TransitionImageLayout(_textureImage, ImageLayout.TransferDstOptimal, ImageLayout.ShaderReadOnlyOptimal);

            VulkanRenderer._vulkan.DestroyBuffer(VulkanRenderer._logicalDevice, _stagingBuffer, null);
            VulkanRenderer._vulkan.FreeMemory(VulkanRenderer._logicalDevice, _stagingBufferMemory, null);
        }

        internal static void CreateTextureBuffer(ref Silk.NET.Vulkan.Image _textureImage, ref DeviceMemory _textureBufferMemory, ref Image<Rgba32> image, Format imageFormat)
        {
            using var _image = image;
            ulong _imageSize = (ulong)(_image.Width * _image.Height * _image.PixelType.BitsPerPixel);

            Buffer _stagingBuffer = default;
            DeviceMemory _stagingBufferMemory = default;
            CreateBuffer(_imageSize, ref _stagingBuffer, ref _stagingBufferMemory, BufferUsageFlags.TransferSrcBit, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);

            void* _data;
            VulkanRenderer._vulkan.MapMemory(VulkanRenderer._logicalDevice, _stagingBufferMemory, 0, _imageSize, 0, &_data);
            _image.CopyPixelDataTo(new Span<byte>(_data, (int)_imageSize));
            VulkanRenderer._vulkan.UnmapMemory(VulkanRenderer._logicalDevice, _stagingBufferMemory);

            CreateImage((uint)_image.Width, (uint)_image.Height, imageFormat, ImageTiling.Optimal, ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit, MemoryPropertyFlags.DeviceLocalBit, ref _textureImage, ref _textureBufferMemory);

            TransitionImageLayout(_textureImage, ImageLayout.Undefined, ImageLayout.TransferDstOptimal);
            CopyBufferToImage(_stagingBuffer, _textureImage, (uint)_image.Width, (uint)_image.Height);
            TransitionImageLayout(_textureImage, ImageLayout.TransferDstOptimal, ImageLayout.ShaderReadOnlyOptimal);

            VulkanRenderer._vulkan.DestroyBuffer(VulkanRenderer._logicalDevice, _stagingBuffer, null);
            VulkanRenderer._vulkan.FreeMemory(VulkanRenderer._logicalDevice, _stagingBufferMemory, null);
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

            VulkanRenderer._vulkan!.CmdCopyBufferToImage(_commandBuffer, _buffer, _image, ImageLayout.TransferDstOptimal, 1, ref _bufferImageCopy);
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

            VulkanRenderer._vulkan!.CmdPipelineBarrier(_commandBuffer, sourceStage, destinationStage, 0, 0, null, 0, null, 1, _barrier);
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
                if (VulkanRenderer._vulkan!.CreateImage(VulkanRenderer._logicalDevice, ref _imageInfo, null, imagePtr) != Result.Success)
                {
                    throw new Exception("failed to create image!");
                }
            }

            VulkanRenderer._vulkan.GetImageMemoryRequirements(VulkanRenderer._logicalDevice, _im, out MemoryRequirements _memReqs);

            MemoryAllocateInfo allocInfo = new()
            {
                SType = StructureType.MemoryAllocateInfo,
                AllocationSize = _memReqs.Size,
                MemoryTypeIndex = FindMemoryType(_memReqs.MemoryTypeBits, _properties),
            };

            fixed (DeviceMemory* imageMemoryPtr = &_devMemory)
            {
                if (VulkanRenderer._vulkan!.AllocateMemory(VulkanRenderer._logicalDevice, ref allocInfo, null, imageMemoryPtr) != Result.Success)
                {
                    throw new Exception("failed to allocate image memory!");
                }
            }

            VulkanRenderer._vulkan!.BindImageMemory(VulkanRenderer._logicalDevice, _im, _devMemory, 0);
        }

        internal static void CreateImageView(ref Silk.NET.Vulkan.Image _textureImage, ref ImageView _imageView, Format imageFormat)
        {
            ImageViewCreateInfo _createInfo = new ImageViewCreateInfo
            {
                SType = StructureType.ImageViewCreateInfo,
                Image = _textureImage,
                Format = imageFormat,
                ViewType = ImageViewType.Type2D
            };

            _createInfo.SubresourceRange.AspectMask = ImageAspectFlags.ColorBit;
            _createInfo.SubresourceRange.BaseMipLevel = 0;
            _createInfo.SubresourceRange.LevelCount = 1;
            _createInfo.SubresourceRange.BaseArrayLayer = 0;
            _createInfo.SubresourceRange.LayerCount = 1;

            if (VulkanRenderer._vulkan!.CreateImageView(VulkanRenderer._logicalDevice, _createInfo, null, out _imageView) != Result.Success)
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
            VulkanRenderer._vulkan.MapMemory(VulkanRenderer._logicalDevice, _stagingBufferMemory, 0, bufferSize, 0, &_dataPtr);
            data.AsSpan().CopyTo(new Span<T>(_dataPtr, data.Length));
            VulkanRenderer._vulkan.UnmapMemory(VulkanRenderer._logicalDevice, _stagingBufferMemory);

            CreateBuffer(bufferSize, ref buffer, ref memory, defaultBufferFlags | usageFlags, defaultStagingMemoryFlags);

            CopyBuffer(ref _stagingBuffer, ref buffer, bufferSize);
            VulkanRenderer._vulkan.DestroyBuffer(VulkanRenderer._logicalDevice, _stagingBuffer, null);
            VulkanRenderer._vulkan.FreeMemory(VulkanRenderer._logicalDevice, _stagingBufferMemory, null);
        }

        internal static void CreateBuffer<T>(ref T data, ref Buffer buffer, ref DeviceMemory memory, BufferUsageFlags usageFlags) where T : unmanaged
        {
            ulong bufferSize = (ulong)(sizeof(T));

            Buffer _stagingBuffer = default;
            DeviceMemory _stagingBufferMemory = default;
            CreateBuffer(bufferSize, ref _stagingBuffer, ref _stagingBufferMemory, defaultStagingBufferFlags, defaultStagingMemoryFlags);

            void* _dataPtr;
            VulkanRenderer._vulkan.MapMemory(VulkanRenderer._logicalDevice, _stagingBufferMemory, 0, bufferSize, 0, &_dataPtr);
            new Span<T>(_dataPtr, 1)[0] = data;
            VulkanRenderer._vulkan.UnmapMemory(VulkanRenderer._logicalDevice, _stagingBufferMemory);

            CreateBuffer(bufferSize, ref buffer, ref memory, defaultBufferFlags | usageFlags, defaultStagingMemoryFlags);

            CopyBuffer(ref _stagingBuffer, ref buffer, bufferSize);
            VulkanRenderer._vulkan.DestroyBuffer(VulkanRenderer._logicalDevice, _stagingBuffer, null);
            VulkanRenderer._vulkan.FreeMemory(VulkanRenderer._logicalDevice, _stagingBufferMemory, null);
        }

        internal static void UpdateBuffer<T>(ref T[] data, ref Buffer buffer, ref DeviceMemory memory, BufferUsageFlags usageFlags) where T : unmanaged
        {
            ulong bufferSize = (ulong)(sizeof(T) * data.Length);

            Buffer _stagingBuffer = default;
            DeviceMemory _stagingBufferMemory = default;
            CreateBuffer(bufferSize, ref _stagingBuffer, ref _stagingBufferMemory, defaultStagingBufferFlags, defaultStagingMemoryFlags);

            void* _dataPtr;
            VulkanRenderer._vulkan.MapMemory(VulkanRenderer._logicalDevice, _stagingBufferMemory, 0, bufferSize, 0, &_dataPtr);
            data.AsSpan().CopyTo(new Span<T>(_dataPtr, data.Length));
            VulkanRenderer._vulkan.UnmapMemory(VulkanRenderer._logicalDevice, _stagingBufferMemory);

            CopyBuffer(ref _stagingBuffer, ref buffer, bufferSize);
            VulkanRenderer._vulkan.DestroyBuffer(VulkanRenderer._logicalDevice, _stagingBuffer, null);
            VulkanRenderer._vulkan.FreeMemory(VulkanRenderer._logicalDevice, _stagingBufferMemory, null);
        }

        internal static void UpdateBuffer<T>(ref T data, ref Buffer buffer, ref DeviceMemory memory, BufferUsageFlags usageFlags) where T : unmanaged
        {
            ulong bufferSize = (ulong)(sizeof(T));

            Buffer _stagingBuffer = default;
            DeviceMemory _stagingBufferMemory = default;
            CreateBuffer(bufferSize, ref _stagingBuffer, ref _stagingBufferMemory, defaultStagingBufferFlags, defaultStagingMemoryFlags);

            void* _dataPtr;
            VulkanRenderer._vulkan.MapMemory(VulkanRenderer._logicalDevice, _stagingBufferMemory, 0, bufferSize, 0, &_dataPtr);
            new Span<T>(_dataPtr, 1)[0] = data;
            VulkanRenderer._vulkan.UnmapMemory(VulkanRenderer._logicalDevice, _stagingBufferMemory);

            CopyBuffer(ref _stagingBuffer, ref buffer, bufferSize);
            VulkanRenderer._vulkan.DestroyBuffer(VulkanRenderer._logicalDevice, _stagingBuffer, null);
            VulkanRenderer._vulkan.FreeMemory(VulkanRenderer._logicalDevice, _stagingBufferMemory, null);
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
                if (VulkanRenderer._vulkan.CreateBuffer(VulkanRenderer._logicalDevice, ref _bufferCreateInfo, null, _bufferPtr) != Result.Success)
                {
                    throw new Exception("Failed to create a buffer");
                }
            }
            MemoryRequirements _memReqs = new MemoryRequirements();
            VulkanRenderer._vulkan.GetBufferMemoryRequirements(VulkanRenderer._logicalDevice, _buffer, out _memReqs);

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
                if (VulkanRenderer._vulkan.AllocateMemory(VulkanRenderer._logicalDevice, ref _allocateInfo, null, _bufferMemoryPtr) != Result.Success)
                {
                    throw new Exception("Failed to allocate buffer memory");
                }
            }

            VulkanRenderer._vulkan.BindBufferMemory(VulkanRenderer._logicalDevice, _buffer, _bufferMemory, 0);
        }

        private static void CopyBuffer(ref Buffer _sourceBuffer, ref Buffer _dstBuffer, ulong bufferSize)
        {
            CommandBufferAllocateInfo _allocInfo = new CommandBufferAllocateInfo()
            {
                SType = StructureType.CommandBufferAllocateInfo,
                Level = CommandBufferLevel.Primary,
                CommandPool = VulkanRenderer._commandPool,
                CommandBufferCount = 1
            };
            CommandBuffer _localCommandBuffer;
            VulkanRenderer._vulkan.AllocateCommandBuffers(VulkanRenderer._logicalDevice, _allocInfo, out _localCommandBuffer);

            CommandBufferBeginInfo _cBBeginInfo = new CommandBufferBeginInfo()
            {
                SType = StructureType.CommandBufferBeginInfo,
                Flags = CommandBufferUsageFlags.OneTimeSubmitBit
            };
            VulkanRenderer._vulkan.BeginCommandBuffer(_localCommandBuffer, _cBBeginInfo);

            BufferCopy _copyRegion = new BufferCopy()
            {
                Size = bufferSize
            };
            VulkanRenderer._vulkan.CmdCopyBuffer(_localCommandBuffer, _sourceBuffer, _dstBuffer, 1, _copyRegion);
            VulkanRenderer._vulkan.EndCommandBuffer(_localCommandBuffer);

            SubmitInfo _subInfo = new SubmitInfo()
            {
                SType = StructureType.SubmitInfo,
                CommandBufferCount = 1,
                PCommandBuffers = &_localCommandBuffer
            };
            Result queue, wait;
            queue = VulkanRenderer._vulkan.QueueSubmit(VulkanRenderer._graphicsQueue, 1, _subInfo, default);
            wait = VulkanRenderer._vulkan.QueueWaitIdle(VulkanRenderer._graphicsQueue);
            if (queue != Result.Success && wait != Result.Success)
            {
                Console.WriteLine("Exception thrown");
                throw new Exception("failed to submit 'copy buffer' commands");
            }
            VulkanRenderer._vulkan.FreeCommandBuffers(VulkanRenderer._logicalDevice, VulkanRenderer._commandPool, 1, _localCommandBuffer);
        }

        internal static uint FindMemoryType(uint _typeFilter, MemoryPropertyFlags _properties)
        {
            PhysicalDeviceMemoryProperties _memProperties;
            VulkanRenderer._vulkan.GetPhysicalDeviceMemoryProperties(VulkanRenderer._gpu, out _memProperties);

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
                CommandPool = VulkanRenderer._commandPool,
                CommandBufferCount = 1,
            };

            VulkanRenderer._vulkan!.AllocateCommandBuffers(VulkanRenderer._logicalDevice, _allocInfo, out CommandBuffer _commandBuffer);

            CommandBufferBeginInfo beginInfo = new CommandBufferBeginInfo()
            {
                SType = StructureType.CommandBufferBeginInfo,
                Flags = CommandBufferUsageFlags.OneTimeSubmitBit,
            };

            VulkanRenderer._vulkan!.BeginCommandBuffer(_commandBuffer, beginInfo);

            return _commandBuffer;
        }

        internal static void EndSingleTimeCommands(ref CommandBuffer commandBuffer)
        {
            VulkanRenderer._vulkan!.EndCommandBuffer(commandBuffer);

            fixed (CommandBuffer* _cptr = &commandBuffer)
            {
                SubmitInfo submitInfo = new()
                {
                    SType = StructureType.SubmitInfo,
                    CommandBufferCount = 1,
                    PCommandBuffers = _cptr,
                };
                Result queue, queuewait, devicewait;
                queue = VulkanRenderer._vulkan!.QueueSubmit(VulkanRenderer._graphicsQueue, 1, ref submitInfo, default);
                queuewait = VulkanRenderer._vulkan!.QueueWaitIdle(VulkanRenderer._graphicsQueue);
                devicewait = VulkanRenderer._vulkan!.DeviceWaitIdle(VulkanRenderer._logicalDevice);
                if (queue != Result.Success && queue != Result.Success && queue != Result.Success)
                {
                    Console.WriteLine("Exception thrown");
                    throw new Exception("failed to submit single time commands");
                }

                VulkanRenderer._vulkan!.FreeCommandBuffers(VulkanRenderer._logicalDevice, VulkanRenderer._commandPool, 1, _cptr);
            }
        }
    }
}