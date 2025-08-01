using Silk.NET.Vulkan;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static ArctisAurora.EngineWork.AssetRegistry.AssetRegistries;

namespace ArctisAurora.EngineWork.AssetRegistry
{
    public class TextureAsset : Asset
    {
        internal Silk.NET.Vulkan.Image _textureImage;
        internal ImageView _textureImageView;
        internal DeviceMemory _textureBufferMemory;
        internal Sampler textureSampler;
        internal SixLabors.ImageSharp.Image<Rgba32> image;


        public override Asset LoadAsset(string name)
        {
            throw new NotImplementedException();
        }

        public override Asset LoadDefault()
        {
            throw new NotImplementedException();
        }
    }
}
