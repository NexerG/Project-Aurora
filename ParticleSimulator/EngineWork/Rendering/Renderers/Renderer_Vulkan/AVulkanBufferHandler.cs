using ArctisAurora.EngineWork.ECS.RenderingComponents.Vulkan;
using ArctisAurora.EngineWork.Rendering.Renderers.Vulkan;
using Assimp;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using StbImageSharp;
using System.Runtime.CompilerServices;
using System.Threading;
using Buffer = Silk.NET.Vulkan.Buffer;
using Format = Silk.NET.Vulkan.Format;
using Image = SixLabors.ImageSharp.Image;
using ImageLayout = Silk.NET.Vulkan.ImageLayout;

namespace ArctisAurora.EngineWork.Rendering.Renderers.Renderer_Vulkan
{
    internal struct UBO
    {
        public Matrix4X4<float> _model;
        public Matrix4X4<float> _view;
        public Matrix4X4<float> _projection;
    }

    internal unsafe class AVulkanBufferHandler
    {
        internal Buffer _vertexBuffer;
        internal DeviceMemory _vertexBufferMemory;

        internal Buffer _indexBuffer;
        internal DeviceMemory _indexBufferMemory;

        internal Buffer[] _uniformBuffers;
        internal DeviceMemory[] _uniformBuffersMemory;

        internal Silk.NET.Vulkan.Image _textureImage;
        internal ImageView _textureImageView;
        internal DeviceMemory _textureBufferMemory;

        internal void CreateVertexBuffer(ref Vertex[] _vertices)
        {
            ulong _bufferSize = (ulong)(Unsafe.SizeOf<Vertex>() * _vertices.Length);
            Buffer _stagingBuffer = default;
            DeviceMemory _stagingBufferMemory = default;
            CreateBuffer(_bufferSize, BufferUsageFlags.TransferSrcBit, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCachedBit, ref _stagingBuffer, ref _stagingBufferMemory);

            void* _data;
            VulkanRenderer._vulkan.MapMemory(VulkanRenderer._logicalDevice, _stagingBufferMemory, 0, _bufferSize, 0, &_data);
            _vertices.AsSpan().CopyTo(new Span<Vertex>(_data, _vertices.Length));
            VulkanRenderer._vulkan.UnmapMemory(VulkanRenderer._logicalDevice, _stagingBufferMemory);

            CreateBuffer(_bufferSize, BufferUsageFlags.TransferDstBit | BufferUsageFlags.VertexBufferBit, MemoryPropertyFlags.DeviceLocalBit, ref _vertexBuffer, ref _vertexBufferMemory);

            CopyBuffer(ref _stagingBuffer, ref _vertexBuffer, _bufferSize);
            VulkanRenderer._vulkan.DestroyBuffer(VulkanRenderer._logicalDevice, _stagingBuffer, null);
            VulkanRenderer._vulkan.FreeMemory(VulkanRenderer._logicalDevice, _stagingBufferMemory, null);
        }

        internal void CreateIndexBuffer(ref ushort[] _meshIndices)
        {
            ulong _bufferSize = ((ulong)(Unsafe.SizeOf<ushort>() * _meshIndices.Length));
            Buffer _stagingBuffer = default;
            DeviceMemory _stagingBufferMemory = default;
            CreateBuffer(_bufferSize, BufferUsageFlags.TransferSrcBit, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit, ref _stagingBuffer, ref _stagingBufferMemory);
            void* _data;
            VulkanRenderer._vulkan.MapMemory(VulkanRenderer._logicalDevice, _stagingBufferMemory, 0, _bufferSize, 0, &_data);
            _meshIndices.AsSpan().CopyTo(new Span<ushort>(_data, _meshIndices.Length));
            VulkanRenderer._vulkan.UnmapMemory(VulkanRenderer._logicalDevice, _stagingBufferMemory);
            CreateBuffer(_bufferSize, BufferUsageFlags.TransferDstBit | BufferUsageFlags.IndexBufferBit, MemoryPropertyFlags.DeviceLocalBit, ref _indexBuffer, ref _indexBufferMemory); ;
            CopyBuffer(ref _stagingBuffer, ref _indexBuffer, _bufferSize);
            VulkanRenderer._vulkan.DestroyBuffer(VulkanRenderer._logicalDevice, _stagingBuffer, null);
            VulkanRenderer._vulkan.FreeMemory(VulkanRenderer._logicalDevice, _stagingBufferMemory, null);
        }

