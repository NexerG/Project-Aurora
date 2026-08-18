using ArctisAurora.Core.Registry;

namespace ArctisAurora.Core.UISystem.Controls.Text
{
    // Text that is drawn and never edited — a button's caption, a row in a list. Not a
    // TextInputControl, whose click handler begins an edit and consumes the click.
    [A_XSDType("Label", "UI", typeof(GlyphControl))]
    public class LabelControl : TextControl
    {
        public override bool canBeActiveContext => false;

        public LabelControl()
        {
            BubbleAll();
        }

        public override void BeginEdit() { }

        public override void CommitEdit() { }

        public override void CancelEdit() { }

        public override void WriteChar(char c) { }
    }
}
