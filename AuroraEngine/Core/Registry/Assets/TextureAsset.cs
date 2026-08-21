using ArctisAurora.Core.Filing.Serialization;
using ArctisAurora.Core.Registry;
using ArctisAurora.EngineWork.Registry;
using ArctisAurora.EngineWork.Rendering;
using ArctisAurora.EngineWork.Rendering.Helpers;
using Silk.NET.Vulkan;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Image = SixLabors.ImageSharp.Image;

namespace ArctisAurora.Core.Registry.Assets
{
    [A_XSDType("TextureAsset", "AssetRegistry")]
    public class TextureAsset : AbstractAsset
    {
        internal Silk.NET.Vulkan.Image _textureImage;
        internal ImageView textureImageView;
        internal DeviceMemory _textureBufferMemory;
        internal Sampler textureSampler;
        internal Image<Rgba32> image = null!;

        #region ---- bindless texture table ----
        public const uint MaxTextures = 256;

        private static readonly List<TextureAsset> _table = new List<TextureAsset>();
        public static IReadOnlyList<TextureAsset> Table => _table;

        public static int TableVersion { get; private set; }

        public uint textureIndex { get; private set; }

        private void RegisterInTable()
        {
            if (_table.Count >= MaxTextures)
                throw new Exception($"Texture table is full ({MaxTextures}). Raise TextureAsset.MaxTextures.");

            textureIndex = (uint)_table.Count;
            _table.Add(this);
            TableVersion++;
        }
        #endregion

        public TextureAsset() { }

        public override void Load(string name, string source)
        {
            LoadFile(VirtualFileSystem.ResolveFile(source));
        }

        // Uploads an image and claims a bindless table slot, without taking a registry name.
        // Non-colour images (distance fields) must pass a UNORM format.
        internal void LoadFile(string path, Format format = Format.R8G8B8A8Srgb)
        {
            if (!File.Exists(path))
                throw new Exception("Texture not found: " + path);

            image = Image.Load<Rgba32>(path);
            AVulkanBufferHandler.CreateTextureBuffer(ref _textureImage, ref _textureBufferMemory, ref image, format, ref Renderer.transferQueue, ref Renderer.transferCommandPool);
            AVulkanBufferHandler.CreateImageView(Renderer.vk, ref Renderer.logicalDevice, ref _textureImage, ref textureImageView, format, ImageAspectFlags.ColorBit);
            RegisterInTable();
        }
    }
}
