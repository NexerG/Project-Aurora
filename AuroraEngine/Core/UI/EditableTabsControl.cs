using ArctisAurora.Core.Registry;

namespace ArctisAurora.Core.UI
{
    // A TabView whose captions rename in place on a double click. What the new name does is the
    // tab's business, not this control's.
    [A_XSDType("EditableTabs", "UI")]
    public class EditableTabsControl : TabViewControl
    {
        protected override Control BuildCaption(TabItemControl item, TabStripButtonControl tab)
        {
            EditableLabelControl caption = new EditableLabelControl
            {
                text = item.header,
                fontSize = captionSize,
                horizontalPosition = 0f
            };
            caption.PaintText(tabInkColorHex, PaletteRole.Ink);

            tab.RegisterOnTap(e =>
            {
                if (e.tapCount != 2) return false;
                BeginRename(item, caption);
                return true;
            });
            return caption;
        }

        protected internal override TabViewControl NewOfSameKind() => new EditableTabsControl();

        private static void BeginRename(TabItemControl item, EditableLabelControl caption)
        {
            if (item.onRename == null) return;
            caption.BeginEdit(name => item.onRename(name));
        }
    }
}
