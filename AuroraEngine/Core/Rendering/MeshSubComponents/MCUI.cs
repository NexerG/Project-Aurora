using ArctisAurora.Core.Data;
using ArctisAurora.Core.UISystem;
using ArctisAurora.EngineWork.Registry;
using ArctisAurora.EngineWork.ECS.RenderingComponents.Vulkan;
using ArctisAurora.EngineWork.Rendering.Helpers;
using ArctisAurora.EngineWork.Rendering.Modules;
using Assimp;
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

        // font asset
        internal FontAsset fontAsset;

        // glyph data
        internal Glyph glyph = null!;

        // Two persistent GPU mirrors sized to the UIControls pool's capacity, one set per swapchain
        // image, patched in place: the baked matrices (GpuTransform column) and the per-control data
        // (ControlData column). Both ride along through compaction/resequence because they are pool
        // columns, so these buffers only ever need the same dense range copied across. They are
        // (re)created solely when the pool grows; ordinary edits write the dirty range straight
        // through the mapped pointer.
        internal Silk.NET.Vulkan.Buffer[] transformsBuffers = null!;
        private DeviceMemory[] _transformsBufferMemories = null!;
        private nint[] _transformsMapped = null!;
        internal Silk.NET.Vulkan.Buffer[] controlDataBuffers = null!;
        private DeviceMemory[] _controlDataBufferMemories = null!;
        private nint[] _controlDataMapped = null!;
        private int _transformCapacity = -1;

        // The gradient table, shared by every window and written once — definitions are authored in
        // XML and parsed at bootstrap, so nothing edits them after this.
        internal Silk.NET.Vulkan.Buffer gradientBuffer;
        private DeviceMemory _gradientBufferMemory;

        internal MCUI()
        {
            render = false;
            Dictionary<string, AVulkanMesh> dMeshes = AssetRegistries.GetRegistryByValueType<string, AVulkanMesh>(typeof(AVulkanMesh));
            Dictionary<string, FontAsset> dFonts = AssetRegistries.GetRegistryByValueType<string, FontAsset>(typeof(FontAsset));
            mesh = dMeshes.GetValueOrDefault("uidefault");
            fontAsset = dFonts.GetValueOrDefault("default");
            image = fontAsset.textureAsset.image;

            CreateSampler();
            CreateGradientTable();
        }

        // Slot 0 is always present, so the buffer is never zero-sized even with no gradients authored.
        private void CreateGradientTable()
        {
            GpuGradient[] gradients = Gradients.Table;
            AVulkanBufferHandler.CreateBuffer(ref gradients, ref Renderer.transferQueue, ref Renderer.transferCommandPool, ref gradientBuffer, ref _gradientBufferMemory, BufferUsageFlags.StorageBufferBit);
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
                // pool grew (or first build): resize every image's mirrors to match. Rare, so a
                // full idle+recreate is fine and avoids in-flight aliasing of the old buffers.
                Renderer.vk.DeviceWaitIdle(Renderer.logicalDevice);
                DestroyMirrors();

                int images = (int)module.window.imageCount;
                transformsBuffers = new Silk.NET.Vulkan.Buffer[images];
                _transformsBufferMemories = new DeviceMemory[images];
                _transformsMapped = new nint[images];
                controlDataBuffers = new Silk.NET.Vulkan.Buffer[images];
                _controlDataBufferMemories = new DeviceMemory[images];
                _controlDataMapped = new nint[images];

                ulong transformSize = (ulong)(sizeof(GpuTransform) * gpu.Length);
                ulong controlSize = (ulong)(sizeof(ControlData) * cd.Length);
                for (int i = 0; i < images; i++)
                {
                    AVulkanBufferHandler.CreateMappedBuffer(transformSize, ref transformsBuffers[i], ref _transformsBufferMemories[i], out _transformsMapped[i], AVulkanBufferHandler.storageBufferFlags);
                    AVulkanBufferHandler.CreateMappedBuffer(controlSize, ref controlDataBuffers[i], ref _controlDataBufferMemories[i], out _controlDataMapped[i], AVulkanBufferHandler.storageBufferFlags);
                    AVulkanBufferHandler.WriteMappedRange(_transformsMapped[i], gpu, 0, gpu.Length);
                    AVulkanBufferHandler.WriteMappedRange(_controlDataMapped[i], cd, 0, cd.Length);
                }
                _transformCapacity = pool.Capacity;
                return;   // the fresh buffers already carry the whole columns
            }

            // Clamp to what is live — rows past Count are slack the shader never reads.
            if (dirtyMax >= live) dirtyMax = live - 1;
            if (dirtyMin < 0) dirtyMin = 0;
            if (dirtyMax < dirtyMin) return;

            // One dirty range covers every column, so both mirrors copy the same slice.
            int count = dirtyMax - dirtyMin + 1;
            AVulkanBufferHandler.WriteMappedRange(_transformsMapped[currentFrame], gpu, dirtyMin, count);
            AVulkanBufferHandler.WriteMappedRange(_controlDataMapped[currentFrame], cd, dirtyMin, count);
        }

        private void DestroyMirrors()
        {
            if (transformsBuffers == null) return;

            for (int i = 0; i < transformsBuffers.Length; i++)
            {
                Renderer.vk.UnmapMemory(Renderer.logicalDevice, _transformsBufferMemories[i]);
                Renderer.vk.DestroyBuffer(Renderer.logicalDevice, transformsBuffers[i], null);
                Renderer.vk.FreeMemory(Renderer.logicalDevice, _transformsBufferMemories[i], null);

                Renderer.vk.UnmapMemory(Renderer.logicalDevice, _controlDataBufferMemories[i]);
                Renderer.vk.DestroyBuffer(Renderer.logicalDevice, controlDataBuffers[i], null);
                Renderer.vk.FreeMemory(Renderer.logicalDevice, _controlDataBufferMemories[i], null);
            }
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
            Renderer.vk.CmdBindDescriptorSets(commandBuffer, PipelineBindPoint.Graphics, pipelineLayout, 1, 1, descriptorSets[0], 0, null);
            Renderer.vk.CmdBindDescriptorSets(commandBuffer, PipelineBindPoint.Graphics, pipelineLayout, 2, 1, descriptorSets[1], 0, null);
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