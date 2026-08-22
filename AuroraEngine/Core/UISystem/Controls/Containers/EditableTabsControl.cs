using ArctisAurora.Core.Registry;
using ArctisAurora.Core.UISystem.Controls.Text;

namespace ArctisAurora.Core.UISystem.Controls.Containers
{
    // A TabView whose captions rename in place on a double click. What the new name does is the
    // tab's business, not this control's.
    [A_XSDType("EditableTabs", "UI", AllowedChildren = typeof(IXMLChild_UI))]
    public class EditableTabsControl : TabViewControl
    {
        private const string captionFieldColorHex = "#3A3A3A";

        protected override VulkanControl BuildCaption(TabItemControl item, TabStripButtonControl tab)
        {
            EditableLabelControl caption = new EditableLabelControl
            {
                text = item.header,
                fontSize = captionSize,
                horizontalPosition = 0f,
                fieldColorHex = captionFieldColorHex
            };

            tab.RegisterOnDoubleClick(() => BeginRename(item, caption));
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
