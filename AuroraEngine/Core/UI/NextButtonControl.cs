using ArctisAurora.Core.Registry;

namespace ArctisAurora.Core.UI
{
    [A_XSDType("NextButton", "UI")]
    public class NextButtonControl : NextPanelControl
    {
        // state tints; an unset one falls back to the state below it
        [A_XSDElementProperty("HoverColorHex", "UI", "Control color while the pointer is over the button.")]
        public string? hoverColorHex;
        [A_XSDElementProperty("PressColorHex", "UI", "Control color while the button is held.")]
        public string? pressColorHex;

        private string restColorHex = "#8C8C8C";
        private bool hovered;
        private bool pressed;

        // The authored colour, not the shown one — the state picks which of the three is painted.
        public override string colorHex
        {
            get => restColorHex;
            set
            {
                restColorHex = value;
                ApplyState();
            }
        }

        public NextButtonControl()
        {
            ApplyState();
        }

        public override bool OnPointerEnter(PointerEvent e)
        {
            hovered = true;
            ApplyState();
            base.OnPointerEnter(e);
            return true;
        }

        public override bool OnPointerExit(PointerEvent e)
        {
            hovered = false;
            pressed = false;
            ApplyState();
            base.OnPointerExit(e);
            return true;
        }

        public override bool OnPointerPress(PointerEvent e)
        {
            pressed = true;
            ApplyState();
            base.OnPointerPress(e);
            return true;
        }

        public override bool OnPointerRelease(PointerEvent e)
        {
            pressed = false;
            ApplyState();
            if (e.button == PointerEvent.leftButton) base.OnPointerRelease(e);
            return true;
        }

        private void ApplyState() => base.colorHex = pressed ? pressColorHex ?? hoverColorHex ?? restColorHex
                                                  : hovered ? hoverColorHex ?? restColorHex
                                                  : restColorHex;
    }
}
