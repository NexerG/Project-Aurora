using ArctisAurora.Core.Registry;

namespace ArctisAurora.Core.UI
{
    // A box that fills when set.
    [A_XSDType("CheckBox", "UI")]
    public class CheckBoxControl : ButtonControl
    {
        private readonly PanelControl mark = new PanelControl
        {
            role = PaletteRole.Ink,
            preferredWidth = 10,
            preferredHeight = 10,
            cornerRadius = new CornerRadii(2),
            hitTestable = false
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

        public override bool OnPointerRelease(PointerEvent e)
        {
            base.OnPointerRelease(e);
            if (e.button != PointerEvent.leftButton) return true;

            isChecked = !isChecked;
            onChanged?.Invoke(isChecked);
            return true;
        }
    }
}
