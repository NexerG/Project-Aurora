using ArctisAurora.Core.Data;
using ArctisAurora.Core.UISystem;
using ArctisAurora.EngineWork.Registry;
using ArctisAurora.EngineWork.ECS.RenderingComponents.Vulkan;
using ArctisAurora.EngineWork.Rendering.Helpers;
using ArctisAurora.EngineWork.Rendering.Modules;
using Assimp;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using SixLabors.ImageSharp.PixelFormats;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.UISystem.Controls;
using ArctisAurora.Core.Registry.Assets;
using static ArctisAurora.Core.UISystem.Controls.VulkanControl;

namespace ArctisAurora.EngineWork.Rendering.MeshSubComponents
{
    internal unsafe class MCUI : MeshComponent
    {
        internal Sampler textureSampler;
        internal SixLabors.ImageSharp.Image<Rgba32> image;
        internal List<VulkanControl> controls;

        // font asset
        internal FontAsset fontAsset;

        // glyph data
        internal Glyph glyph = null!;

        // Two persistent GPU mirrors sized to the UIControls pool's capacity, patched in place:
        // the baked matrices (GpuTransform column) and the per-control data (ControlData column).
        // Both ride along through compaction/resequence because they are pool columns, so these
        // buffers only ever need the same dense range copied across. They are (re)created solely
        // when the pool grows; ordinary edits sub-upload the dirty range.
        internal Silk.NET.Vulkan.Buffer controlDataBuffer;
        private DeviceMemory _controlDataBufferMemory;
        private int _transformCapacity = -1;

        internal MCUI()
        {
            render = false;
            Dictionary<string, AVulkanMesh> dMeshes = AssetRegistries.GetRegistryByValueType<string, AVulkanMesh>(typeof(AVulkanMesh));
            Dictionary<string, FontAsset> dFonts = AssetRegistries.GetRegistryByValueType<string, FontAsset>(typeof(FontAsset));
            mesh = dMeshes.GetValueOrDefault("uidefault");
            fontAsset = dFonts.GetValueOrDefault("default");
            image = fontAsset.textureAsset.image;

            controls = EntityRegistry.GetGroup("Controls").As<VulkanControl>();

            CreateSampler();
        }

        public override void OnStart()
        {
        }

        internal override void LoadCustomMesh(Scene sc)
        {
            base.LoadCustomMesh(sc);
        }

        // Mirror the pool's GpuTransform column to the GPU. dirtyMin/dirtyMax is the dense range the
        // caller's PoolCursor reported; dirtyMax < dirtyMin means nothing to copy.
        //
        // Matrices are already baked by VulkanControl.CommitTransform, so nothing is derived here
        // and the column is read-only to the render thread.
        internal void MakeInstanced(RenderingModule module, int currentFrame, int dirtyMin, int dirtyMax)
        {
            DataPool pool = ((UIModule)module).ControlPool;
            int live = pool.Count;
            instances = live;
            render = live > 0;
            if (live == 0) return;

            GpuTransform[] gpu = pool.Backing<GpuTransform>();
            ControlData[] cd = pool.Backing<ControlData>();

            if (_transformCapacity != pool.Capacity)
            {
                // pool grew (or first build): resize both persistent mirrors to match. Rare, so a
                // full idle+recreate is fine and avoids in-flight aliasing of the old buffers.
                Renderer.vk.DeviceWaitIdle(Renderer.logicalDevice);
                if (transformsBuffer.Handle != 0)
                {
                    Renderer.vk.DestroyBuffer(Renderer.logicalDevice, transformsBuffer, null);
                    Renderer.vk.FreeMemory(Renderer.logicalDevice, _transformsBufferMemory, null);
                }
                if (controlDataBuffer.Handle != 0)
                {
                    Renderer.vk.DestroyBuffer(Renderer.logicalDevice, controlDataBuffer, null);
                    Renderer.vk.FreeMemory(Renderer.logicalDevice, _controlDataBufferMemory, null);
                }
                AVulkanBufferHandler.CreateBuffer(ref gpu, ref Renderer.transferQueue, ref Renderer.transferCommandPool, ref transformsBuffer, ref _transformsBufferMemory, BufferUsageFlags.StorageBufferBit);
                AVulkanBufferHandler.CreateBuffer(ref cd, ref Renderer.transferQueue, ref Renderer.transferCommandPool, ref controlDataBuffer, ref _controlDataBufferMemory, BufferUsageFlags.StorageBufferBit);
                _transformCapacity = pool.Capacity;
                return;   // the fresh buffers already carry the whole columns
            }

            // Clamp to what is live — rows past Count are slack the shader never reads.
            if (dirtyMax >= live) dirtyMax = live - 1;
            if (dirtyMin < 0) dirtyMin = 0;
            if (dirtyMax < dirtyMin) return;

            // One dirty range covers every column, so both mirrors copy the same slice.
            int count = dirtyMax - dirtyMin + 1;
            AVulkanBufferHandler.UpdateBufferRange(gpu, dirtyMin, dirtyMin, count, ref Renderer.transferQueue, ref Renderer.transferCommandPool, ref transformsBuffer);
            AVulkanBufferHandler.UpdateBufferRange(cd, dirtyMin, dirtyMin, count, ref Renderer.transferQueue, ref Renderer.transferCommandPool, ref controlDataBuffer);
        }

