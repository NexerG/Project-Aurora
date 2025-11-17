using ArctisAurora.EngineWork.AssetRegistry;
using ArctisAurora.EngineWork.Rendering.Helpers;
using ArctisAurora.EngineWork.Rendering.UI;
using ArctisAurora.EngineWork.Rendering.UI.Controls;
using Silk.NET.Maths;
using Silk.NET.Vulkan;

namespace ArctisAurora.Core.Rendering.UI.Controls.Text
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
            width = (int)(glyph.glyphWidth * px);
            height = (int)(glyph.glyphHeight * px);

            float k = MathF.Ceiling(MathF.Sqrt(fontAsset.atlasMetaData.glyphCount));
            float glyphAtlasSize = 1f / k;
            float xOffset = index % k * glyphAtlasSize;

            float yOffset = MathF.Floor(index / k) * glyphAtlasSize;

            controlData.uvs.uv1 = new Vector2D<float>(xOffset, yOffset);
            controlData.uvs.uv2 = new Vector2D<float>(xOffset + glyphAtlasSize, yOffset);
            controlData.uvs.uv3 = new Vector2D<float>(xOffset + glyphAtlasSize, yOffset + glyphAtlasSize);
            controlData.uvs.uv4 = new Vector2D<float>(xOffset, yOffset + glyphAtlasSize);
            AVulkanBufferHandler.UpdateBuffer(ref controlData, ref controlDataBuffer, ref controlDataBufferMemory, BufferUsageFlags.StorageBufferBit);
            transform.SetWorldScale(new Vector3D<float>(width, height, 1));
        }
    }
}
