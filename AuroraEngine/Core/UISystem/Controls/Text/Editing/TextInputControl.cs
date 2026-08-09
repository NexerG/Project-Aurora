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
        #endregion

        public TextInputControl()
        {
        }

        #region ---- EDITING ----
        public override void BeginEdit()
        {
            isEditing = true;
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
                && controlColorHex == other.controlColorHex
                && fontName == other.fontName;
        }

        // Split at charOffset. This keeps [0..charOffset), returns new control with [charOffset..end).
        public TextInputControl SplitAt(int charOffset)
        {
            TextInputControl right = new TextInputControl();
            right.bold = bold;
            right.italic = italic;
            right.strikethrough = strikethrough;
            right.controlColorHex = controlColorHex;
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
    }
}
