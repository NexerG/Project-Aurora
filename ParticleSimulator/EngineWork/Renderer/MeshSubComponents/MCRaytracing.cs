using ArctisAurora.EngineWork.ECS.RenderingComponents.Vulkan;
using ArctisAurora.EngineWork.Renderer.Helpers;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using ImageLayout = Silk.NET.Vulkan.ImageLayout;

namespace ArctisAurora.EngineWork.Renderer.MeshSubComponents
{
    internal unsafe class MCRaytracing : MeshComponent
    {
        internal MCRaytracing()
        {
            _aditionalUsageFlags = BufferUsageFlags.AccelerationStructureBuildInputReadOnlyBitKhr;
            AVulkanBufferHandler.CreateVertexBuffer(ref _mesh._vertices, ref _vertexBuffer, ref _vertexBufferMemory, _aditionalUsageFlags);
            AVulkanBufferHandler.CreateIndexBuffer(ref _mesh._indices, ref _indexBuffer, ref _indexBufferMemory, _aditionalUsageFlags);
        }

        public override void OnStart()
        {
            base.OnStart();
            CreateDescriptorSet();
        }

        internal override void FreeDescriptorSets()
        {
            base.FreeDescriptorSets();
            if (_descriptorSets != null)
                VulkanRenderer._vulkan.FreeDescriptorSets(VulkanRenderer._logicalDevice, Rasterizer._descriptorPool, (uint)_descriptorSets.Length, _descriptorSets);
        }

        internal override void ReinstantiateDesriptorSets()
        {
            base.ReinstantiateDesriptorSets();
            CreateDescriptorSet();
        }

        internal override void CreateDescriptorSet()
        {
            DescriptorSetLayout[] _layouts = new DescriptorSetLayout[Pathtracing._swapchain!._swapchainImages.Length];
            Array.Fill(_layouts, Pathtracing._descriptorSetLayout);

            fixed (DescriptorSetLayout* _layoutsPtr = _layouts)
            {
                DescriptorSetAllocateInfo _allocateInfo = new DescriptorSetAllocateInfo()
                {
                    SType = StructureType.DescriptorSetAllocateInfo,
                    DescriptorPool = Pathtracing._descriptorPool,
                    DescriptorSetCount = (uint)VulkanRenderer._swapimageCount,
                    PSetLayouts = _layoutsPtr
                };

                _descriptorSets = new DescriptorSet[Pathtracing._swapchain!._swapchainImages.Length];
                fixed (DescriptorSet* _descriptorSetsPtr = _descriptorSets)
                {
                    Result r = VulkanRenderer._vulkan.AllocateDescriptorSets(VulkanRenderer._logicalDevice, _allocateInfo, _descriptorSetsPtr);
                    if (r != Result.Success)
                    {
                        throw new Exception("Failed to allocate descriptor set with error code: " + r);
                    }
                }
            }
            for (int i = 0; i < Pathtracing._swapchain!._swapchainImages.Length; i++)
            {
                fixed (AccelerationStructureKHR* _accelStrPtr = &Pathtracing._TLAS._handle)
                {
                    WriteDescriptorSetAccelerationStructureKHR _dasinfo = new()
                    {
                        SType = StructureType.WriteDescriptorSetAccelerationStructureKhr,
                        AccelerationStructureCount = 1,
                        PAccelerationStructures = _accelStrPtr,
                    };
                    DescriptorImageInfo _dImageInfo = new()
                    {
                        ImageLayout = ImageLayout.General,
                        ImageView = Pathtracing._storageImageView[i]
                    };
                    DescriptorBufferInfo _bufferInfoMatrices = new DescriptorBufferInfo()
                    {
                        Buffer = VulkanRenderer._camera._cameraBuffer[i],
                        Offset = 0,
                        Range = (ulong)Unsafe.SizeOf<UBO>()
                    };

                    var _writeDescriptorSets = new WriteDescriptorSet[]
                    {
                        new WriteDescriptorSet
                        {
                            SType = StructureType.WriteDescriptorSet,
                            PNext = &_dasinfo,
                            DstSet = _descriptorSets[i],
                            DstBinding = 0,
                            DstArrayElement = 0,
                            DescriptorCount = 1,
                            DescriptorType = DescriptorType.AccelerationStructureKhr
                        },
                        new WriteDescriptorSet
                        {
                            SType = StructureType.WriteDescriptorSet,
                            DstSet = _descriptorSets[i],
                            DstBinding = 1,
                            DescriptorCount = 1,
                            DstArrayElement = 0,
                            DescriptorType = DescriptorType.StorageImage,
                            PImageInfo = &_dImageInfo
                        },
                        new WriteDescriptorSet
                        {
                            SType = StructureType.WriteDescriptorSet,
                            DstSet = _descriptorSets[i],
                            DstBinding = 2,
                            DescriptorCount = 1,
                            DstArrayElement = 0,
                            DescriptorType = DescriptorType.UniformBuffer,
                            PBufferInfo = &_bufferInfoMatrices
                        }
                    };
                    fixed (WriteDescriptorSet* _descPtr = _writeDescriptorSets)
                    {
                        VulkanRenderer._vulkan!.UpdateDescriptorSets(VulkanRenderer._logicalDevice, (uint)_writeDescriptorSets.Length, _descPtr, 0, null);
                    }
                }
            }
        }

        internal override void MakeInstanced(ref List<Matrix4X4<float>> _matrices)
        {
            base.MakeInstanced(ref _matrices);
            //wil have to adjust later because this should have to remake a TLAS
        }

        internal override void SingletonMatrix()
        {
            base.SingletonMatrix();
            AVulkanBufferHandler.CreateTransformBuffer(ref _transformMatrices, ref _trasnformsBuffer, ref _trasnformsBufferMemory, _aditionalUsageFlags);
        }

        internal override void EnqueueDrawCommands(ulong[] _offset, int _loopIndex, ref CommandBuffer _commandBuffer)
        {
            base.EnqueueDrawCommands(_offset, _loopIndex, ref _commandBuffer);
        }
    }
}