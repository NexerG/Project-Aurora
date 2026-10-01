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
        private static readonly Diagnostics.LogChannel Log = Diagnostics.LogChannel.For("Assets");

        internal Silk.NET.Vulkan.Image _textureImage;
        internal ImageView textureImageView;
        internal DeviceMemory _textureBufferMemory;
        internal Sampler textureSampler;
        internal Image<Rgba32> image = null!;

        #region ---- bindless texture table ----
        public const uint MaxTextures = 256;

        private static TextureAsset[] _table = Array.Empty<TextureAsset>();
        public static IReadOnlyList<TextureAsset> Table => Volatile.Read(ref _table);

        public static int TableVersion { get; private set; }

        public uint textureIndex { get; private set; }

        private void RegisterInTable()
        {
            if (_table.Length >= MaxTextures)
                throw new Exception($"Texture table is full ({MaxTextures}). Raise TextureAsset.MaxTextures.");

            textureIndex = (uint)_table.Length;
            TextureAsset[] grown = new TextureAsset[_table.Length + 1];
            Array.Copy(_table, grown, _table.Length);
            grown[^1] = this;
            Volatile.Write(ref _table, grown);
            TableVersion++;
        }
        #endregion

        #region ---- pictures by file ----
        private static readonly Dictionary<string, TextureAsset> _byFile = new Dictionary<string, TextureAsset>(StringComparer.OrdinalIgnoreCase);

        // One mipmapped colour texture per file; null when it cannot load or the table is full.
        public static TextureAsset? ForFile(string path)
        {
            string full = Path.GetFullPath(path);
            if (_byFile.TryGetValue(full, out TextureAsset? cached)) return cached;

            if (_table.Length >= MaxTextures)
            {
                Log.Warn($"texture table is full ({MaxTextures}); '{full}' is not drawn.");
                return null;
            }

            try
            {
                TextureAsset texture = new TextureAsset();
                texture.LoadFile(full, Format.R8G8B8A8Srgb, true);
                _byFile.Add(full, texture);
                return texture;
            }
            catch (Exception e)
            {
                Log.Warn($"picture '{full}' failed to load: {e.Message}");
                return null;
            }
        }
        #endregion

        public TextureAsset() { }

        public override void Load(string name, string source)
        {
            LoadFile(VirtualFileSystem.ResolveFile(source));
        }

        // Uploads an image and claims a bindless table slot, without taking a registry name.
        // Non-colour images (distance fields) must pass a UNORM format.
        public void LoadFile(string path, Format format = Format.R8G8B8A8Srgb, bool mipmaps = false)
        {
            if (!File.Exists(path))
                throw new Exception("Texture not found: " + path);

            LoadImage(Image.Load<Rgba32>(path), format, mipmaps);
        }

        // Uploads a decoded image and claims a table slot; takes ownership of it.
        public void LoadImage(Image<Rgba32> source, Format format = Format.R8G8B8A8Srgb, bool mipmaps = false)
        {
            image = source;
            AVulkanBufferHandler.CreateTextureBuffer(ref _textureImage, ref _textureBufferMemory, ref image, format, ref Renderer.transferQueue, ref Renderer.transferCommandPool, mipmaps);
            AVulkanBufferHandler.CreateImageView(Renderer.vk, ref Renderer.logicalDevice, ref _textureImage, ref textureImageView, format, ImageAspectFlags.ColorBit);
            RegisterInTable();
        }
    }
}
