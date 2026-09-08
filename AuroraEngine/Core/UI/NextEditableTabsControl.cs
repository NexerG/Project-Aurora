using ArctisAurora.Core.Registry;

namespace ArctisAurora.Core.UI
{
    // A NextTabView whose captions rename in place on a double click. What the new name does is the
    // tab's business, not this control's.
    [A_XSDType("NextEditableTabs", "UI")]
    public class NextEditableTabsControl : NextTabViewControl
    {
        private const string captionFieldColorHex = "#3A3A3A";

        protected override Control BuildCaption(NextTabItemControl item, NextTabStripButtonControl tab)
        {
            NextEditableLabelControl caption = new NextEditableLabelControl
            {
                textColorHex = tabInkColorHex,
                text = item.header,
                fontSize = captionSize,
                horizontalPosition = 0f,
                fieldColorHex = captionFieldColorHex
            };

            tab.RegisterOnTap(e =>
            {
                if (e.tapCount != 2) return false;
                BeginRename(item, caption);
                return true;
            });
            return caption;
        }

        protected internal override NextTabViewControl NewOfSameKind() => new NextEditableTabsControl();

        private static void BeginRename(NextTabItemControl item, NextEditableLabelControl caption)
        {
            if (item.onRename == null) return;
            caption.BeginEdit(name => item.onRename(name));
        }
    }
}
