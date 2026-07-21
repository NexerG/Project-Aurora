using ArctisAurora.Core.Filing.Serialization;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.UISystem;
using ArctisAurora.EngineWork.Registry;

namespace ArctisAurora.Core.Registry.Assets
{
    [A_XSDType("FontAsset", "AssetRegistry")]
    public class FontAsset : AbstractAsset
    {
        public AtlasMetaData atlasMetaData;
        public TextureAsset textureAsset;

        public FontAsset() { }
        public FontAsset(string name)
        {
            Dictionary<string, FontAsset> d = AssetRegistries.GetRegistryByValueType<string, FontAsset>(typeof(FontAsset));
            d.Add(name, this);
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

        public override void LoadAll(string path)
        {
            throw new NotImplementedException();
        }

        public override void LoadAsset(AbstractAsset asset, string name, string path)
        {
            Dictionary<string, FontAsset> d = AssetRegistries.GetRegistryByValueType<string, FontAsset>(typeof(FontAsset));
            if (d.ContainsKey(name))
            {
                asset = d[name];
                return;
            }
            atlasMetaData = new AtlasMetaData();
            atlasMetaData.Deserialize(name);

            string imagePath = Paths.FONTS + "\\" + name + "\\" + name + "_atlas.png";
            if (System.IO.File.Exists(imagePath))
            {
                textureAsset.LoadAsset(asset, name, path);
                d[name] = this;
                return;
            }

            throw new Exception(name);
        }

        public override void LoadDefault()
        {
            atlasMetaData = new AtlasMetaData();
            string path = Paths.FONTS + $"\\{"arial"}\\{"arial"}.agd";
            if(File.Exists(path))
            {
                Serializer.DeserializeAttributed(path, ref atlasMetaData);
            }

            string imagePath = Paths.FONTS + "\\arial\\" + "arial_atlas.png";
            if (File.Exists(imagePath))
            {
                textureAsset = new TextureAsset("uidefault");
                textureAsset.LoadAsset(this, "arial", imagePath);
            }
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
