using ArctisAurora.Core.UISystem.Controls;
using ArctisAurora.EngineWork.AssetRegistry;
using ArctisAurora.EngineWork.Rendering.Helpers;
using Silk.NET.Maths;
using Silk.NET.Vulkan;

namespace ArctisAurora.Core.UISystem.Controls.Text
{
    public class GlyphControl : VulkanControl
    {
        public char character;
        int index;
        Glyph glyph;

        public GlyphControl(char character, FontAsset fontAsset, int px)
        {
            this.character = character;
            maskAsset = fontAsset.textureAsset;

            (glyph, index) = fontAsset.atlasMetaData.GetGlyphAndIndex(character);
            preferredWidth = (int)(glyph.glyphWidth * px);
            preferredHeight = (int)(glyph.glyphHeight * px);

            float k = MathF.Ceiling(MathF.Sqrt(fontAsset.atlasMetaData.glyphCount));
            float glyphAtlasSize = 1f / k;
            float xOffset = index % k * glyphAtlasSize;

            float yOffset = MathF.Floor(index / k) * glyphAtlasSize;

            controlData.uvs.uv1 = new Vector2D<float>(xOffset + glyphAtlasSize, yOffset + glyphAtlasSize);
            controlData.uvs.uv2 = new Vector2D<float>(xOffset, yOffset);
            controlData.uvs.uv3 = new Vector2D<float>(xOffset, yOffset + glyphAtlasSize);
            controlData.uvs.uv4 = new Vector2D<float>(xOffset + glyphAtlasSize, yOffset);
            AVulkanBufferHandler.UpdateBuffer(ref controlData, ref controlDataBuffer, ref controlDataBufferMemory, BufferUsageFlags.StorageBufferBit);
        }
    }
}
