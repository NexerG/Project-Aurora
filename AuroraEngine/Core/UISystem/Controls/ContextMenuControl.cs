using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Registry.Assets;
using ArctisAurora.Core.UISystem.Controls.Containers;
using ArctisAurora.Core.UISystem.Controls.Text;
using ArctisAurora.EngineWork.Registry;

namespace ArctisAurora.Core.UISystem.Controls
{
    // An open context menu: a translucent ground over a column of entries. The column is rebuilt on
    // every open, because the entries are composed per right click and never repeat.
    //
    // Nothing here is sized. The entries measure to their widest caption and the menu to them, which
    // is what the window it opens in is then sized to.
    public class ContextMenuControl : HintControl
    {
        // entry metrics
        private const int itemHeight = 26;
        private const int captionSize = 13;
        private const float captionInset = 10f;
        private const float separatorInset = 3f;

        // entry palette
        private const string groundColorHex = "#1E1E1E";
        private const string captionColorHex = "#E6E6E6";
        private const string disabledCaptionColorHex = "#6E6E6E";
        private const string itemHoverColorHex = "#3A3A3A";
        private const string itemPressColorHex = "#4A4A4A";
        private const string separatorColorHex = "#3A3A3A";

        private readonly StackPanelControl column = new StackPanelControl();

        // Raised before an entry fires, so whatever is showing the menu can take it down first.
        internal Action onEntryInvoked;

        public ContextMenuControl()
        {
            controlColorHex = groundColorHex;
            alpha = 0.92f;
            padding = new Thickness(4);

            column.orientation = StackPanelControl.Orientation.Vertical;
            column.maskAsset = AssetRegistries.GetAsset<TextureAsset>("invisible");
            AddChild(column);
        }

        public void Fill(IReadOnlyList<ContextEntry> entries)
        {
            foreach (Entity child in column.children.ToArray())
                child.Destroy();

            foreach (ContextEntry entry in entries)
            {
                if (entry.separatorBefore) column.AddChild(BuildSeparator());
                column.AddChild(BuildItem(entry));
            }

            InvalidateLayout();
        }

        // preferredWidth stays 0 so the row measures to its caption and then stretches to the widest
        // one when the column arranges it.
        private VulkanControl BuildItem(ContextEntry entry)
        {
            ContextMenuItemControl item = new ContextMenuItemControl
            {
                preferredHeight = itemHeight,
                padding = new Thickness(captionInset, 0),
                controlColorHex = groundColorHex,
                hoverColorHex = itemHoverColorHex,
                pressColorHex = itemPressColorHex,
                alpha = this.alpha,
                enabled = entry.enabled
            };
            if (entry.enabled)
                item.RegisterOnRelease(() =>
                {
                    onEntryInvoked?.Invoke();
                    entry.invoke?.Invoke();
                });

            item.AddChild(new LabelControl
            {
                text = entry.caption,
                fontSize = captionSize,
                horizontalPosition = 0f,
                controlColorHex = entry.enabled ? captionColorHex : disabledCaptionColorHex
            });

            return item;
        }

        private VulkanControl BuildSeparator() => new PanelControl
        {
            preferredHeight = 1,
            margin = new Thickness(0, separatorInset),
            controlColorHex = separatorColorHex,
            alpha = this.alpha,
            hitTestable = false
        };
    }
}
