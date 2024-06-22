using ArctisAurora.EngineWork.ECS.RenderingComponents.Vulkan;
using ArctisAurora.EngineWork.Rendering.Renderers.Vulkan;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using System.Runtime.CompilerServices;
using Buffer = Silk.NET.Vulkan.Buffer;

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

        internal void UpdateUniformBuffer(ref AVulkanMeshComponent _sender, AVulkanCamera _camera, uint _currentImage, Extent2D _extent)
        {
            UBO _ubo = new UBO()
            {
                _model = _sender._instanceMatrices[0],
                _view = _camera._view,
                _projection = _camera._projection
            };
            _ubo._projection.M22 *= -1;

            void* _data;
            VulkanRenderer._vulkan.MapMemory(VulkanRenderer._logicalDevice, _uniformBuffersMemory[_currentImage], 0, (ulong)Unsafe.SizeOf<UBO>(), 0, &_data);
            new Span<UBO>(_data, 1)[0] = _ubo;
            VulkanRenderer._vulkan.UnmapMemory(VulkanRenderer._logicalDevice, _uniformBuffersMemory[_currentImage]);
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