        internal void CreateUniformBuffer()
        {
            ulong _bufferSize = (ulong)Unsafe.SizeOf<UBO>();
            _uniformBuffers = new Buffer[VulkanRenderer._swapchain._swapchainImages.Length];
            _uniformBuffersMemory = new DeviceMemory[VulkanRenderer._swapchain._swapchainImages.Length];

            for (int i = 0; i < VulkanRenderer._swapchain._swapchainImages.Length; i++)
            {
                CreateBuffer(_bufferSize, BufferUsageFlags.UniformBufferBit, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit, ref _uniformBuffers[i], ref _uniformBuffersMemory[i]);
            }
        }

        internal void CreateTextureBuffer()
        {
            using var _image = Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>("../../../Shaders/Brick2.png");
            ulong _imageSize = (ulong)(_image.Width * _image.Height * _image.PixelType.BitsPerPixel / 8);

            Buffer _stagingBuffer = default;
            DeviceMemory _stagingBufferMemory = default;
            CreateBuffer(_imageSize, BufferUsageFlags.TransferSrcBit, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit, ref _stagingBuffer, ref _stagingBufferMemory);

            void* _data;
            VulkanRenderer._vulkan.MapMemory(VulkanRenderer._logicalDevice, _stagingBufferMemory, 0, _imageSize, 0, &_data);
            _image.CopyPixelDataTo(new Span<byte>(_data, (int)_imageSize));
            VulkanRenderer._vulkan.UnmapMemory(VulkanRenderer._logicalDevice, _stagingBufferMemory);

            CreateImage((uint)_image.Width, (uint)_image.Height, Format.R8G8B8A8Srgb, ImageTiling.Optimal, ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit, MemoryPropertyFlags.DeviceLocalBit);

            TransitionImageLayout(_textureImage, ImageLayout.Undefined, ImageLayout.TransferDstOptimal);
            CopyBufferToImage(_stagingBuffer, _textureImage, (uint)_image.Width, (uint)_image.Height);
            TransitionImageLayout(_textureImage, ImageLayout.TransferDstOptimal, ImageLayout.ShaderReadOnlyOptimal);

            VulkanRenderer._vulkan.DestroyBuffer(VulkanRenderer._logicalDevice, _stagingBuffer, null);
            VulkanRenderer._vulkan.FreeMemory(VulkanRenderer._logicalDevice, _stagingBufferMemory, null);
        }

        internal void UpdateUniformBuffer(ref AVulkanMeshComponent _sender, AVulkanCamera _camera, uint _currentImage, Extent2D _extent)
        {
            UBO _ubo = new UBO()
            {
                _model = _sender._instanceMatrices[0],
                _view = _camera._view,
                _projection = _camera._projection
            };

            void* _data;
            VulkanRenderer._vulkan.MapMemory(VulkanRenderer._logicalDevice, _uniformBuffersMemory[_currentImage], 0, (ulong)Unsafe.SizeOf<UBO>(), 0, &_data);
            new Span<UBO>(_data, 1)[0] = _ubo;
            VulkanRenderer._vulkan.UnmapMemory(VulkanRenderer._logicalDevice, _uniformBuffersMemory[_currentImage]);
        }

        private void CopyBufferToImage(Buffer _buffer, Silk.NET.Vulkan.Image _image, uint _width, uint _height)
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

            VulkanRenderer._vulkan!.CmdCopyBufferToImage(_commandBuffer, _buffer, _image, ImageLayout.TransferDstOptimal, 1, _bufferImageCopy);

            EndSingleTimeCommands(_commandBuffer);
        }

        private CommandBuffer BeginSingleTimeCommands()
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

        private void EndSingleTimeCommands(CommandBuffer commandBuffer)
        {
            VulkanRenderer._vulkan!.EndCommandBuffer(commandBuffer);

            SubmitInfo submitInfo = new()
            {
                SType = StructureType.SubmitInfo,
                CommandBufferCount = 1,
                PCommandBuffers = &commandBuffer,
            };

            VulkanRenderer._vulkan!.QueueSubmit(VulkanRenderer._graphicsQueue, 1, submitInfo, default);
            VulkanRenderer._vulkan!.QueueWaitIdle(VulkanRenderer._graphicsQueue);

            VulkanRenderer._vulkan!.FreeCommandBuffers(VulkanRenderer._logicalDevice, VulkanRenderer._commandPool, 1, commandBuffer);
        }

