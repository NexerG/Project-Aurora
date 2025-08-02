using ArctisAurora.EngineWork.AssetRegistry;
using ArctisAurora.EngineWork.ECS.RenderingComponents.Vulkan;
using ArctisAurora.EngineWork.Renderer.UI;
using ArctisAurora.EngineWork.Serialization;
using ArctisAurora.GameObject;
using Assimp;
using Silk.NET.Maths;
using System.Windows.Forms.Design;

namespace ArctisAurora.CustomEntities
{
    internal class TextEntity : Entity
    {
        internal string text;
        internal List<GlyphEntity> children = new List<GlyphEntity>();

        FontAsset fontAsset;

        internal TextEntity(string text)
        {
            MeshImporter importer = new MeshImporter();
            Scene scene1 = importer.ImportFBX("C:\\Users\\gmgyt\\Desktop\\VienetinisPlane.fbx");
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
                GlyphEntity glyph = new GlyphEntity(text[i], glyphPos);
                children.Add(glyph);
                glyph.GetComponent<MeshComponent>().LoadCustomMesh(scene1);

                horizontalOffset += (gAsset.rsb * gAsset.px);
            }
        }
    }
}
