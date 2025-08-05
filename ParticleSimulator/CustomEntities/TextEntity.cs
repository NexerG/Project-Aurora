using ArctisAurora.EngineWork.AssetRegistry;
using ArctisAurora.EngineWork.EngineEntity;
using ArctisAurora.EngineWork.Rendering.MeshSubComponents;
using ArctisAurora.EngineWork.Rendering.UI;
using ArctisAurora.EngineWork.Serialization;
using Assimp;
using Silk.NET.Maths;

namespace ArctisAurora.CustomEntities
{
    internal class TextEntity : Entity
    {
        internal string text;
        internal List<GlyphEntity> children = new List<GlyphEntity>();

        FontAsset fontAsset;

        internal TextEntity(string text)
        {
            transform.SetWorldPosition(new Vector3D<float>(2, 0, -200));
            this.text = text;

            fontAsset = AssetRegistries.fonts["default"];

            float horizontalOffset = 0;
            float verticalOffset = 0;
            for (int i = 0; i< text.Length; i++)
            {
                Glyph gAsset= fontAsset.atlasMetaData.GetGlyph(text[i]);
                horizontalOffset += (gAsset.lsb * gAsset.px);
                verticalOffset = (gAsset.tsb * gAsset.px);
                Vector3D<float> glyphPos = transform.position + new Vector3D<float>(0, verticalOffset, horizontalOffset);
                GlyphEntity glyph = new GlyphEntity(text[i], glyphPos, gAsset);
                children.Add(glyph);

                horizontalOffset += (gAsset.rsb * gAsset.px);
            }
        }
    }
}
