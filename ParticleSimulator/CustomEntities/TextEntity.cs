using ArctisAurora.EngineWork.AssetRegistry;
using ArctisAurora.EngineWork.EngineEntity;
using ArctisAurora.EngineWork.Rendering.UI;
using ArctisAurora.EngineWork.Rendering.UI.Controls;
using Silk.NET.Maths;

namespace ArctisAurora.CustomEntities
{
    internal class TextEntity : Entity
    {
        internal string text;
        internal List<GlyphControl> children = new List<GlyphControl>();

        FontAsset fontAsset;

        internal TextEntity(string text, int px, Vector3D<float> pos)
        {
            transform.SetWorldPosition(pos);
            this.text = text;

            Dictionary<string, FontAsset> d = AssetRegistries.GetRegistry<string, FontAsset>(typeof(FontAsset));
            fontAsset = d["default"];

            float horizontalOffset = 0;
            float verticalOffset = 0;
            for (int i = 0; i< text.Length; i++)
            {
                Glyph gAsset= fontAsset.atlasMetaData.GetGlyph(text[i]);
                horizontalOffset += (gAsset.lsb * px);
                verticalOffset = (gAsset.tsb * px);
                Vector3D<float> glyphPos = transform.position + new Vector3D<float>(0, verticalOffset, horizontalOffset);
                GlyphControl glyph = new GlyphControl(text[i], glyphPos, gAsset, fontAsset, px);
                children.Add(glyph);

                horizontalOffset += (gAsset.rsb * px);
            }
        }
    }
}
