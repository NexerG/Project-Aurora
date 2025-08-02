using ArctisAurora.CustomEntities;
using ArctisAurora.EngineWork.AssetRegistry;
using ArctisAurora.EngineWork.ECS.RenderingComponents.Vulkan;
using ArctisAurora.EngineWork.Renderer.Helpers;
using ArctisAurora.EngineWork.Renderer.UI;
using Assimp;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using SixLabors.ImageSharp.PixelFormats;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace ArctisAurora.EngineWork.Renderer.MeshSubComponents
{
    internal unsafe class MCUI : MeshComponent
    {
        //texture image
        //internal Silk.NET.Vulkan.Image _textureImage;
        //internal ImageView _textureImageView;
        //internal DeviceMemory _textureBufferMemory;
        internal Sampler textureSampler;
        internal SixLabors.ImageSharp.Image<Rgba32> image;

        // font asset
        internal FontAsset fontAsset;

        // glyph data
        internal Glyph glyph;
        internal Vector2D<float>[] glyphUVs;
        internal Buffer uvBuffer;
        internal DeviceMemory uvBufferMemory;

        internal MCUI()
        {
            _mesh = AssetRegistries.meshes.GetValueOrDefault("default");
            fontAsset = AssetRegistries.fonts.GetValueOrDefault("default");
        }

        public override void OnStart()
        {
            //_mesh = AssetRegistries.meshes.GetValueOrDefault("default");
            //_mesh.BufferMesh();

            image = fontAsset.image.image;
            char glyphChar = ((GlyphEntity)parent).character;
            int index;
            (glyph, index) = fontAsset.atlasMetaData.GetGlyphAndIndex(glyphChar);

            float k = MathF.Ceiling(MathF.Sqrt(fontAsset.atlasMetaData.glyphCount));
            float glyphAtlasSize = 1f / k;
            float xOffset = (index % k) * glyphAtlasSize;
            float yOffset = MathF.Floor(index / k) * glyphAtlasSize;

            glyphUVs = new Vector2D<float>[4];
            glyphUVs[0] = new Vector2D<float>(xOffset, yOffset);
            glyphUVs[1] = new Vector2D<float>(xOffset + glyphAtlasSize, yOffset);
            glyphUVs[2] = new Vector2D<float>(xOffset + glyphAtlasSize, yOffset + glyphAtlasSize);
            glyphUVs[3] = new Vector2D<float>(xOffset, yOffset + glyphAtlasSize);

            AVulkanBufferHandler.CreateBuffer(ref glyphUVs, ref uvBuffer, ref uvBufferMemory, BufferUsageFlags.StorageBufferBit);

            CreateSampler();
            
            base.OnStart();
            CreateDescriptorSet();
        }

        internal override void LoadCustomMesh(Scene sc)
        {
            base.LoadCustomMesh(sc);
        }

        internal override void MakeInstanced(ref List<Matrix4X4<float>> _matrices)
        {
            base.MakeInstanced(ref _matrices);
            _instances = _matrices.Count;
            _transformMatrices = _matrices;

            Matrix4X4<float>[] _mats = _matrices.ToArray();
            AVulkanBufferHandler.CreateBuffer(ref _mats, ref _transformsBuffer, ref _transformsBufferMemory, BufferUsageFlags.StorageBufferBit);
            //VulkanRenderer._vulkan.FreeDescriptorSets(VulkanRenderer._logicalDevice, Rasterizer._descriptorPool, (uint)_descriptorSets.Length, _descriptorSets);
            //VulkanRenderer._vulkan.FreeDescriptorSets(VulkanRenderer._logicalDevice, Rasterizer._descriptorPoolShadow, (uint)_descriptorSetsShadow.Length, _descriptorSetsShadow);
            //CreateDescriptorSet();
            VulkanRenderer._rendererInstance.RecreateCommandBuffers();
        }

        internal override void SingletonMatrix()
        {
            if(glyph != null)
            {
                parent.transform.SetWorldScale(new Vector3D<float>(1, glyph.glyphHeight, glyph.glyphWidth));
            }
            base.SingletonMatrix();

            Matrix4X4<float>[] _mats = _transformMatrices.ToArray();
            AVulkanBufferHandler.CreateBuffer(ref _mats, ref _transformsBuffer, ref _transformsBufferMemory, BufferUsageFlags.StorageBufferBit);
        }

        internal override void UpdateMatrices()
        {
            base.UpdateMatrices();
        }

        private void CreateSampler()
        {
            VulkanRenderer._vulkan.GetPhysicalDeviceProperties(VulkanRenderer._gpu, out PhysicalDeviceProperties _properties);
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
                Result r = VulkanRenderer._vulkan.CreateSampler(VulkanRenderer._logicalDevice, ref _createInfo, null, _textureSamplerPtr);
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