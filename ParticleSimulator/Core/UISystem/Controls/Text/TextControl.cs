using ArctisAurora.Core.AssetRegistry;
using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.UISystem.Controls;
using ArctisAurora.Core.UISystem.Controls.Containers;
using ArctisAurora.EngineWork.AssetRegistry;
using Silk.NET.Maths;

namespace ArctisAurora.Core.UISystem.Controls.Text
{
    [A_XSDType("TextControl", "UI", allowedChildren:typeof(IXMLChild_UI))]
    public class TextControl : AbstractContainerControl
    {
        private string _text = string.Empty;
        private FontAsset _fontAsset;
        private int _fontSize = 16;

        [A_XSDElementProperty("Text", "UI", "The string to display.")]
        public string text
        {
            get => _text;
            set 
            {
                if (_text == value) return;
                _text = value;
                RebuildGlyphs();
            }
        }

        [A_XSDElementProperty("FontSize", "UI", "Font size in pixels.")]
        public int fontSize
        {
            get => _fontSize;
            set 
            {
                if (_fontSize == value) return;
                _fontSize = value;
                RebuildGlyphs();
            }
        }

        public TextControl()
        {
            Dictionary<string, FontAsset> d = AssetRegistries.GetRegistryByValueType<string, FontAsset>(typeof(FontAsset));
            _fontAsset = d["default"];
            maskAsset = AssetRegistries.GetAsset<TextureAsset>("invisible");
        }

        public TextControl(string text, FontAsset fontAsset, int fontSize = 16)
        {
            _fontAsset = fontAsset;
            _fontSize = fontSize;
            _text = text;
            RebuildGlyphs();
            maskAsset = AssetRegistries.GetAsset<TextureAsset>("invisible");
        }

        private void RebuildGlyphs()
        {
            // Clear old glyph children without triggering full entity destroy
            children.Clear();

            if (_fontAsset == null || string.IsNullOrEmpty(_text)) return;

            foreach (char c in _text)
            {
                GlyphControl glyph = new GlyphControl(c, _fontAsset, _fontSize);
                glyph.parent = this;
                children.Add(glyph);
            }
            InvalidateLayout();
        }

        // Measure: sum glyph widths, take max height
        public override Vector2D<float> Measure(Vector2D<float> availableSize)
        {
            float totalWidth = 0f;
            float maxHeight = 0f;

            foreach (Entity child in children)
            {
                if (child is not VulkanControl vc) continue;
                Vector2D<float> desired = vc.Measure(new Vector2D<float>(availableSize.X - totalWidth, availableSize.Y));
                totalWidth += desired.X;
                if (desired.Y > maxHeight) maxHeight = desired.Y;
            }

            // Respect explicit preferred size if set, otherwise size-to-content
            float w = preferredWidth > 0 ? preferredWidth : totalWidth;
            float h = preferredHeight > 0 ? preferredHeight : maxHeight;

            DesiredSize = new Vector2D<float>(w, h);
            IsMeasureDirty = false;
            return DesiredSize;
        }

        // Arrange: place glyphs left-to-right, vertically centered within the row
        public override void Arrange(LayoutRect finalRect)
        {
            // Let base handle own transform + ClipRect
            arrangedRect = finalRect;
            transform.SetWorldPosition(new Vector3D<float>(
                finalRect.x + finalRect.width / 2f,
                finalRect.y + finalRect.height / 2f,
                parent != null
                    ? parent.transform.GetEntityPosition().Z + 0.001f
                    : transform.GetEntityPosition().Z));
            transform.SetWorldScale(new Vector3D<float>(finalRect.width, finalRect.height, 1));

            if (parent is VulkanControl parentControl)
                ClipRect = clipOutOfBounds
                    ? LayoutRect.Intersect(finalRect, parentControl.ClipRect)
                    : parentControl.ClipRect;
            else
                ClipRect = finalRect;

            // Flow children left-to-right inside padding
            LayoutRect innerRect = finalRect.Shrink(padding);
            float cursor = innerRect.x;

            foreach (Entity child in children)
            {
                if (child is not VulkanControl vc) continue;

                float glyphW = vc.DesiredSize.X;
                float glyphH = vc.DesiredSize.Y;

                // Vertically center each glyph in the row
                float cy = innerRect.y + (innerRect.height - glyphH) * 0.5f;

                vc.Arrange(new LayoutRect(cursor, cy, glyphW, glyphH));
                cursor += glyphW;
            }

            isArrangeDirty = false;
        }

        public override void AddChild(Entity entity)
        {
            if (entity is not VulkanControl control)
                throw new Exception("TextControl children must be VulkanControls");
            control.parent = this;
            children.Add(control);
            InvalidateLayout();
        }
    }
}