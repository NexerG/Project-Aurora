using ArctisAurora.EngineWork.Rendering.UI;
using ArctisAurora.EngineWork.Serialization;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Image = SixLabors.ImageSharp.Image;

namespace ArctisAurora.EngineWork.AssetRegistry
{
    internal class FontAsset : Asset
    {
        public AtlasMetaData atlasMetaData;
        public TextureAsset textureAsset;

        public FontAsset() { }
        public FontAsset(string name)
        {
            AssetRegistries.fonts.Add(name, this);
        }

        public Glyph GetGlyph(char c)
        {
            for (int i = 0; i < atlasMetaData.glyphCount; i++)
            {
                if (atlasMetaData.chars[i] == c)
                {
                    return atlasMetaData.glyphs[i];
                }
            }
            return null;
        }

        public override void LoadAsset(Asset asset, string name, string path)
        {
            if (AssetRegistries.fonts.ContainsKey(name))
            {
                asset = AssetRegistries.fonts[name];
                return;
            }
            atlasMetaData = new AtlasMetaData();
            atlasMetaData.Deserialize(name);

            string imagePath = Paths.FONTS + "\\" + name + "\\" + name + "_atlas.png";
            if (System.IO.File.Exists(imagePath))
            {
                textureAsset.LoadAsset(asset, name, path);
                AssetRegistries.fonts[name] = this;
                return;
            }

            throw new Exception(name);
        }

        public override void LoadDefault()
        {
            atlasMetaData = new AtlasMetaData();
            atlasMetaData.Deserialize("arial");

            string imagePath = Paths.FONTS + "\\arial\\" + "arial_atlas.png";
            textureAsset = new TextureAsset("uidefault");
            textureAsset.LoadAsset(this, "arial", imagePath);
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
