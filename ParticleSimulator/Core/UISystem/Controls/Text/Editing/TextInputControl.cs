using ArctisAurora.Core.Registry;
using Silk.NET.Maths;

namespace ArctisAurora.Core.UISystem.Controls.Text.Editing
{
    [A_XSDType("TextInput", "UI", typeof(GlyphControl))]
    public class TextInputControl : TextControl, IContext
    {
        #region ---- style ----
        [A_XSDElementProperty("Bold", "TextEditor")]
        public bool bold { get; set; } = false;

        [A_XSDElementProperty("Italic", "TextEditor")]
        public bool italic { get; set; } = false;

        [A_XSDElementProperty("Strikethrough", "TextEditor")]
        public bool strikethrough { get; set; } = false;

        [A_XSDElementProperty("RunColorHex", "TextEditor")]
        public string runColorHex { get; set; } = "#FFFFFF";

        [A_XSDElementProperty("FontName", "TextEditor")]
        public string fontName { get; set; } = "default";
        #endregion

        #region ---- wrapping state ----
        // Set by parent TextBlockControl before Measure — how far into the line we start
        public float firstLineOffset;

        // Set during Measure — where our last line ends, so parent knows where to place next sibling
        public float lastLineEndX;
        public float lastLineHeight;
        public float minLineHeight = 0f;
        #endregion

        #region ---- cursor ----
        public int cursorPosition = 0;
        #endregion

        public TextInputControl() 
        {
            minLineHeight = 50f;
        }

        #region ---- EDITING ----
        public override void BeginEdit()
        {
            isEditing = true;
            Console.WriteLine("SET EDITING");
        }

        public override void CancelEdit()
        {
            isEditing = false;
        }

        public override void CommitEdit()
        {
            isEditing = false;
        }

        public override void WriteChar(char c)
        {
            if (c == '\0') return;
            InsertGlyph(cursorPosition, c);
            cursorPosition++;
        }

        public void InsertAt(int charOffset, string insert)
        {
            text = text[..charOffset] + insert + text[charOffset..];
        }

        public void DeleteAt(int charOffset, int count)
        {
            if (charOffset < 0 || charOffset + count > text.Length) return;
            text = text[..charOffset] + text[(charOffset + count)..];
            if (cursorPosition > charOffset)
                cursorPosition = Math.Max(charOffset, cursorPosition - count);
        }
        public void Backspace()
        {
            if (cursorPosition <= 0) return;
            cursorPosition--;
            text = text[..cursorPosition] + text[(cursorPosition + 1)..];
        }

        public void Delete()
        {
            if (cursorPosition >= text.Length) return;
            text = text[..cursorPosition] + text[(cursorPosition + 1)..];
        }

        public void MoveCursorLeft()
        {
            if (cursorPosition > 0) cursorPosition--;
        }

        public void MoveCursorRight()
        {
            if (cursorPosition < text.Length) cursorPosition++;
        }

        public void MoveCursorHome()
        {
            cursorPosition = 0;
        }

        public void MoveCursorEnd()
        {
            cursorPosition = text.Length;
        }
        #endregion

        #region ---- style helpers ----
        public bool StyleEquals(TextInputControl other)
        {
            return bold == other.bold
                && italic == other.italic
                && strikethrough == other.strikethrough
                && fontSize == other.fontSize
                && runColorHex == other.runColorHex
                && fontName == other.fontName;
        }

        // Split at charOffset. This keeps [0..charOffset), returns new control with [charOffset..end).
        public TextInputControl SplitAt(int charOffset)
        {
            TextInputControl right = new TextInputControl();
            right.bold = bold;
            right.italic = italic;
            right.strikethrough = strikethrough;
            right.runColorHex = runColorHex;
            right.fontName = fontName;
            right.fontSize = fontSize;
            right.text = text[charOffset..];

            text = text[..charOffset];
            return right;
        }
        #endregion

        public override void ResolveOnClick(Vector2D<float> oldPos, Vector2D<float> delta)
        {
            BeginEdit();
            //cursorPosition = HitTestCursor(oldPos);
            cursorPosition = text.Length;
        }

