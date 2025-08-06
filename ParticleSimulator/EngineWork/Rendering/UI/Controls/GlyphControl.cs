using ArctisAurora.EngineWork.AssetRegistry;
using ArctisAurora.EngineWork.Rendering.Helpers;
using Silk.NET.Maths;
using Silk.NET.Vulkan;

namespace ArctisAurora.EngineWork.Rendering.UI.Controls
{
    internal class GlyphControl : VulkanControl
    {
        internal char character;
        int index;
        Glyph glyph;

        internal GlyphControl(char character, Vector3D<float> pos, Glyph gAsset, FontAsset fontAsset)
        {
            this.character = character;
            transform.SetWorldPosition(pos);
            (glyph, index) = fontAsset.atlasMetaData.GetGlyphAndIndex(character);

            float k = MathF.Ceiling(MathF.Sqrt(fontAsset.atlasMetaData.glyphCount));
            float glyphAtlasSize = 1f / k;
            float xOffset = index % k * glyphAtlasSize;

            float yOffset = MathF.Floor(index / k) * glyphAtlasSize;

            quadData.uvs.uv1 = new Vector2D<float>(xOffset, yOffset);
            quadData.uvs.uv2 = new Vector2D<float>(xOffset + glyphAtlasSize, yOffset);
            quadData.uvs.uv3 = new Vector2D<float>(xOffset + glyphAtlasSize, yOffset + glyphAtlasSize);
            quadData.uvs.uv4 = new Vector2D<float>(xOffset, yOffset + glyphAtlasSize);
            AVulkanBufferHandler.UpdateBuffer(ref quadData, ref quadDataBuffer, ref quadDataBufferMemory, BufferUsageFlags.StorageBufferBit);
            transform.SetWorldScale(new Vector3D<float>(1, glyph.glyphHeight, glyph.glyphWidth));
        }
    }
}
