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
        // Bubbles instead of beginning its own edit; the editor places the caret.
        public override void ResolveOnClick(Silk.NET.Maths.Vector2D<float> oldPos, Silk.NET.Maths.Vector2D<float> delta)
        {
            if (parent is VulkanControl parentControl)
                parentControl.ResolveOnClick(oldPos, delta);
        }

        public TextRun Clone() => new TextRun
        {
            bold = bold,
            italic = italic,
            strikethrough = strikethrough,
            controlColorHex = controlColorHex,
            gradient = gradient,
            fontName = fontName,
            fontSize = fontSize,
            stylingType = stylingType,
            text = text
        };
    }
}