        private void TransitionImageLayout(Silk.NET.Vulkan.Image _image, ImageLayout _oldLayout, ImageLayout _newLayout)
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
            EndSingleTimeCommands(_commandBuffer);
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
                if (VulkanRenderer._vulkan.CreateBuffer(VulkanRenderer._logicalDevice, _bufferCreateInfo, null, _bufferPtr) != Result.Success)
                {
                    throw new Exception("Failed to create a VBO");
                }
            }
            MemoryRequirements _memReqs = new MemoryRequirements();
            VulkanRenderer._vulkan.GetBufferMemoryRequirements(VulkanRenderer._logicalDevice, _buffer, out _memReqs);

            MemoryAllocateInfo _allocateInfo = new MemoryAllocateInfo()
            {
                SType = StructureType.MemoryAllocateInfo,
                AllocationSize = _memReqs.Size,
                MemoryTypeIndex = FindMemoryType(_memReqs.MemoryTypeBits, _properties)
            };

            fixed (DeviceMemory* _bufferMemoryPtr = &_bufferMemory)
            {
                if (VulkanRenderer._vulkan.AllocateMemory(VulkanRenderer._logicalDevice, _allocateInfo, null, _bufferMemoryPtr) != Result.Success)
                {
                    throw new Exception("Failed to allocate vertex buffer memory");
                }
            }

            VulkanRenderer._vulkan.BindBufferMemory(VulkanRenderer._logicalDevice, _buffer, _bufferMemory, 0);
        }

        private void CopyBuffer(ref Buffer _sourceBuffer, ref Buffer _dstBuffer, ulong bufferSize)
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
            VulkanRenderer._vulkan.QueueSubmit(VulkanRenderer._graphicsQueue, 1, _subInfo, default);
            VulkanRenderer._vulkan.QueueWaitIdle(VulkanRenderer._graphicsQueue);
            VulkanRenderer._vulkan.FreeCommandBuffers(VulkanRenderer._logicalDevice, VulkanRenderer._commandPool, 1, _localCommandBuffer);
        }

        private void CreateImage(uint _width, uint _height, Format _format, ImageTiling _tiling, ImageUsageFlags _usage, MemoryPropertyFlags _properties)
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

            fixed (Silk.NET.Vulkan.Image* imagePtr = &_textureImage)
            {
                if (VulkanRenderer._vulkan!.CreateImage(VulkanRenderer._logicalDevice, _imageInfo, null, imagePtr) != Result.Success)
                {
                    throw new Exception("failed to create image!");
                }
            }

            VulkanRenderer._vulkan.GetImageMemoryRequirements(VulkanRenderer._logicalDevice, _textureImage, out MemoryRequirements _memReqs);

            MemoryAllocateInfo allocInfo = new()
            {
                SType = StructureType.MemoryAllocateInfo,
                AllocationSize = _memReqs.Size,
                MemoryTypeIndex = FindMemoryType(_memReqs.MemoryTypeBits, _properties),
            };

            fixed (DeviceMemory* imageMemoryPtr = &_textureBufferMemory)
            {
                if (VulkanRenderer._vulkan!.AllocateMemory(VulkanRenderer._logicalDevice, allocInfo, null, imageMemoryPtr) != Result.Success)
                {
                    throw new Exception("failed to allocate image memory!");
                }
            }

            VulkanRenderer._vulkan!.BindImageMemory(VulkanRenderer._logicalDevice, _textureImage, _textureBufferMemory, 0);
        }

        internal void CreateImageView()
        {
            ImageViewCreateInfo _createInfo = new ImageViewCreateInfo
            {
                Image = _textureImage,
                Format = Format.R8G8B8A8Srgb,
                ViewType = ImageViewType.Type2D
            };

            _createInfo.SubresourceRange.AspectMask = ImageAspectFlags.ColorBit;
            _createInfo.SubresourceRange.BaseMipLevel = 0;
            _createInfo.SubresourceRange.LevelCount = 1;
            _createInfo.SubresourceRange.BaseArrayLayer = 0;
            _createInfo.SubresourceRange.LayerCount = 1;

            if (VulkanRenderer._vulkan!.CreateImageView(VulkanRenderer._logicalDevice, _createInfo, null, out _textureImageView) != Result.Success)
            {
                throw new Exception("failed to create texture image view!");
            }
        }

        private uint FindMemoryType(uint _typeFilter, MemoryPropertyFlags _properties)
        {
            PhysicalDeviceMemoryProperties _memProperties;
            VulkanRenderer._vulkan.GetPhysicalDeviceMemoryProperties(VulkanRenderer._gpu, out _memProperties);

            for (int i = 0; i < _memProperties.MemoryTypeCount; i++)
            {
                if ((_typeFilter & (1 << i)) != 0 && (_memProperties.MemoryTypes[i].PropertyFlags & _properties) == _properties)
                {
                    return (uint)i;
                }
            }
            throw new Exception("Failed to find suitable memory type");
        }
    }
}