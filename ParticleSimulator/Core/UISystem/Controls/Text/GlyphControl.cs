using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Registry.Assets;
using ArctisAurora.EngineWork.Rendering;
using ArctisAurora.EngineWork.Rendering.Helpers;
using Silk.NET.Maths;
using Silk.NET.Vulkan;

namespace ArctisAurora.Core.UISystem.Controls.Text
{
    public class GlyphControl : VulkanControl, IContext
    {
        public char character;
        int index;
        Glyph glyph;

        public GlyphControl(char character, FontAsset fontAsset, int px)
        {
            BubbleAll();
            this.character = character;
            maskAsset = fontAsset.textureAsset;

            (glyph, index) = fontAsset.atlasMetaData.GetGlyphAndIndex(character);

            preferredWidth = (int)(glyph.glyphWidth * px);
            preferredHeight = (int)(glyph.glyphHeight * px);

            float k = MathF.Ceiling(MathF.Sqrt(fontAsset.atlasMetaData.glyphCount));
            float glyphAtlasSize = 1f / k;
            float xOffset = index % k * glyphAtlasSize;

            float yOffset = MathF.Floor(index / k) * glyphAtlasSize;

            float texelPad = 1f / fontAsset.textureAsset.image.Width;

            float u0 = xOffset + texelPad;
            float v0 = yOffset + texelPad;
            float u1 = xOffset + glyphAtlasSize - texelPad;
            float v1 = yOffset + glyphAtlasSize - texelPad;

            controlData.uvs.uv1 = new Vector2D<float>(u1, v1);
            controlData.uvs.uv2 = new Vector2D<float>(u0, v0);
            controlData.uvs.uv3 = new Vector2D<float>(u0, v1);
            controlData.uvs.uv4 = new Vector2D<float>(u1, v0);
            AVulkanBufferHandler.UpdateBuffer(ref controlData, ref Renderer.transferQueue, ref Renderer.transferCommandPool, ref controlDataBuffer, ref controlDataBufferMemory, BufferUsageFlags.StorageBufferBit);
        }

        public void OnContextAdded()
        {
            (parent as IContext)?.OnContextAdded();
            UICollisionHandling.activeControl = (VulkanControl)parent;
        }

        public void OnContextRemoved()
        {
            (parent as IContext)?.OnContextRemoved();
        }
    }
}
