using ArctisAurora.Core.Registry;

namespace ArctisAurora.Core.UI
{
    [A_XSDType("Button", "UI")]
    public class ButtonControl : PanelControl
    {
        // state tints; an unset one falls back to the state below it
        [A_XSDElementProperty("HoverColorHex", "UI", "Control color while the pointer is over the button.")]
        public string? hoverColorHex;
        [A_XSDElementProperty("PressColorHex", "UI", "Control color while the button is held.")]
        public string? pressColorHex;

        private string? restColorHex;
        private uint restPaint = Palettes.Inline("#8C8C8C");
        private bool hovered;
        private bool pressed;

        // The authored colour, not the shown one — the state picks which of the three is painted.
        public override string colorHex
        {
            get => restColorHex ?? base.colorHex;
            set
            {
                base.colorHex = value;
                restColorHex = value;
                restPaint = Palettes.Inline(value);
                ApplyState();
            }
        }

        public ButtonControl()
        {
            ApplyState();
        }

        protected override void ApplyRole(PaletteDefinition scheme, uint ground)
        {
            if (role == PaletteRole.None) return;
            restPaint = RolePaint(scheme, ground);
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

        // An authored state colour wins; an authored rest falls back to itself, a palette rest steps.
        private void ApplyState()
        {
            string? stateHex = pressed ? pressColorHex ?? hoverColorHex : hovered ? hoverColorHex : null;
            bool fromPalette = restColorHex == null && palette != null && role != PaletteRole.None;
            uint state = pressed ? 2u : hovered ? 1u : 0u;

            SetPaint(stateHex != null ? Palettes.Inline(stateHex)
                   : fromPalette ? Palettes.Step(palette!, restPaint, state)
                   : restPaint);
            visual.alpha = fromPalette && role == PaletteRole.Clear && state == 0 ? 0f : alpha;
            RepaintChildren();
        }
    }
}
