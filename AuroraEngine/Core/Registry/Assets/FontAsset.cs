using ArctisAurora.Core.Filing.Serialization;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.UISystem;
using ArctisAurora.EngineWork.Registry;
using Silk.NET.Vulkan;

namespace ArctisAurora.Core.Registry.Assets
{
    [A_XSDType("FontAsset", "AssetRegistry")]
    public class FontAsset : AbstractAsset
    {
        public AtlasMetaData atlasMetaData = null!;
        public TextureAsset textureAsset = null!;

        public FontAsset() { }

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

        public override void Load(string name, string source)
        {
            string fontName = source.Substring(source.LastIndexOf('/') + 1);

            atlasMetaData = new AtlasMetaData();
            Serializer.DeserializeAttributed(Paths.Font(fontName, fontName + ".agd"), ref atlasMetaData);

            textureAsset = new TextureAsset();
            textureAsset.LoadFile(Paths.Font(fontName, fontName + "_atlas.png"), Format.R8G8B8A8Unorm);
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
