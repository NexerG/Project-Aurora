using ArctisAurora.EngineWork.ComponentBehaviour;
using ArctisAurora.EngineWork.Rendering.Renderers.Renderer_Vulkan;
using ArctisAurora.EngineWork.Rendering.Renderers.Vulkan;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using System.Runtime.CompilerServices;
using Buffer = Silk.NET.Vulkan.Buffer;
using ImageLayout = Silk.NET.Vulkan.ImageLayout;

namespace ArctisAurora.EngineWork.ECS.RenderingComponents.Vulkan
{
    internal unsafe class AVulkanMeshComponent
    {
        bool _render = true;
        internal AVulkanMesh _mesh = new AVulkanMesh();

        //buffer handler and descriptor set
        internal AVulkanBufferHandler _bufferHandler;
        internal DescriptorSet[] _descriptorSets;

        int _instances = 1;
        internal List<Matrix4X4<float>> _instanceMatrices = new List<Matrix4X4<float>>();

        internal AVulkanMeshComponent()
        {
            if (_mesh != null)
            {
                _bufferHandler = new AVulkanBufferHandler();
                _bufferHandler.CreateVertexBuffer(ref _mesh._vertices);
                _bufferHandler.CreateIndexBuffer(ref _mesh._indices);
                SingletonMatrix();
                _bufferHandler.CreateTextureBuffer();
                _bufferHandler.CreateImageView();
            }
        }

        internal void MakeInstanced(int _instanceCount, ref List<Matrix4X4<float>> _matrices)
        {
            _instances = _instanceCount;
            _instanceMatrices = _matrices;
        }

        internal void SingletonMatrix()
        {
            Vector3D<float> _pos = new Vector3D<float>(0, 0, 0);
            Quaternion<float> q = Quaternion<float>.CreateFromYawPitchRoll(0,0,0);

            Matrix4X4<float> _transform = Matrix4X4<float>.Identity;
            //_transform *= Matrix4X4.CreateScale(new Vector3D<float>(1,1,1));
            //_transform *= Matrix4X4.CreateFromQuaternion(q);
            //_transform *= Matrix4X4.CreateTranslation(_pos);
            
            _instanceMatrices.Add(_transform);
        }

        internal void CreateDescriptorSet()
        {
            DescriptorSetLayout[] _layouts = new DescriptorSetLayout[VulkanRenderer._swapchain!._swapchainImages.Length];
            Array.Fill(_layouts, VulkanRenderer._descriptorSetLayout);

            fixed (DescriptorSetLayout* _layoutsPtr = _layouts)
            {
                DescriptorSetAllocateInfo _allocateInfo = new DescriptorSetAllocateInfo()
                {
                    SType = StructureType.DescriptorSetAllocateInfo,
                    DescriptorPool = VulkanRenderer._descriptorPool,
                    DescriptorSetCount = (uint)VulkanRenderer._swapchain!._swapchainImages.Length,
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

                DescriptorImageInfo _imageInfo = new DescriptorImageInfo()
                {
                    ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
                    ImageView = _bufferHandler._textureImageView,
                    Sampler = VulkanRenderer._textureSampler
                };

                var _writeDescriptorSets = new WriteDescriptorSet[]
                {
                    new WriteDescriptorSet()
                    {
                        SType = StructureType.WriteDescriptorSet,
                        DstSet = _descriptorSets[i],
                        DstBinding = 0,
                        DstArrayElement = 0,
                        DescriptorType = DescriptorType.UniformBuffer,
                        DescriptorCount = 1,
                        PBufferInfo = &_bufferInfo
                    },
                    new WriteDescriptorSet()
                    {
                        SType = StructureType.WriteDescriptorSet,
                        DstSet = _descriptorSets[i],
                        DstBinding = 1,
                        DstArrayElement = 0,
                        DescriptorType = DescriptorType.CombinedImageSampler,
                        DescriptorCount = 1,
                        PImageInfo = &_imageInfo
                    }
                };
                fixed(WriteDescriptorSet* _descPtr = _writeDescriptorSets)
                {
                    VulkanRenderer._vulkan!.UpdateDescriptorSets(VulkanRenderer._logicalDevice, (uint)_writeDescriptorSets.Length, _descPtr, 0, null);
                }
            }
        }

        internal void UpdateMatrices()
        {

            Vector3D<float> _pos = new Vector3D<float>(0, 0, 0);

            Matrix4X4<float> _transform = Matrix4X4<float>.Identity;
            //_transform *= Matrix4X4.CreateScale(new Vector3D<float>(1, 1, 1));
            //_transform *= Matrix4X4.CreateTranslation(_pos);

            _instanceMatrices[0] = _transform;
        }

        internal void EnqueueDrawCommands(ulong[] _offset, int _loopIndex, ref CommandBuffer _commandBuffer)
        {
            if (_render)
            {
                Buffer[] _vertBuffer = new Buffer[] { _bufferHandler._vertexBuffer };
                fixed (ulong* _offsetsPtr = _offset)
                fixed (Buffer* _vertBuffersPtr = _vertBuffer)
                {
                    VulkanRenderer._vulkan.CmdBindVertexBuffers(_commandBuffer, 0, 1, _vertBuffersPtr, _offsetsPtr);
                }
                VulkanRenderer._vulkan.CmdBindIndexBuffer(_commandBuffer, _bufferHandler._indexBuffer, 0, IndexType.Uint16);
                VulkanRenderer._vulkan.CmdBindDescriptorSets(_commandBuffer, PipelineBindPoint.Graphics, VulkanRenderer._pipeline._pipelineLayout, 0, 1, _descriptorSets[_loopIndex], 0, null);
                VulkanRenderer._vulkan.CmdDrawIndexed(_commandBuffer, (uint)_mesh._indices.Length, (uint)_instances, 0, 0, 0);
            }
        }
    }
}