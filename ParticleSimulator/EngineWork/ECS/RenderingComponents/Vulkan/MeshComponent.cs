using ArctisAurora.EngineWork.ComponentBehaviour;
using ArctisAurora.EngineWork.Rendering;
using ArctisAurora.EngineWork.Rendering.Helpers;
using Assimp;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace ArctisAurora.EngineWork.ECS.RenderingComponents.Vulkan
{
    public unsafe class MeshComponent : EntityComponent
    {
        internal bool render = true;
        internal AVulkanMesh mesh;
        //descriptor set
        internal DescriptorSet[] _descriptorSets;
        internal BufferUsageFlags _aditionalUsageFlags = BufferUsageFlags.None;

        internal Buffer transformsBuffer;
        internal DeviceMemory _transformsBufferMemory;

        internal int instances = 1;
        internal List<Matrix4X4<float>> transformMatrices = new List<Matrix4X4<float>>();

        public MeshComponent()
        {
            //_mesh = new AVulkanMesh();
        }

        public override void OnStart()
        {
            SingletonMatrix();
            EntityManager.AddEntityToRender(parent);
        }

        internal virtual void LoadCustomMesh(Scene sc)
        {
            //VulkanRenderer._vulkan.DestroyBuffer(VulkanRenderer._logicalDevice, _vertexBuffer, null);
            //VulkanRenderer._vulkan.DestroyBuffer(VulkanRenderer._logicalDevice, _indexBuffer, null);
            //VulkanRenderer._vulkan.FreeMemory(VulkanRenderer._logicalDevice, _indexBufferMemory, null);
            //VulkanRenderer._vulkan.FreeMemory(VulkanRenderer._logicalDevice, _vertexBufferMemory, null);
            mesh.LoadCustomMesh(sc);
            //AVulkanBufferHandler.CreateBuffer(ref _mesh._vertices, ref _vertexBuffer, ref _vertexBufferMemory, AVulkanBufferHandler.vertexBufferFlags | _aditionalUsageFlags);
            //AVulkanBufferHandler.CreateBuffer(ref _mesh._indices, ref _indexBuffer, ref _indexBufferMemory, AVulkanBufferHandler.indexBufferFlags | _aditionalUsageFlags);

            //Renderer.renderer.RecreateCommandBuffers();
        }

        internal virtual void FreeDescriptorSets() { }

        internal virtual void ReinstantiateDesriptorSets() { }

        internal virtual void MakeInstanced(ref List<Matrix4X4<float>> _matrices) { }
        internal virtual void MakeInstanced() { }

        internal virtual void SingletonMatrix()
        {
            Quaternion<float> q = Quaternion<float>.CreateFromYawPitchRoll(0,0,0);
            Matrix4X4<float> _transform = Matrix4X4<float>.Identity;
            _transform *= Matrix4X4.CreateScale(parent.transform.scale);
            //_transform *= Matrix4X4.CreateFromQuaternion(q);
            _transform *= Matrix4X4.CreateTranslation(parent.transform.position);

            transformMatrices.Add(_transform);
        }

        internal virtual void CreateDescriptorSet() { }

        internal virtual void UpdateMatrices()
        {
            Quaternion<float> q = Quaternion<float>.CreateFromYawPitchRoll(0, 0, 0);
            Matrix4X4<float> _transform = Matrix4X4<float>.Identity;
            _transform *= Matrix4X4.CreateScale(parent.transform.scale);
            _transform *= Matrix4X4.CreateFromQuaternion(q);
            _transform *= Matrix4X4.CreateTranslation(parent.transform.position);

            transformMatrices[0] = _transform;
            Matrix4X4<float>[] _mats = transformMatrices.ToArray();
            AVulkanBufferHandler.UpdateBuffer(ref _mats, ref transformsBuffer, ref _transformsBufferMemory, _aditionalUsageFlags);
        }

        internal virtual void EnqueueDrawCommands(ref ulong[] _offset, int _loopIndex, ref CommandBuffer _commandBuffer)
        {
            //    if (_render)
            //    {
            //        fixed (ulong* _offsetsPtr = _offset)
            //        {
            //            VulkanRenderer._vulkan.CmdBindVertexBuffers(_commandBuffer, 0, 1, ref _mesh._vertexBuffer, _offsetsPtr);
            //        }
            //        VulkanRenderer._vulkan.CmdBindIndexBuffer(_commandBuffer, _mesh._indexBuffer, 0, IndexType.Uint32);
            //        VulkanRenderer._vulkan.CmdBindDescriptorSets(_commandBuffer, PipelineBindPoint.Graphics, VulkanRenderer._pipeline._pipelineLayout, 0, 1, ref VulkanRenderer._descriptorSets[_loopIndex], 0, null);
            //        VulkanRenderer._vulkan.CmdDrawIndexed(_commandBuffer, (uint)_mesh._indices.Length, (uint)_instances, 0, 0, 0);
            //        _offset[0] += (ulong)(sizeof(Vertex) * _loopIndex);
            //    }
        }

        internal virtual void EnqueueDrawCommands(ref ulong[] _offset, int _loopIndex, int instanceID, ref CommandBuffer _commandBuffer)
        {
            //    if (_render)
            //    {
            //        fixed (ulong* _offsetsPtr = _offset)
            //        {
            //            VulkanRenderer._vulkan.CmdBindVertexBuffers(_commandBuffer, 0, 1, ref _mesh._vertexBuffer, _offsetsPtr);
            //        }
            //        VulkanRenderer._vulkan.CmdBindIndexBuffer(_commandBuffer, _mesh._indexBuffer, 0, IndexType.Uint32);
            //        VulkanRenderer._vulkan.CmdBindDescriptorSets(_commandBuffer, PipelineBindPoint.Graphics, VulkanRenderer._pipeline._pipelineLayout, 0, 1, ref VulkanRenderer._descriptorSets[_loopIndex], 0, null);
            //        VulkanRenderer._vulkan.CmdDrawIndexed(_commandBuffer, (uint)_mesh._indices.Length, (uint)_instances, 0, 0, (uint)instanceID);
            //        _offset[0] += (ulong)(sizeof(Vertex) * _loopIndex);
            //    }
        }

        internal virtual void EnqueueDrawCommands(ref ulong[] _offset, int _loopIndex, int instanceID, ref CommandBuffer _commandBuffer, ref PipelineLayout pipelineLayout, ref DescriptorSet descriptorSet)
        {
            if (render)
            {
                fixed (ulong* _offsetsPtr = _offset)
                {
                    Renderer.vk.CmdBindVertexBuffers(_commandBuffer, 0, 1, ref mesh.vertexBuffer, _offsetsPtr);
                }
                Renderer.vk.CmdBindIndexBuffer(_commandBuffer, mesh.indexBuffer, 0, IndexType.Uint32);
                Renderer.vk.CmdBindDescriptorSets(_commandBuffer, PipelineBindPoint.Graphics, pipelineLayout, 0, 1, descriptorSet, 0, null);
                Renderer.vk.CmdDrawIndexed(_commandBuffer, (uint)mesh.indices.Length, (uint)instances, 0, 0, (uint)instanceID);
                _offset[0] += (ulong)(sizeof(Vertex) * _loopIndex);
            }
        }

    }
}