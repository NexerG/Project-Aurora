using ArctisAurora.EngineWork.ComponentBehaviour;
using ArctisAurora.EngineWork.Renderer;
using ArctisAurora.EngineWork.Renderer.Helpers;
using ArctisAurora.EngineWork.Renderer.MeshSubComponents;
using Assimp;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using System.Runtime.CompilerServices;
using Buffer = Silk.NET.Vulkan.Buffer;
using ImageLayout = Silk.NET.Vulkan.ImageLayout;

namespace ArctisAurora.EngineWork.ECS.RenderingComponents.Vulkan
{
    internal unsafe class MeshComponent : EntityComponent
    {
        internal bool _render = true;
        internal AVulkanMesh _mesh;
        //descriptor set
        internal DescriptorSet[] _descriptorSets;
        internal BufferUsageFlags _aditionalUsageFlags = BufferUsageFlags.None;

        //buffer objects
        internal Buffer _vertexBuffer;
        internal DeviceMemory _vertexBufferMemory;

        internal Buffer _indexBuffer;
        internal DeviceMemory _indexBufferMemory;

        internal Buffer _trasnformsBuffer;
        internal DeviceMemory _trasnformsBufferMemory;

        internal int _instances = 1;
        internal List<Matrix4X4<float>> _transformMatrices = new List<Matrix4X4<float>>();

        public MeshComponent()
        {
            _mesh = new AVulkanMesh();
        }

        public override void OnStart()
        {
            SingletonMatrix();
            VulkanRenderer._rendererInstance.AddEntityToRenderQueue(parent);
        }

        internal void LoadCustomMesh(Scene sc)
        {
            VulkanRenderer._vulkan.DestroyBuffer(VulkanRenderer._logicalDevice, _vertexBuffer, null);
            VulkanRenderer._vulkan.DestroyBuffer(VulkanRenderer._logicalDevice, _indexBuffer, null);
            VulkanRenderer._vulkan.FreeMemory(VulkanRenderer._logicalDevice, _indexBufferMemory, null);
            VulkanRenderer._vulkan.FreeMemory(VulkanRenderer._logicalDevice, _vertexBufferMemory, null);
            _mesh.LoadCustomMesh(sc);
            AVulkanBufferHandler.CreateVertexBuffer(ref _mesh._vertices, ref _vertexBuffer, ref _vertexBufferMemory, _aditionalUsageFlags);
            AVulkanBufferHandler.CreateIndexBuffer(ref _mesh._indices, ref _indexBuffer, ref _indexBufferMemory, _aditionalUsageFlags);
            VulkanRenderer._rendererInstance.RecreateCommandBuffers();
        }

        internal virtual void FreeDescriptorSets() { }

        internal virtual void ReinstantiateDesriptorSets() { }

        internal virtual void MakeInstanced(ref List<Matrix4X4<float>> _matrices) { }

        internal virtual void SingletonMatrix()
        {
            Quaternion<float> q = Quaternion<float>.CreateFromYawPitchRoll(0,0,0);
            Matrix4X4<float> _transform = Matrix4X4<float>.Identity;
            _transform *= Matrix4X4.CreateTranslation(parent.transform.position);
            //_transform *= Matrix4X4.CreateFromQuaternion(q);
            _transform *= Matrix4X4.CreateScale(parent.transform.scale);

            _transformMatrices.Add(_transform);
        }

        internal virtual void CreateDescriptorSet() { }

        internal void UpdateMatrices()
        {
            Quaternion<float> q = Quaternion<float>.CreateFromYawPitchRoll(30f * MathF.PI / 180f, 0, 0);
            Matrix4X4<float> _transform = Matrix4X4<float>.Identity;
            _transform *= Matrix4X4.CreateScale(parent.transform.scale);
            _transform *= Matrix4X4.CreateFromQuaternion(q);
            _transform *= Matrix4X4.CreateTranslation(parent.transform.position);

            _transformMatrices[0] = _transform;
            AVulkanBufferHandler.UpdateTransformBuffer(ref _transformMatrices, ref _trasnformsBufferMemory);
        }

        internal virtual void EnqueueDrawCommands(ulong[] _offset, int _loopIndex, ref CommandBuffer _commandBuffer)
        {
            if (_render)
            {
                Buffer[] _vertBuffer = new Buffer[] { _vertexBuffer };
                fixed (ulong* _offsetsPtr = _offset)
                fixed (Buffer* _vertBuffersPtr = _vertBuffer)
                {
                    VulkanRenderer._vulkan.CmdBindVertexBuffers(_commandBuffer, 0, 1, _vertBuffersPtr, _offsetsPtr);
                }
                VulkanRenderer._vulkan.CmdBindIndexBuffer(_commandBuffer, _indexBuffer, 0, IndexType.Uint16);
                VulkanRenderer._vulkan.CmdBindDescriptorSets(_commandBuffer, PipelineBindPoint.Graphics, Rasterizer._pipeline._pipelineLayout, 0, 1, _descriptorSets[_loopIndex], 0, null);
                VulkanRenderer._vulkan.CmdDrawIndexed(_commandBuffer, (uint)_mesh._indices.Length, (uint)_instances, 0, 0, 0);
            }
        }
    }
}