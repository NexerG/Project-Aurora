using ArctisAurora.Core.Registry;
using Silk.NET.Maths;

namespace ArctisAurora.Core.UISystem.Controls.Interactable
{
    [A_XSDType("Button", "UI", AllowedChildren = typeof(IXMLChild_UI), MaxChildren = 1)]
    public class ButtonControl : PanelControl
    {
        // state tints; an unset one falls back to the state below it
        [A_XSDElementProperty("HoverColorHex", "UI", "Control color while the pointer is over the button.")]
        public string hoverColorHex;
        [A_XSDElementProperty("PressColorHex", "UI", "Control color while the button is held.")]
        public string pressColorHex;

        private bool hovered;
        private bool pressed;

        public ButtonControl()
        {
            controlColorHex = "#8C8C8C";
        }

        public override void OnStart()
        {
            base.OnStart();
            UpdateControlData();
        }

        public override void ResolveOnEnter()
        {
            hovered = true;
            ApplyState();
            base.ResolveOnEnter();
        }

        public override void ResolveExit()
        {
            hovered = false;
            pressed = false;
            ApplyState();
            base.ResolveExit();
        }

        public override void ResolveOnClick(Vector2D<float> oldPos, Vector2D<float> delta)
        {
            pressed = true;
            ApplyState();
            base.ResolveOnClick(oldPos, delta);
        }

        public override void ResolveOnRelease()
        {
            pressed = false;
            ApplyState();
            base.ResolveOnRelease();
        }

        private void ApplyState()
        {
            string hex = pressed ? pressColorHex ?? hoverColorHex ?? controlColorHex
                       : hovered ? hoverColorHex ?? controlColorHex
                       : controlColorHex;

            controlData.style.tint = new Vector4D<float>(HexToRGB(hex), controlData.style.tint.W);
            UpdateControlData();
        }
    }
}