        internal override void SingletonMatrix()
        {
            base.SingletonMatrix();

            Matrix4X4<float>[] _mats = transformMatrices.ToArray();
            AVulkanBufferHandler.CreateBuffer(ref _mats, ref Renderer.transferQueue, ref Renderer.transferCommandPool, ref transformsBuffer, ref _transformsBufferMemory, BufferUsageFlags.StorageBufferBit);
        }

        internal override void UpdateMatrices()
        {
            transformMatrices = new List<Matrix4X4<float>>();
            for (int i = 0; i < instances; i++)
            {
                Quaternion<float> q = Quaternion<float>.CreateFromYawPitchRoll(0, 0, 0);
                Matrix4X4<float> _transform = Matrix4X4<float>.Identity;
                _transform *= Matrix4X4.CreateScale(controls[i].transform.scale);
                //_transform *= Matrix4X4.CreateFromQuaternion(q);
                _transform *= Matrix4X4.CreateTranslation(controls[i].transform.position);

                transformMatrices.Add(_transform);
            }
            Matrix4X4<float>[] _mats = transformMatrices.ToArray();
            AVulkanBufferHandler.UpdateBuffer(ref _mats, ref Renderer.transferQueue, ref Renderer.transferCommandPool, ref transformsBuffer, ref _transformsBufferMemory, BufferUsageFlags.StorageBufferBit);
        }

        internal override void EnqueueDrawCommands(ref ulong[] offset, int loopIndex, int instanceID, ref CommandBuffer commandBuffer, ref PipelineLayout pipelineLayout, ref DescriptorSet[][] descriptorSets)
        {
            if (render)
            {
                fixed (ulong* offsetsPtr = offset)
                {
                    Renderer.vk.CmdBindVertexBuffers(commandBuffer, 0, 1, ref mesh.vertexBuffer, offsetsPtr);
                }
                Renderer.vk.CmdBindIndexBuffer(commandBuffer, mesh.indexBuffer, 0, IndexType.Uint32);
                Renderer.vk.CmdBindDescriptorSets(commandBuffer, PipelineBindPoint.Graphics, pipelineLayout, 0, 1, descriptorSets[0][loopIndex], 0, null);
                Renderer.vk.CmdBindDescriptorSets(commandBuffer, PipelineBindPoint.Graphics, pipelineLayout, 1, 1, descriptorSets[1][loopIndex], 0, null);
                Renderer.vk.CmdDrawIndexed(commandBuffer, (uint)mesh.indices.Length, (uint)instances, 0, 0, (uint)instanceID);
            }
        }

        // UI-only: the pool is shared by every window, so each one draws the dense range its own tree
        // occupies. The base signature is shared with MCRaster/MCRaytracing, hence an overload.
        internal void EnqueueDrawCommands(ref ulong[] offset, int loopIndex, int firstInstance, int instanceCount, ref CommandBuffer commandBuffer, ref PipelineLayout pipelineLayout, ref DescriptorSet[] descriptorSets)
        {
            if (!render || instanceCount <= 0) return;

            fixed (ulong* offsetsPtr = offset)
            {
                Renderer.vk.CmdBindVertexBuffers(commandBuffer, 0, 1, ref mesh.vertexBuffer, offsetsPtr);
            }
            Renderer.vk.CmdBindIndexBuffer(commandBuffer, mesh.indexBuffer, 0, IndexType.Uint32);
            Renderer.vk.CmdBindDescriptorSets(commandBuffer, PipelineBindPoint.Graphics, pipelineLayout, 0, 1, descriptorSets[0], 0, null);
            Renderer.vk.CmdBindDescriptorSets(commandBuffer, PipelineBindPoint.Graphics, pipelineLayout, 1, 1, descriptorSets[1], 0, null);
            Renderer.vk.CmdDrawIndexed(commandBuffer, (uint)mesh.indices.Length, (uint)instanceCount, 0, 0, (uint)firstInstance);
        }

