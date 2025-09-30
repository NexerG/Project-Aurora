using ArctisAurora.EngineWork.AssetRegistry;
using ArctisAurora.EngineWork.Rendering.Helpers;
using Silk.NET.Maths;
using Silk.NET.Vulkan;

namespace ArctisAurora.EngineWork.Rendering.UI.Controls
{
    public class GlyphControl : VulkanControl
    {
        public char character;
        int index;
        Glyph glyph;

        public GlyphControl(char character, Vector3D<float> pos, Glyph gAsset, FontAsset fontAsset, int px)
        {
            this.character = character;
            maskAsset = fontAsset.textureAsset;

            transform.SetWorldPosition(pos);
            (glyph, index) = fontAsset.atlasMetaData.GetGlyphAndIndex(character);
            this.px = new Vector2D<float>(glyph.glyphHeight * px, glyph.glyphWidth * px);

            float k = MathF.Ceiling(MathF.Sqrt(fontAsset.atlasMetaData.glyphCount));
            float glyphAtlasSize = 1f / k;
            float xOffset = index % k * glyphAtlasSize;

            float yOffset = MathF.Floor(index / k) * glyphAtlasSize;

            controlData.quadData.uvs.uv1 = new Vector2D<float>(xOffset, yOffset);
            controlData.quadData.uvs.uv2 = new Vector2D<float>(xOffset + glyphAtlasSize, yOffset);
            controlData.quadData.uvs.uv3 = new Vector2D<float>(xOffset + glyphAtlasSize, yOffset + glyphAtlasSize);
            controlData.quadData.uvs.uv4 = new Vector2D<float>(xOffset, yOffset + glyphAtlasSize);
            AVulkanBufferHandler.UpdateBuffer(ref controlData, ref controlDataBuffer, ref controlDataBufferMemory, BufferUsageFlags.StorageBufferBit);
            transform.SetWorldScale(new Vector3D<float>(1, this.px.X, this.px.Y));
        }
    }
}
