using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.Registry.Assets;
using ArctisAurora.Core.UISystem.Controls.Containers;
using ArctisAurora.EngineWork.Registry;
using Silk.NET.Maths;

namespace ArctisAurora.Core.UISystem.Controls.Text.Document
{
    // One LineSegment's worth of glyphs: a straight horizontal strip of text in a single style,
    // sitting on a single baseline. It never wraps and never looks for a break, because a segment
    // cannot contain one — DocumentLayoutCache already decided where the text broke and this only
    // draws what it was handed.
    //
    // That is the whole difference from TextInputControl, which re-derives its own wrapping in
    // Measure and Arrange and can therefore disagree with the cache about where a line ends. Here
    // the x, y, width and baseline all arrive from the cache, so the drawn glyphs and the geometry
    // used to hit-test them cannot come apart.
    //
    // A container base for the same reason TextControl uses one: a plain VulkanControl accepts a
    // single child, and a strip holds one GlyphControl per character.
    public class TextRunControl : AbstractContainerControl
    {
        // Where this strip sits in document space — not screen space. The canvas adds its own
        // origin, which ScrollableControl has already shifted by the scroll offset.
        public LayoutRect documentRect;

        // Arranged top down to the baseline, taken from the cached line's ascent. Shared across the
        // segments on a line so that mixed sizes sit on one baseline rather than each centring
        // itself in its own box.
        public float baselineOffset;

        public TextRunControl()
        {
            maskAsset = AssetRegistries.GetAsset<TextureAsset>("invisible");
        }

        // Point the control at a different piece of text. One call rather than a property per field
        // because the view reassigns a control wholesale — none of the previous contents relate to
        // the new ones, so there is no incremental case worth detecting the way TextControl does.
        public void SetSegment(string text, string fontName, int fontSize, string colorHex)
        {
            FontAsset fontAsset = ResolveFont(fontName);

            // parent is cleared first because Destroy() would otherwise remove the glyph from
            // `children` itself, shifting the list out from under the loop editing it.
            foreach (Entity existing in children)
            {
                existing.parent = null;
                existing.Destroy();
            }
            children.Clear();

            float width = 0f;
            foreach (char character in text)
            {
                GlyphControl glyph = new GlyphControl(character, fontAsset, fontSize);
                glyph.controlColorHex = colorHex;
                glyph.parent = this;
                children.Add(glyph);

                // Measured now rather than left to the layout pass: a control materialized during
                // Arrange has already missed this frame's Measure, and its glyph quads would be
                // arranged at whatever size the pool row happened to hold.
                glyph.Measure(new Vector2D<float>(float.MaxValue, float.MaxValue));
                width += glyph.advance;
            }

            DesiredSize = new Vector2D<float>(width, documentRect.height);
            MarkTreeOrderDirty();
        }

        // The cache already decided this strip's box, so measuring is a lookup rather than a
        // computation. The glyphs still need their own Measure to size their quads.
        public override Vector2D<float> Measure(Vector2D<float> availableSize)
        {
            foreach (Entity child in children)
                if (child is VulkanControl glyph)
                    glyph.Measure(availableSize);

            DesiredSize = new Vector2D<float>(documentRect.width, documentRect.height);
            isMeasureDirty = false;
            return DesiredSize;
        }

        public override void Arrange(LayoutRect finalRect)
        {
            arrangedRect = finalRect;
            WriteArrangedTransform(finalRect);

            ClipRect = parent is VulkanControl parentControl
                ? (clipOutOfBounds ? LayoutRect.Intersect(finalRect, parentControl.ClipRect) : parentControl.ClipRect)
                : finalRect;

            float baselineY = finalRect.y + baselineOffset;
            float pen = finalRect.x;

            foreach (Entity child in children)
            {
                if (child is not GlyphControl glyph) continue;

                // Ink sits at pen + bearing and hangs off the baseline; the pen then moves by the
                // advance — the same formula the cache measured the segment's width with.
                glyph.Arrange(new LayoutRect(pen + glyph.bearingX, baselineY - glyph.ascent,
                    glyph.DesiredSize.X, glyph.DesiredSize.Y));
                pen += glyph.advance;
            }

            isArrangeDirty = false;
        }

        // Mirrors FontAssetGlyphMetrics.Resolve. The view has to land on the same font the cache
        // measured with; picking a different one would put every glyph on this line at an x the
        // geometry never predicted.
        private static FontAsset ResolveFont(string fontName)
        {
            Dictionary<string, FontAsset> fonts =
                AssetRegistries.GetRegistryByValueType<string, FontAsset>(typeof(FontAsset));

            return fonts.TryGetValue(fontName ?? "default", out FontAsset named) ? named : fonts["default"];
        }
    }
}
