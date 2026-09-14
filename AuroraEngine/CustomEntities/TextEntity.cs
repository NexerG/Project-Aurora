using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.Registry.Assets;
using ArctisAurora.Core.UISystem;
using ArctisAurora.Core.UISystem.Controls.Text;
using ArctisAurora.EngineWork.Registry;
using System.Numerics;

namespace ArctisAurora.CustomEntities
{
    internal class TextEntity : TransformEntity
    {
        internal string text;
        internal List<GlyphControl> children = new List<GlyphControl>();

        FontAsset fontAsset;

        internal TextEntity(string text, int px, Vector3 pos)
        {
            transform.SetWorldPosition(pos);
            this.text = text;

            Dictionary<string, FontAsset> d = AssetRegistries.GetRegistryByValueType<string, FontAsset>(typeof(FontAsset));
            fontAsset = d["default"];

            float horizontalOffset = 0;
            float verticalOffset = 0;
            for (int i = 0; i< text.Length; i++)
            {
                Glyph gAsset= fontAsset.atlasMetaData.GetGlyph(text[i]);
                horizontalOffset += (gAsset.regular.leftSideOffset * px);
                verticalOffset = (gAsset.regular.tsb * px);
                Vector3 glyphPos = transform.position + new Vector3(0, verticalOffset, horizontalOffset);
                GlyphControl glyph = new GlyphControl(text[i], fontAsset, px);
                children.Add(glyph);

                horizontalOffset += (gAsset.regular.advanceWidth * px);
            }
        }
    }
}
