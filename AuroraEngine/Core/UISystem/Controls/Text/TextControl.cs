using ArctisAurora.Core.Registry;
using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.UISystem.Controls.Containers;
using ArctisAurora.Core.UISystem.Controls.Text.Document;
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

        // measurement
        private static FontAssetGlyphMetrics metrics;
        private readonly TextMeasurer.Run[] runBuffer = new TextMeasurer.Run[1];
        protected BlockLayout layout;

        #region ---- line flow ----
        // set by the parent before Measure; reported back by Measure
        public float firstLineOffset;
        public float lastLineEndX;
        public float lastLineHeight;
        public float lineHeight = 1.5f;

        // caret offset, in characters
        public int cursorPosition = 0;
        #endregion

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
                RepointGlyphs();
                InvalidateLayout();
            }
        } = 16;

        // Repoints the glyphs, the same way fontSize and fontName do.
        public override string controlColorHex
        {
            get => base.controlColorHex;
            set
            {
                base.controlColorHex = value;
                RepointGlyphs();
            }
        }

        // Resolved into fontSize by the owning block's ApplyLayout, not read at draw time.
        [A_XSDElementProperty("StylingType", "UI", "Style this text takes; Inherit follows the block.")]
        public TextStyleType stylingType { get; set; } = TextStyleType.Inherit;

        [A_XSDElementProperty("FontName", "UI", "Name of the font asset to draw with.")]
        public string fontName
        {
            get => field;
            set
            {
                if (field == value) return;
                field = value;
                _fontAsset = ResolveFont(value);
                RepointGlyphs();
                InvalidateLayout();
            }
        } = "default";

        public TextControl()
        {
            _fontAsset = ResolveFont(fontName);
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
        private static void DiscardGlyph(Entity glyph)
        {
            glyph.parent = null;
            glyph.Destroy();
        }

        // Mirrors FontAssetGlyphMetrics.Resolve.
        private static FontAsset ResolveFont(string name)
        {
            Dictionary<string, FontAsset> fonts =
                AssetRegistries.GetRegistryByValueType<string, FontAsset>(typeof(FontAsset));

            return fonts.TryGetValue(name ?? "default", out FontAsset named) ? named : fonts["default"];
        }

        // Rewrites every glyph at the current font, size and colour.
        private void RepointGlyphs()
        {
            if (_fontAsset == null) return;

            foreach (Entity child in children)
                if (child is GlyphControl glyph)
                {
                    glyph.SetCharacter(glyph.character, _fontAsset, fontSize);
                    glyph.controlColorHex = controlColorHex;
                }
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

                    if (existing != null)
                    {
                        // Different char — repoint the control rather than swap it out. The diff is
                        // positional, so inserting a character mismatches every position after it;
                        // replacing them meant one pool allocation, one deferred free and an O(n)
                        // list removal per character, so typing at the head of a long paragraph cost
                        // the whole tail. Rewriting them costs a field update each, and glyphs are
                        // only ever appended to or trimmed from the end.
                        existing.SetCharacter(target[i], _fontAsset, fontSize);
                        continue;
                    }

                    // Not a glyph at all — AddChild accepts any VulkanControl, and there is nothing
                    // to repoint on one, so this case still has to swap.
                    Entity replaced = children[i];
                    GlyphControl replacement = new GlyphControl(target[i], _fontAsset, fontSize);
                    replacement.parent = this;
                    replacement.controlColorHex = controlColorHex;
                    children[i] = replacement;   // detached by the overwrite
                    DiscardGlyph(replaced);
                    treeChanged = true;
                }
                else
                {
                    // Append new glyph
                    GlyphControl glyph = new GlyphControl(target[i], _fontAsset, fontSize);
                    glyph.parent = this;
                    glyph.controlColorHex = controlColorHex;
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

        // Measures the glyph quads, then wraps into lines via TextMeasurer.
        // Width the text wraps at. A single-line control overrides this so its text never wraps,
        // while its own width still comes from preferredWidth.
        protected virtual float WrapWidth(float available) => preferredWidth > 0 ? preferredWidth : available;

        public override Vector2D<float> Measure(Vector2D<float> availableSize)
        {
            foreach (Entity child in children)
                if (child is VulkanControl vc)
                    vc.Measure(new Vector2D<float>(float.MaxValue, float.MaxValue));

            metrics ??= new FontAssetGlyphMetrics();

            float contentWidth = WrapWidth(availableSize.X);

            runBuffer[0] = new TextMeasurer.Run(text ?? string.Empty, fontName, fontSize);
            layout = TextMeasurer.MeasureBlock(runBuffer, contentWidth, metrics, lineHeight, firstLineOffset);

            TextLine last = layout.lines[layout.lines.Count - 1];
            lastLineEndX = layout.lines.Count == 1 ? firstLineOffset + last.width : last.width;
            lastLineHeight = last.height;

            float w = preferredWidth > 0 ? preferredWidth : layout.width;
            float h = preferredHeight > 0 ? preferredHeight : layout.height;

            DesiredSize = new Vector2D<float>(w, h);
            isMeasureDirty = false;
            return DesiredSize;
        }

        // Places the glyphs on the lines Measure produced.
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

            isArrangeDirty = false;
            if (layout == null) return;

            LayoutRect innerRect = finalRect.Shrink(padding);

            for (int l = 0; l < layout.lines.Count; l++)
            {
                TextLine line = layout.lines[l];

                float baselineY = innerRect.y + line.baseline;
                float pen = innerRect.x + (l == 0 ? firstLineOffset : 0f);

                foreach (LineSegment segment in line.segments)
                {
                    for (int k = 0; k < segment.charCount; k++)
                    {
                        int index = segment.charStart + k;
                        if (index >= children.Count) return;
                        if (children[index] is not GlyphControl glyph) continue;

                        glyph.Arrange(new LayoutRect(pen + glyph.bearingX, baselineY - glyph.ascent,
                            glyph.DesiredSize.X, glyph.DesiredSize.Y));
                        pen += glyph.advance;
                    }
                }
            }
        }

        #region ---- caret geometry ----
        // Read by the document when it resolves a caret across runs sharing a visual line.
        internal BlockLayout Layout => layout;

        // Local point to the character slot it belongs to.
        public int OffsetAt(float localX, float localY)
        {
            if (layout == null) return 0;

            int lineIndex = LineAt(localY);
            TextLine line = layout.lines[lineIndex];
            TextMeasurer.Run run = new TextMeasurer.Run(text ?? string.Empty, fontName, fontSize);
            float pen = lineIndex == 0 ? firstLineOffset : 0f;

            foreach (LineSegment segment in line.segments)
            {
                for (int i = 0; i < segment.charCount; i++)
                {
                    float advance = TextMeasurer.MeasureAdvance(text[segment.charStart + i], run, metrics);
                    if (localX < pen + advance * 0.5f) return segment.charStart + i;
                    pen += advance;
                }
            }

            LineSegment lastSegment = line.segments[line.segments.Count - 1];
            return lastSegment.charStart + lastSegment.charCount;
        }

        // Character slot to a local caret rect.
        public CaretGeometry CaretAt(int offset)
        {
            if (layout == null) return new CaretGeometry(0f, 0f, fontSize * lineHeight, 0f);

            for (int i = 0; i < layout.lines.Count; i++)
            {
                TextLine line = layout.lines[i];
                bool isLastLine = i == layout.lines.Count - 1;
                float x = i == 0 ? firstLineOffset : 0f;

                for (int s = 0; s < line.segments.Count; s++)
                {
                    LineSegment segment = line.segments[s];
                    int end = segment.charStart + segment.charCount;

                    bool endBelongsHere = isLastLine || s < line.segments.Count - 1;
                    bool holds = offset >= segment.charStart
                                 && (offset < end || (endBelongsHere && offset == end));

                    if (!holds)
                    {
                        x += segment.width;
                        continue;
                    }

                    TextMeasurer.Run run = new TextMeasurer.Run(text ?? string.Empty, fontName, fontSize);
                    for (int c = segment.charStart; c < offset; c++)
                        x += TextMeasurer.MeasureAdvance(text[c], run, metrics);

                    return new CaretGeometry(x, line.top, line.height, line.baseline);
                }
            }

            TextLine last = layout.lines[layout.lines.Count - 1];
            return new CaretGeometry(last.width, last.top, last.height, last.baseline);
        }

        // Rightmost line not past y; clamps at both ends.
        private int LineAt(float y)
        {
            int low = 0;
            int high = layout.lines.Count - 1;

            while (low < high)
            {
                int mid = (low + high + 1) / 2;
                if (layout.lines[mid].top <= y) low = mid;
                else high = mid - 1;
            }
            return low;
        }
        #endregion

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