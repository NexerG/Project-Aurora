using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Registry.Assets;
using Silk.NET.Maths;
using Silk.NET.Vulkan;

namespace ArctisAurora.Core.UISystem.Controls.Text
{
    public class GlyphControl : VulkanControl
    {
        public override bool canBeActiveContext => false;

        // Internal, not private: TextMeasurer reproduces this cell geometry without building a
        // control, and sharing the constants is what stops the two from drifting apart.
        internal const float atlasInkMargin = 0.1f;

        internal const float CellScale = 1f / (1f - 2f * atlasInkMargin);

        public char character;
        public FontStyle style = FontStyle.Regular;
        int index;
        Glyph? glyph;

        public float ascent;
        public float descent;

        public float advance;
        public float bearingX;

        private float _cellW;
        private float _cellH;

        public GlyphControl(char character, FontAsset fontAsset, int px, FontStyle style = FontStyle.Regular)
        {
            BubbleAll();
            this.style = style;
            SetCharacter(character, fontAsset, px);
        }

        // Repoint an existing glyph at a different character instead of replacing the control.
        // Everything a GlyphControl holds is derived from (character, font, size), so a rewrite is a
        // handful of field writes and a UV update — no pool row allocated, no deferred free, no tree
        // reorder. That is what lets a text edit cost the characters that actually changed instead of
        // every character that happens to sit after the edit.
        public void SetCharacter(char character, FontAsset fontAsset, int px)
        {
            this.character = character;
            maskAsset = fontAsset.textureAsset;

            (glyph, index) = fontAsset.atlasMetaData.GetGlyphAndIndex(character);

            // A character outside the imported set used to arrive here as null and throw on the
            // next line. Fall back to a blank of space width so an unexpected glyph is a visible
            // gap, not a crash.
            if (glyph == null)
            {
                (glyph, index) = fontAsset.atlasMetaData.GetGlyphAndIndex(' ');
                if (glyph == null) { glyph = new Glyph(); index = 0; }
            }

            // A style the family has no face for collapses to regular, in the metrics and the cell alike.
            FontStyle effective = fontAsset.atlasMetaData.Effective(style);
            GlyphMetrics metrics = glyph.Metrics(effective);

            _cellW = metrics.glyphWidth * px * CellScale;
            _cellH = metrics.glyphHeight * px * CellScale;
            float cellW = _cellW;
            preferredWidth = (int)_cellW;
            preferredHeight = (int)_cellH;

            advance = metrics.advanceWidth * px;
            bearingX = metrics.leftSideOffset * px - cellW * atlasInkMargin;

            // Cleared before the test rather than left to fall through it: on a reused control these
            // still hold the previous character's metrics, and a glyph with no vertical range would
            // otherwise sit on the baseline the character before it had.
            ascent = 0f;
            descent = 0f;

            int range = metrics.yMax - metrics.yMin;
            if (range != 0)
            {
                // baselineFromTop is a fraction OF THE CELL, so it must scale the cell height.
                float baselineFromTop = atlasInkMargin + (1f - 2f * atlasInkMargin) * metrics.yMax / range;
                ascent = baselineFromTop * _cellH;
                descent = _cellH - ascent;
            }

            int cell = fontAsset.atlasMetaData.CellIndex(index, effective);
            float k = MathF.Ceiling(MathF.Sqrt(fontAsset.atlasMetaData.cellCount));
            float glyphAtlasSize = 1f / k;
            float xOffset = cell % k * glyphAtlasSize;

            float yOffset = MathF.Floor(cell / k) * glyphAtlasSize;

            float texelPad = 1f / fontAsset.textureAsset.image.Width;

            float u0 = xOffset + texelPad;
            float v0 = yOffset + texelPad;
            float u1 = xOffset + glyphAtlasSize - texelPad;
            float v1 = yOffset + glyphAtlasSize - texelPad;

            controlData.uvs.uv1 = new Vector2D<float>(u1, v1);
            controlData.uvs.uv2 = new Vector2D<float>(u0, v0);
            controlData.uvs.uv3 = new Vector2D<float>(u0, v1);
            controlData.uvs.uv4 = new Vector2D<float>(u1, v0);
            UpdateControlData();

            // The preferred size setters invalidate on their own, but only when the number changes —
            // and two characters routinely share a cell size while differing in advance, bearing and
            // UVs. Layout has to re-run whenever the character does, not whenever the box moves.
            InvalidateLayout();
        }


        public override Vector2D<float> Measure(Vector2D<float> availableSize)
        {
            DesiredSize = new Vector2D<float>(_cellW, _cellH);
            isMeasureDirty = false;
            return DesiredSize;
        }
    }
}
