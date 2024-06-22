using ArctisAurora.EngineWork.Rendering.Renderers.Renderer_Vulkan;
using ArctisAurora.EngineWork.Rendering.Renderers.Vulkan;
using Silk.NET.Vulkan;
using System.Runtime.CompilerServices;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace ArctisAurora.EngineWork.ECS.RenderingComponents.Vulkan
{
    internal unsafe class AVulkanMeshComponent
    {
        bool _render = true;
        internal AVulkanMesh _mesh = new AVulkanMesh();

        //Vertex objects & buffers
        //internal VVAO _vao;
        //internal VVBO _vbo;
        //internal VEBO _ebo;
        internal AVulkanBufferHandler _bufferHandler;
        internal DescriptorSet[] _descriptorSets;

        int _instances = 1;

        //internal List<Matrix4x4> _instanceMatrix = new List<Matrix4x4>();

        internal AVulkanMeshComponent()
        {
            if (_mesh != null)
            {
                _bufferHandler = new AVulkanBufferHandler();
                _bufferHandler.CreateVertexBuffer(ref _mesh._vertices);
                _bufferHandler.CreateIndexBuffer(ref _mesh._indices);
            }
        }

        internal void CreateDescriptorSet()
        {
            DescriptorSetLayout[] _layouts = new DescriptorSetLayout[VulkanRenderer._swapchain._swapchainImages.Length];
            Array.Fill(_layouts, VulkanRenderer._descriptorSetLayout);

            fixed (DescriptorSetLayout* _layoutsPtr = _layouts)
            {
                DescriptorSetAllocateInfo _allocateInfo = new DescriptorSetAllocateInfo()
                {
                    SType = StructureType.DescriptorSetAllocateInfo,
                    DescriptorPool = VulkanRenderer._descriptorPool,
                    DescriptorSetCount = (uint)VulkanRenderer._swapchain._swapchainImages.Length,
                    PSetLayouts = _layoutsPtr
                };

                _descriptorSets = new DescriptorSet[VulkanRenderer._swapchain._swapchainImages.Length];
                fixed (DescriptorSet* _descriptorSetsPtr = _descriptorSets)
                {
                    if (VulkanRenderer._vulkan.AllocateDescriptorSets(VulkanRenderer._logicalDevice, _allocateInfo, _descriptorSetsPtr) != Result.Success)
                    {
                        throw new Exception("Failed to allocate descriptor set");
                    }
                }
            }
            for (int i = 0; i < VulkanRenderer._swapchain._swapchainImages.Length; i++)
            {
                DescriptorBufferInfo _bufferInfo = new DescriptorBufferInfo()
                {
                    Buffer = _bufferHandler._uniformBuffers[i],
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
                VulkanRenderer._vulkan.UpdateDescriptorSets(VulkanRenderer._logicalDevice, 1, _descriptorWrite, 0, null);
            }
        }

        internal void Draw(ulong[] _offset, int _loopIndex, ref CommandBuffer _commandBuffer)
        {
            Buffer[] _vertBuffer = new Buffer[] { _bufferHandler._vertexBuffer };
            fixed (ulong* _offsetsPtr = _offset)
            fixed (Buffer* _vertBuffersPtr = _vertBuffer)
            {
                VulkanRenderer._vulkan.CmdBindVertexBuffers(_commandBuffer, 0, 1, _vertBuffersPtr, _offsetsPtr);
            }
            VulkanRenderer._vulkan.CmdBindIndexBuffer(_commandBuffer, _bufferHandler._indexBuffer, 0, IndexType.Uint16);
            VulkanRenderer._vulkan.CmdBindDescriptorSets(_commandBuffer, PipelineBindPoint.Graphics, VulkanRenderer._pipeline._pipelineLayout, 0, 1, _descriptorSets[_loopIndex], 0, null);
            VulkanRenderer._vulkan.CmdDrawIndexed(_commandBuffer, (uint)_mesh._indices.Length, 1, 0, 0, 0);
        }
    }
}