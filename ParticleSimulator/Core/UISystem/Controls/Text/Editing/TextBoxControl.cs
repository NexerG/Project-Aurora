using ArctisAurora.Core.Registry;
using Silk.NET.Maths;

namespace ArctisAurora.Core.UISystem.Controls.Text.Editing
{
    [A_XSDType("TextBox", "UI", typeof(GlyphControl))]
    public class TextBoxControl : TextControl
    {
        public override void BeginEdit()
        {
        }

        public override void CancelEdit()
        {
            int editLength = newEdit.Length;
            text = text[..editLength];
        }

        public override void CommitEdit()
        {
            newEdit = string.Empty;
        }

        public override void WriteChar(char c)
        {
            newEdit += c;
            text += c;
        }
    }
}