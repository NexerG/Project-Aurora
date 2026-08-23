using ArctisAurora.Core.Registry;
using ArctisAurora.Core.UISystem.Controls.Text;
using Silk.NET.Maths;

namespace ArctisAurora.Core.UISystem.Controls.Interactable
{
    // A caption that drops its options through the context menu, so wherever a windowed menu is in
    // use the list escapes the parent's clip rect for free.
    [A_XSDType("Dropdown", "UI")]
    public class DropdownControl : ButtonControl
    {
        // caption
        private const int captionSize = 13;
        private const string captionColorHex = "#E6E6E6";

        private readonly LabelControl label = new LabelControl
        {
            fontSize = captionSize,
            controlColorHex = captionColorHex
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

        public override void ResolveOnRelease()
        {
            base.ResolveOnRelease();

            List<ContextEntry> entries = new List<ContextEntry>();
            foreach (string option in options)
            {
                string picked = option;
                entries.Add(new ContextEntry(picked, () => Pick(picked), true, false));
            }

            ContextMenus.OpenList(this, entries,
                new Vector2D<float>(arrangedRect.x, arrangedRect.y + arrangedRect.height));
        }

        private void Pick(string option)
        {
            selected = option;
            onPicked?.Invoke(option);
        }
    }
}
