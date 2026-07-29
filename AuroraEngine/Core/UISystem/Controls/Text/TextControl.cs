using ArctisAurora.Core.Registry;
using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.UISystem.Controls.Containers;
using ArctisAurora.EngineWork.Registry;
using Silk.NET.Maths;
using ArctisAurora.Core.Registry.Assets;

namespace ArctisAurora.Core.UISystem.Controls.Text
{
    public abstract class TextControl : AbstractContainerControl
    {
        internal string newEdit = string.Empty;
        public bool isEditing = false;
        private FontAsset _fontAsset;

        [A_XSDElementProperty("Text", "UI", "The string to display.")]
        public string text
        {
            get => field;
            set
            {
                if (field == value) return;
                field = value;
                //RebuildGlyphs();
                SyncGlyphs();
            }
        } = string.Empty;

        [A_XSDElementProperty("FontSize", "UI", "Font size in pixels.")]
        public int fontSize
        {
            get => field;
            set
            {
                if (field == value) return;
                field = value;
                //RebuildGlyphs();
                //SyncGlyphs();
                InvalidateLayout();
            }
        } = 16;

        public TextControl()
        {
            Dictionary<string, FontAsset> d = AssetRegistries.GetRegistryByValueType<string, FontAsset>(typeof(FontAsset));
            _fontAsset = d["default"];
            maskAsset = AssetRegistries.GetAsset<TextureAsset>("invisible");
        }

        public TextControl(string text, FontAsset fontAsset, int fontSize = 16)
        {
            _fontAsset = fontAsset;
            fontSize = fontSize;
            text = text;
            //RebuildGlyphs();
            foreach (char c in text)
            {
                GlyphControl glyph = new GlyphControl(c, _fontAsset, fontSize);
                glyph.parent = this;
                children.Add(glyph);
            }
            InvalidateLayout();
            maskAsset = AssetRegistries.GetAsset<TextureAsset>("invisible");
        }

        // Drop a glyph out of the tree for good: pool row, Vulkan buffer and registry entries all
        // go at the next frame edge. parent is cleared first because Destroy() would otherwise
        // remove the glyph from `children` itself, shifting the list under the loop editing it.
        //
        // Detaching without destroying is what used to happen here, and it leaked twice over: the
        // glyph kept its UIControls slot forever (so the pool only ever grew, and every growth
        // forced a full descriptor rebuild) and it stayed unreachable from the control tree, which
        // is exactly the case UI.DFSOrder cannot produce an order for.
        private static void DiscardGlyph(Entity glyph)
        {
            glyph.parent = null;
            glyph.Destroy();
        }

        // NOTE: dead — every caller is commented out in favour of SyncGlyphs. Kept in sync with it
        // anyway so uncommenting it does not reintroduce the leak.
        private void RebuildGlyphs()
        {
            foreach (Entity glyph in children)
                DiscardGlyph(glyph);
            children.Clear();

            if (_fontAsset == null || string.IsNullOrEmpty(text)) return;

            foreach (char c in text)
            {
                GlyphControl glyph = new GlyphControl(c, _fontAsset, fontSize);
                glyph.parent = this;
                children.Add(glyph);
            }
            MarkTreeOrderDirty();
            InvalidateLayout();
        }

        private void SyncGlyphs()
        {
            if (_fontAsset == null)
            {
                foreach (Entity glyph in children)
                    DiscardGlyph(glyph);
                children.Clear();
                return;
            }

            string target = text ?? string.Empty;
            int targetLen = target.Length;
            int existingLen = children.Count;

            // Every new glyph's pool row lands at the dense tail, but in DFS order it belongs
            // wherever this control sits — so any change here puts the pool out of paint order.
            bool treeChanged = false;

            // Update or reuse existing glyphs
            int i = 0;
            for (; i < targetLen; i++)
            {
                if (i < existingLen)
                {
                    GlyphControl existing = children[i] as GlyphControl;
                    if (existing != null && existing.character == target[i])
                        continue; // same char — skip

                    // Different char — replace in place
                    Entity replaced = children[i];
                    GlyphControl replacement = new GlyphControl(target[i], _fontAsset, fontSize);
                    replacement.parent = this;
                    children[i] = replacement;   // detached by the overwrite
                    DiscardGlyph(replaced);
                    treeChanged = true;
                }
                else
                {
                    // Append new glyph
                    GlyphControl glyph = new GlyphControl(target[i], _fontAsset, fontSize);
                    glyph.parent = this;
                    children.Add(glyph);
                    treeChanged = true;
                }
            }

            // Remove excess glyphs from the end
            if (existingLen > targetLen)
            {
                for (int r = targetLen; r < existingLen; r++)
                    DiscardGlyph(children[r]);
                children.RemoveRange(targetLen, existingLen - targetLen);
                treeChanged = true;
            }

            if (treeChanged) MarkTreeOrderDirty();
            InvalidateLayout();
        }

        // These three only edit the string — the setter runs SyncGlyphs, which owns glyph creation
        // and disposal. InsertGlyph used to also build a GlyphControl and parent it here without
        // ever adding it to `children`; SyncGlyphs then built the real one, so every keystroke
        // leaked an entire control (pool slot, SSBO and a "Controls" group entry that was still
        // being drawn) that nothing could reach to destroy.
        internal void InsertGlyph(int index, char c)
        {
            text = text.Insert(index, c.ToString());
        }

        internal void RemoveGlyph(int index)
        {
            if (index < 0 || index >= children.Count) return;
            text = text.Remove(index, 1);
        }

        internal void RemoveGlyphRange(int start, int count)
        {
            text = text.Remove(start, count);
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
                // Glyphs advance by their typographic advance; anything else by its own width.
                totalWidth += vc is GlyphControl g ? g.advance : desired.X;
                if (desired.Y > maxHeight) maxHeight = desired.Y;
            }

            // Respect explicit preferred size if set, otherwise size-to-content
            float w = preferredWidth > 0 ? preferredWidth : totalWidth;
            float h = preferredHeight > 0 ? preferredHeight : maxHeight;

            DesiredSize = new Vector2D<float>(w, h);
            isMeasureDirty = false;
            return DesiredSize;
        }

        // Arrange: place glyphs left-to-right, vertically centered within the row
        public override void Arrange(LayoutRect finalRect)
        {
            // Let base handle own transform + ClipRect
            arrangedRect = finalRect;
            WriteArrangedTransform(finalRect);

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

                // Ink sits at pen + bearing; the pen then moves by the advance.
                GlyphControl glyph = vc as GlyphControl;
                vc.Arrange(new LayoutRect(cursor + (glyph?.bearingX ?? 0f), cy, glyphW, glyphH));
                cursor += glyph?.advance ?? glyphW;
            }

            isArrangeDirty = false;
        }

        public override void AddChild(Entity entity)
        {
            if (entity is not VulkanControl control)
                throw new Exception("TextControl children must be VulkanControls");
            control.parent = this;
            children.Add(control);
            MarkTreeOrderDirty();
            InvalidateLayout();
        }

        public abstract void BeginEdit();

        public abstract void CommitEdit();

        public abstract void CancelEdit();

        /*public virtual void InsertAt(int charOffset, string insert)
        {
            text = text[..charOffset] + insert + text[charOffset..];
        }*/

        public abstract void WriteChar(char c);
    }
}