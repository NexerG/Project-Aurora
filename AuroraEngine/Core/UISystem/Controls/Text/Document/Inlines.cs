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
        // A size chosen for this run rather than resolved from the styling scheme. ApplyLayout leaves
        // those alone, which is the whole difference between the px field and a heading.
        [A_XSDElementProperty("FontSizeAuthored", "UI", "The run's FontSize was chosen, not taken from the styling scheme.")]
        public bool fontSizeAuthored { get; set; } = false;

        public TextRun()
        {
            bubbleMultiClick = true;
        }

        // Bubbles instead of beginning its own edit; the editor places the caret.
        public override void ResolveOnClick(Silk.NET.Maths.Vector2D<float> oldPos, Silk.NET.Maths.Vector2D<float> delta)
        {
            if (parent is VulkanControl parentControl)
                parentControl.ResolveOnClick(oldPos, delta);
        }

        public override void OnContextAdded(string context)
        {
            base.OnContextAdded(context);
            if (context == "ActiveControl") Owner?.RegainFocus();
        }

        public override void OnContextRemoved(string context)
        {
            base.OnContextRemoved(context);
            if (context == "ActiveControl") Owner?.LoseFocus();
        }

        private DocumentControl? Owner => DocumentControl.BlockOf(this)?.parent as DocumentControl;

        public TextRun Clone() => new TextRun
        {
            bold = bold,
            italic = italic,
            strikethrough = strikethrough,
            controlColorHex = controlColorHex,
            gradient = gradient,
            fontName = fontName,
            fontSize = fontSize,
            fontSizeAuthored = fontSizeAuthored,
            stylingType = stylingType,
            text = text
        };

        // Keeps [0..offset) and returns a run of the same style holding the rest. Hides
        // TextInputControl.SplitAt, which is older dead code returning the wrong type for a document.
        public new TextRun SplitAt(int offset)
        {
            string whole = text ?? string.Empty;

            TextRun right = Clone();
            right.text = whole[offset..];
            text = whole[..offset];
            return right;
        }
    }
}
