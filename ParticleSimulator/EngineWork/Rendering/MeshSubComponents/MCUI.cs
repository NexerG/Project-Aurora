using ArctisAurora.EngineWork.AssetRegistry;
using ArctisAurora.EngineWork.ECS.RenderingComponents.Vulkan;
using ArctisAurora.EngineWork.Rendering.Helpers;
using ArctisAurora.EngineWork.Rendering.UI;
using Assimp;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using SixLabors.ImageSharp.PixelFormats;

namespace ArctisAurora.EngineWork.Rendering.MeshSubComponents
{
    internal unsafe class MCUI : MeshComponent
    {
        internal Sampler textureSampler;
        internal SixLabors.ImageSharp.Image<Rgba32> image;

        // font asset
        internal FontAsset fontAsset;

        // glyph data
        internal Glyph glyph;

        internal MCUI()
        {
            render = false;
            mesh = AssetRegistries.meshes.GetValueOrDefault("uidefault");
            fontAsset = AssetRegistries.fonts.GetValueOrDefault("default");
            image = fontAsset.textureAsset.image;
            CreateSampler();
        }

        public override void OnStart()
        {
        }

        internal override void LoadCustomMesh(Scene sc)
        {
            base.LoadCustomMesh(sc);
        }

        internal override void MakeInstanced()
        {
            instances = EntityManager.controls.Count;
            if (instances > 0)
            {
                if (transformMatrices.Count != instances)
                {
                    render = true;
                    transformMatrices = new List<Matrix4X4<float>>();
                    for (int i = 0; i < instances; i++)
                    {
                        Quaternion<float> q = Quaternion<float>.CreateFromYawPitchRoll(0, 0, 0);
                        Matrix4X4<float> _transform = Matrix4X4<float>.Identity;
                        _transform *= Matrix4X4.CreateScale(EntityManager.controls[i].transform.scale);
                        //_transform *= Matrix4X4.CreateFromQuaternion(q);
                        _transform *= Matrix4X4.CreateTranslation(EntityManager.controls[i].transform.position);

                        transformMatrices.Add(_transform);
                    }
                    if (transformsBuffer.Handle != 0)
                    {
                        Renderer.vk.DestroyBuffer(Renderer.logicalDevice, transformsBuffer, null);
                    }
                    Matrix4X4<float>[] _mats = transformMatrices.ToArray();
                    AVulkanBufferHandler.CreateBuffer(ref _mats, ref transformsBuffer, ref _transformsBufferMemory, BufferUsageFlags.StorageBufferBit);
                }
                else
                {
                    for (int i = 0; i < instances; i++)
                    {
                        Quaternion<float> q = Quaternion<float>.CreateFromYawPitchRoll(0, 0, 0);
                        Matrix4X4<float> _transform = Matrix4X4<float>.Identity;
                        _transform *= Matrix4X4.CreateScale(EntityManager.controls[i].transform.scale);
                        //_transform *= Matrix4X4.CreateFromQuaternion(q);
                        _transform *= Matrix4X4.CreateTranslation(EntityManager.controls[i].transform.position);

                        transformMatrices[i] = _transform;
                    }
                    Matrix4X4<float>[] _mats = transformMatrices.ToArray();
                    AVulkanBufferHandler.UpdateBuffer(ref _mats, ref transformsBuffer, ref _transformsBufferMemory, BufferUsageFlags.StorageBufferBit);
                }
            }
        }

        internal override void SingletonMatrix()
        {
            base.SingletonMatrix();

            Matrix4X4<float>[] _mats = transformMatrices.ToArray();
            AVulkanBufferHandler.CreateBuffer(ref _mats, ref transformsBuffer, ref _transformsBufferMemory, BufferUsageFlags.StorageBufferBit);
        }

        internal override void UpdateMatrices()
        {
            transformMatrices = new List<Matrix4X4<float>>();
            for (int i = 0; i < instances; i++)
            {
                Quaternion<float> q = Quaternion<float>.CreateFromYawPitchRoll(0, 0, 0);
                Matrix4X4<float> _transform = Matrix4X4<float>.Identity;
                _transform *= Matrix4X4.CreateScale(EntityManager.controls[i].transform.scale);
                //_transform *= Matrix4X4.CreateFromQuaternion(q);
                _transform *= Matrix4X4.CreateTranslation(EntityManager.controls[i].transform.position);

                transformMatrices.Add(_transform);
            }
            Matrix4X4<float>[] _mats = transformMatrices.ToArray();
            AVulkanBufferHandler.UpdateBuffer(ref _mats, ref transformsBuffer, ref _transformsBufferMemory, BufferUsageFlags.StorageBufferBit);
        }

        internal override void EnqueueDrawCommands(ref ulong[] offset, int loopIndex, int instanceID, ref CommandBuffer commandBuffer, ref PipelineLayout pipelineLayout, ref DescriptorSet[][] descriptorSets)
        {
            if (render)
            {
                fixed(ulong* offsetsPtr = offset)
                {
                    Renderer.vk.CmdBindVertexBuffers(commandBuffer, 0, 1, ref mesh.vertexBuffer, offsetsPtr);
                }
                Renderer.vk.CmdBindIndexBuffer(commandBuffer, mesh.indexBuffer, 0, IndexType.Uint32);
                Renderer.vk.CmdBindDescriptorSets(commandBuffer, PipelineBindPoint.Graphics, pipelineLayout, 0, 1, descriptorSets[0][loopIndex], 0, null);
                Renderer.vk.CmdBindDescriptorSets(commandBuffer, PipelineBindPoint.Graphics, pipelineLayout, 1, 1, descriptorSets[1][loopIndex], 0, null);
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
                AddressModeU = SamplerAddressMode.Repeat,
                AddressModeV = SamplerAddressMode.Repeat,
                AddressModeW = SamplerAddressMode.Repeat,
                AnisotropyEnable = true,
                MaxAnisotropy = _properties.Limits.MaxSamplerAnisotropy,
                BorderColor = BorderColor.IntOpaqueBlack,
                UnnormalizedCoordinates = false,
                CompareEnable = false,
                CompareOp = CompareOp.Always,
                MipmapMode = SamplerMipmapMode.Linear
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