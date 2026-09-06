using ArctisAurora.Core.Registry;

namespace ArctisAurora.Core.UI
{
    // Text that is drawn and never edited — a button's caption, a row in a list.
    [A_XSDType("NextLabel", "UI")]
    public class NextLabelControl : TextRunControl
    {
        public override Control? ActiveContextTarget() => (parent as Control)?.ActiveContextTarget();
    }
}
