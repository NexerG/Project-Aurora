using ArctisAurora.Core.Registry;
using ArctisAurora.Core.UISystem.Controls.Text.Editing;

namespace ArctisAurora.Core.UISystem.Controls.Text.Document
{
    // A contiguous run of text sharing one style — the leaf content of a block. It is a
    // TextInputControl: style (bold/italic/strikethrough/colour/font), text and glyph rendering all
    // already live there, so the run reuses them instead of re-declaring them. The view maps one
    // TextRun to one styled run control.
    [A_XSDType("Run", "UI")]
    public class TextRun : TextInputControl
    {
        public TextRun Clone() => new TextRun
        {
            bold = bold,
            italic = italic,
            strikethrough = strikethrough,
            runColorHex = runColorHex,
            fontName = fontName,
            fontSize = fontSize,
            text = text
        };
    }
}
