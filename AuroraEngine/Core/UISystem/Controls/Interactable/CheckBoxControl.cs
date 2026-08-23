using ArctisAurora.Core.Registry;

namespace ArctisAurora.Core.UISystem.Controls.Interactable
{
    // A box that fills when set. The mark is a child panel rather than a glyph, so it does not
    // depend on the font atlas carrying a tick.
    [A_XSDType("CheckBox", "UI")]
    public class CheckBoxControl : ButtonControl
    {
        private const string markColorHex = "#D8D8D8";

        private readonly PanelControl mark = new PanelControl
        {
            controlColorHex = markColorHex,
            preferredWidth = 10,
            preferredHeight = 10,
            cornerRadius = new CornerRadii(2)
        };

        public Action<bool>? onChanged;

        private bool _isChecked;

        public bool isChecked
        {
            get => _isChecked;
            set
            {
                _isChecked = value;
                if (value) mark.Show(); else mark.Hide();
            }
        }

        public CheckBoxControl()
        {
            preferredWidth = 18;
            preferredHeight = 18;
            cornerRadius = new CornerRadii(3);

            AddChild(mark);
            mark.Hide();
        }

        public override void ResolveOnRelease()
        {
            base.ResolveOnRelease();
            isChecked = !isChecked;
            onChanged?.Invoke(isChecked);
        }
    }
}
