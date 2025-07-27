using ArctisAurora.EngineWork.ECS.RenderingComponents.Vulkan;
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

        internal TextEntity(string text)
        {
            MeshImporter importer = new MeshImporter();
            Scene scene1 = importer.ImportFBX("C:\\Users\\gmgyt\\Desktop\\VienetinisPlane.fbx");
            transform.SetWorldPosition(new Vector3D<float>(2, 0, 0));
            this.text = text;

            int horizontalOffset = 0;
            int verticalOffset = 0;
            for (int i = 0; i< text.Length; i++)
            {
                Vector3D<float> glyphPos = transform.position + new Vector3D<float>(0, verticalOffset, horizontalOffset);
                GlyphEntity glyph = new GlyphEntity(text[i], glyphPos);
                children.Add(glyph);
                glyph.GetComponent<MeshComponent>().LoadCustomMesh(scene1);

                //horizontalOffset += ;
            }
        }
    }
}
