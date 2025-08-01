using ArctisAurora.EngineWork.Renderer.UI;
using ArctisAurora.EngineWork.Serialization;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Image = SixLabors.ImageSharp.Image;

namespace ArctisAurora.EngineWork.AssetRegistry
{
    internal class FontAsset : Asset
    {
        public AtlasMetaData atlasMetaData;
        public Image<Rgba32> image;

        public override Asset LoadAsset(string name)
        {
            if (AssetRegistries.fonts.ContainsKey(name))
            {
                return AssetRegistries.fonts[name];
            }
            atlasMetaData = new AtlasMetaData();
            atlasMetaData.Deserialize(name);

            string imagePath = Paths.FONTS + "\\" + name + "\\" + name + "_atlas.png";
            if (System.IO.File.Exists(imagePath))
            {
                image = Image.Load<Rgba32>(imagePath);
                AssetRegistries.fonts[name] = this;
                return this;
            }

            throw new Exception(name);
        }

        public override Asset LoadDefault()
        {
            FontAsset fontAsset = new FontAsset();

            fontAsset.atlasMetaData = new AtlasMetaData();
            fontAsset.atlasMetaData.Deserialize("arial");

            string imagePath = Paths.FONTS + "\\arial\\" + "arial_atlas.png";
            fontAsset.image = Image.Load<Rgba32>(imagePath);

            return fontAsset;
        }

        /*public FontAsset LoadFont(string name)
        {
            if (AssetRegistries.fonts.ContainsKey(name))
            {
                return AssetRegistries.fonts[name];
            }
            atlasMetaData = new AtlasMetaData();
            atlasMetaData.Deserialize(name);

            string imagePath = Paths.FONTS + "\\" + name + "\\" + name + "_atlas.png";
            if (System.IO.File.Exists(imagePath))
            {
                image = Image.Load<Rgba32>(imagePath);
                AssetRegistries.fonts[name] = this;
                return this;
            }

            throw new Exception(name);
        }

        public static FontAsset LoadDefault()
        {
            FontAsset fontAsset = new FontAsset();

            fontAsset.atlasMetaData = new AtlasMetaData();
            fontAsset.atlasMetaData.Deserialize("arial");

            string imagePath = Paths.FONTS + "\\arial\\" + "arial_atlas.png";
            fontAsset.image = Image.Load<Rgba32>(imagePath);

            return fontAsset;
        }*/
    }
}
