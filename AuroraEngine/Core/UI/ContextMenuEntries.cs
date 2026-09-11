using ArctisAurora.Core.Registry;

namespace ArctisAurora.Core.UI
{
    // A menu document's root: what a control naming it offers on right click.
    [A_XSDType("NextContextMenu", "UI", AllowedChildren = typeof(ContextMenuEntry))]
    public class ContextMenu
    {
        public readonly List<ContextMenuEntry> entries = new List<ContextMenuEntry>();
    }

    [A_XSDType("NextContextEntry", "UI", isAbstract: true)]
    public abstract class ContextMenuEntry { }

    [A_XSDType("NextContextButton", "UI")]
    public class ContextMenuButton : ContextMenuEntry
    {
        [A_XSDElementProperty("Text", "UI", "The caption shown on the row.")]
        public string text = string.Empty;

        [A_XSDElementProperty("Action", "UI", "Runs when the row is clicked.")]
        public Action? action;

        public ContextMenuButton() { }

        public ContextMenuButton(string text, Action action)
        {
            this.text = text;
            this.action = action;
        }
    }

    [A_XSDType("NextContextLine", "UI")]
    public class ContextMenuLine : ContextMenuEntry { }

    [A_XSDType("NextContextSubmenu", "UI", AllowedChildren = typeof(ContextMenuEntry))]
    public class ContextMenuSubmenu : ContextMenuEntry
    {
        [A_XSDElementProperty("Text", "UI", "The caption shown on the row.")]
        public string text = string.Empty;

        public readonly List<ContextMenuEntry> entries = new List<ContextMenuEntry>();
    }
}
