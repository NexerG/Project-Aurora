using ArctisAurora.Core.Registry;
using System.Numerics;

namespace ArctisAurora.Core.UI
{
    // A caption that drops its options as a menu under it.
    [A_XSDType("Dropdown", "UI")]
    public class DropdownControl : ButtonControl
    {
        // caption
        private const int captionSize = 13;
        private const string captionColorHex = "#E6E6E6";

        private readonly LabelControl label = new LabelControl
        {
            fontSize = captionSize,
            colorHex = captionColorHex
        };

        public IReadOnlyList<string> options = Array.Empty<string>();
        public Action<string>? onPicked;

        public string selected
        {
            get => label.text;
            set => label.text = value;
        }

        public DropdownControl()
        {
            AddChild(label);
        }

        public override bool OnPointerRelease(PointerEvent e)
        {
            base.OnPointerRelease(e);
            if (e.button != PointerEvent.leftButton) return true;

            List<ContextMenuEntry> entries = new List<ContextMenuEntry>();
            foreach (string option in options)
                entries.Add(new ContextMenuButton(option, () => Pick(option)));

            ContextMenus.Open(entries, this,
                new Vector2(arrangedRect.x, arrangedRect.y + arrangedRect.height));
            return true;
        }

        private void Pick(string option)
        {
            selected = option;
            onPicked?.Invoke(option);
        }
    }
}