        private int HitTestCursor(Vector2D<float> pos)
        {
            float bestDist = float.MaxValue;
            int bestIndex = text.Length;

            for (int i = 0; i < children.Count; i++)
            {
                if (children[i] is not VulkanControl glyph) continue;

                float glyphCenterX = glyph.arrangedRect.x + glyph.arrangedRect.width * 0.5f;
                float dist = MathF.Abs(pos.X - glyphCenterX);

                // Also check vertical — pick the right line
                if (pos.Y >= glyph.arrangedRect.y && pos.Y <= glyph.arrangedRect.y + glyph.arrangedRect.height)
                {
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        bestIndex = pos.X < glyphCenterX ? i : i + 1;
                    }
                }
            }
            return Math.Min(bestIndex, text.Length);
        }

        public void OnContextAdded()
        {
        }

        public void OnContextRemoved()
        {
            CommitEdit();
        }

        public override Vector2D<float> Measure(Vector2D<float> availableSize)
        {
            float fullWidth = availableSize.X;
            float cursorX = firstLineOffset;
            float lineHeight = 0f;
            float totalHeight = 0f;
            float maxWidth = 0f;
            bool firstLine = true;

            for (int i = 0; i < children.Count; i++)
            {
                if (children[i] is not VulkanControl glyph) continue;

                Vector2D<float> desired = glyph.Measure(new Vector2D<float>(float.MaxValue, float.MaxValue));

                // Would this glyph exceed current line?
                if (cursorX + desired.X > fullWidth && cursorX > (firstLine ? firstLineOffset : 0))
                {
                    // Line break
                    if (cursorX > maxWidth) maxWidth = cursorX;
                    totalHeight += MathF.Max(lineHeight, minLineHeight);
                    cursorX = 0f;
                    lineHeight = 0f;
                    firstLine = false;
                }

                cursorX += desired.X;
                if (desired.Y > lineHeight) lineHeight = desired.Y;
            }

            // Last line
            if (cursorX > maxWidth) maxWidth = cursorX;
            totalHeight += MathF.Max(lineHeight, minLineHeight);
            lastLineEndX = cursorX;
            lastLineHeight = lineHeight;

            float w = preferredWidth > 0 ? MathF.Max(preferredWidth, maxWidth) : maxWidth;
            float h = preferredHeight > 0 ? MathF.Max(preferredHeight, totalHeight) : totalHeight;

            DesiredSize = new Vector2D<float>(w, h);
            isMeasureDirty = false;
            return DesiredSize;
        }

        public override void Arrange(LayoutRect finalRect)
        {
            arrangedRect = finalRect;
            transform.SetWorldPosition(new Vector3D<float>(
                finalRect.x + finalRect.width / 2f,
                finalRect.y + finalRect.height / 2f,
                parent != null ? parent.transform.GetEntityPosition().Z + 0.001f : transform.GetEntityPosition().Z));
            transform.SetWorldScale(new Vector3D<float>(finalRect.width, finalRect.height, 1));

            ClipRect = parent is VulkanControl p
                ? (clipOutOfBounds ? LayoutRect.Intersect(finalRect, p.ClipRect) : p.ClipRect)
                : finalRect;

            float fullWidth = finalRect.width;
            float cursorX = firstLineOffset;
            float cursorY = finalRect.y;
            float lineHeight = 0f;
            bool firstLine = true;

            for (int i = 0; i < children.Count; i++)
            {
                if (children[i] is not VulkanControl glyph) continue;

                float glyphW = glyph.DesiredSize.X;
                float glyphH = glyph.DesiredSize.Y;

                if (cursorX + glyphW > fullWidth && cursorX > (firstLine ? firstLineOffset : 0))
                {
                    cursorY += MathF.Max(lineHeight, minLineHeight);
                    cursorX = 0f;
                    lineHeight = 0f;
                    firstLine = false;
                }

                float cx = finalRect.x + cursorX;
                float cy = cursorY;
                glyph.Arrange(new LayoutRect(cx, cy, glyphW, glyphH));

                cursorX += glyphW;
                if (glyphH > lineHeight) lineHeight = glyphH;
            }

            isArrangeDirty = false;
        }
    }
}
