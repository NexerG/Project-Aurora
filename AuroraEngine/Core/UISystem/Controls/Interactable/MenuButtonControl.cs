using ArctisAurora.Core.Registry;
using ArctisAurora.Core.UISystem.Controls.Text;
using Silk.NET.Maths;

namespace ArctisAurora.Core.UISystem.Controls.Interactable
{
    // A menu bar entry: a button whose named menu drops on press rather than on right click, and
    // which leaves the active control alone so the entries still act on whatever was focused.
    [A_XSDType("MenuButton", "UI")]
    public class MenuButtonControl : ButtonControl
    {
        // caption
        private const int captionSize = 13;
        private const string captionColorHex = "#CCCCCC";

        private readonly LabelControl label = new LabelControl
        {
            fontSize = captionSize,
            controlColorHex = captionColorHex
        };

        [A_XSDElementProperty("Caption", "UI", "Text shown on the button.")]
        public string Caption
        {
            get => label.text;
            set => label.text = value;
        }

        public override bool takesActiveControl => false;

        public MenuButtonControl()
        {
            AddChild(label);
        }

        public override void ResolveOnClick(Vector2D<float> oldPos, Vector2D<float> delta)
        {
            base.ResolveOnClick(oldPos, delta);
            ContextMenus.Open(this);
        }
    }
}