        internal override void EnqueueDrawCommands(ref ulong[] offset, int loopIndex, int instanceID, ref CommandBuffer commandBuffer, ref PipelineLayout pipelineLayout, ref DescriptorSet[] descriptorSets)
        {
            if (render)
            {
                fixed (ulong* offsetsPtr = offset)
                {
                    Renderer.vk.CmdBindVertexBuffers(commandBuffer, 0, 1, ref mesh.vertexBuffer, offsetsPtr);
                }
                Renderer.vk.CmdBindIndexBuffer(commandBuffer, mesh.indexBuffer, 0, IndexType.Uint32);
                Renderer.vk.CmdBindDescriptorSets(commandBuffer, PipelineBindPoint.Graphics, pipelineLayout, 0, 1, descriptorSets[0], 0, null);
                Renderer.vk.CmdBindDescriptorSets(commandBuffer, PipelineBindPoint.Graphics, pipelineLayout, 1, 1, descriptorSets[1], 0, null);
                Renderer.vk.CmdDrawIndexed(commandBuffer, (uint)mesh.indices.Length, (uint)instances, 0, 0, (uint)instanceID);
            }
        }

        private void CreateSampler()
        {
            Renderer.vk.GetPhysicalDeviceProperties(Renderer.gpu, out PhysicalDeviceProperties _properties);
            SamplerCreateInfo _createInfo = new SamplerCreateInfo()
            {
                SType = StructureType.SamplerCreateInfo,
                MagFilter = Filter.Linear,
                MinFilter = Filter.Linear,
                AddressModeU = Silk.NET.Vulkan.SamplerAddressMode.Repeat,
                AddressModeV = Silk.NET.Vulkan.SamplerAddressMode.Repeat,
                AddressModeW = Silk.NET.Vulkan.SamplerAddressMode.Repeat,
                AnisotropyEnable = true,
                MaxAnisotropy = _properties.Limits.MaxSamplerAnisotropy,
                BorderColor = BorderColor.IntOpaqueBlack,
                UnnormalizedCoordinates = false,
                CompareEnable = false,
                CompareOp = CompareOp.Always,
                MipmapMode = Silk.NET.Vulkan.SamplerMipmapMode.Linear
            };

            fixed (Sampler* _textureSamplerPtr = &textureSampler)
            {
                Result r = Renderer.vk.CreateSampler(Renderer.logicalDevice, ref _createInfo, null, _textureSamplerPtr);
                if (r != Result.Success)
                {
                    throw new Exception("Failed to create a texture sampler with error: " + r);
                }
            }
        }

        private void CreateCircleSDF(int width, int height, float radius, float edgeSoftness)
        {
            image = new SixLabors.ImageSharp.Image<Rgba32>(width, height);
            float centerX = width / 2;
            float centerY = height / 2;
            float maxDist = radius * edgeSoftness;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float dx = x - centerX;
                    float dy = y - centerY;
                    float distance = MathF.Sqrt(dx * dx + dy * dy);

                    float sdf = (distance - radius) / edgeSoftness; // Normalize edge
                    float alpha = Math.Clamp(0.5f - sdf * 0.5f, 0f, 1f); // Map to [0,1]

                    byte value = (byte)(alpha * 255);
                    image[x, y] = new Rgba32(value);
                }
            }
        }

        private void CreateFillSDF(int width, int height, float radius, float edgeSoftness)
        {
            image = new SixLabors.ImageSharp.Image<Rgba32>(width, height);
            float centerX = width / 2;
            float centerY = height / 2;
            float maxDist = radius * edgeSoftness;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float dx = x - centerX;
                    float dy = y - centerY;
                    float distance = MathF.Sqrt(dx * dx + dy * dy);

                    float sdf = (distance - radius) / edgeSoftness;
                    float alpha = Math.Clamp(0.5f - sdf * 0.5f, 0f, 1f);

                    byte value = (byte)(alpha * 255);
                    image[x, y] = new Rgba32(255);
                }
            }
        }
    }
}