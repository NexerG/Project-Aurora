using ArctisAurora.EngineWork.AssetRegistry;
using ArctisAurora.EngineWork.Rendering.Helpers;
using ArctisAurora.EngineWork.Rendering.MeshSubComponents;
using ArctisAurora.EngineWork.Rendering.UI;
using ArctisAurora.EngineWork.Rendering.UI.Controls;
using Silk.NET.Maths;
using Silk.NET.Vulkan;

namespace ArctisAurora.CustomEntities
{
    internal class GlyphEntity : VulkanControl
    {

        internal char character;
        int index;
        Glyph glyph;

        internal GlyphEntity(char character, Vector3D<float> pos, Glyph gAsset)
        {
            this.character = character;
            transform.SetWorldPosition(pos);
            FontAsset fa = AssetRegistries.fonts.GetValueOrDefault("default");
            (glyph, index) = fa.atlasMetaData.GetGlyphAndIndex(character);

            float k = MathF.Ceiling(MathF.Sqrt(fa.atlasMetaData.glyphCount));
            float glyphAtlasSize = 1f / k;
            float xOffset = (index % k) * glyphAtlasSize;

            float yOffset = MathF.Floor(index / k) * glyphAtlasSize;

            quadUV = new Vector2D<float>[4];
            quadUV[0] = new Vector2D<float>(xOffset, yOffset);
            quadUV[1] = new Vector2D<float>(xOffset + glyphAtlasSize, yOffset);
            quadUV[2] = new Vector2D<float>(xOffset + glyphAtlasSize, yOffset + glyphAtlasSize);
            quadUV[3] = new Vector2D<float>(xOffset, yOffset + glyphAtlasSize);
            AVulkanBufferHandler.UpdateBuffer(ref quadUV, ref uvBuffer, ref uvBufferMemory, BufferUsageFlags.StorageBufferBit);

            transform.SetWorldScale(new Vector3D<float>(1, glyph.glyphHeight, glyph.glyphWidth));

            //GetComponent<MCUI>().UpdateMatrices();
        }
    }
}
