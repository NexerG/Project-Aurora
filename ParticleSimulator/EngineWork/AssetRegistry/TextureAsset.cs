using ArctisAurora.EngineWork.Rendering;
using ArctisAurora.EngineWork.Rendering.Helpers;
using Silk.NET.Vulkan;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Image = SixLabors.ImageSharp.Image;

namespace ArctisAurora.EngineWork.AssetRegistry
{
    public class TextureAsset : Asset
    {
        internal Silk.NET.Vulkan.Image _textureImage;
        internal ImageView textureImageView;
        internal DeviceMemory _textureBufferMemory;
        internal Sampler textureSampler;
        internal Image<Rgba32> image;


        public TextureAsset(string name)
        {
            AssetRegistries.textures.Add(name, this);
        }

        public override void LoadAsset(Asset asset, string name, string path)
        {
            if (AssetRegistries.textures.ContainsKey(name))
            {
                asset = AssetRegistries.textures[name];
                return;
            }
            else if (System.IO.File.Exists(path))
            { 
                image = Image.Load<Rgba32>(path);

                AVulkanBufferHandler.CreateTextureBuffer(ref _textureImage, ref _textureBufferMemory, ref image, Format.R8G8B8A8Srgb);
                AVulkanBufferHandler.CreateImageView(ref Renderer.vk, ref Renderer.logicalDevice, ref _textureImage, ref textureImageView, Format.R8G8B8A8Srgb, ImageAspectFlags.ColorBit);

                return;
            }

            throw new NotImplementedException();
        }

        public override void LoadDefault()
        {
            throw new NotImplementedException();
        }
    }
}
