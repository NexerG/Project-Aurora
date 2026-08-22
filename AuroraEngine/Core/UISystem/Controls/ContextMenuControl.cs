using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Registry.Assets;
using ArctisAurora.Core.UISystem.Controls.Containers;
using ArctisAurora.Core.UISystem.Controls.Text;
using ArctisAurora.EngineWork.Registry;
using ArctisAurora.EngineWork.Rendering;
using Silk.NET.Maths;

namespace ArctisAurora.Core.UISystem.Controls
{
    // An open context menu: a translucent ground over a column of entries. The column is rebuilt on
    // every open, because the entries are composed per right click and never repeat.
    //
    // It hosts itself. This one floats in the window the right click came from; the windowed subclass
    // puts the same column in a window of its own.
    //
    // Nothing here is sized. The entries measure to their widest caption and the menu to them.
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

        public bool isOpen { get; private set; }

        public ContextMenuControl()
        {
            controlColorHex = groundColorHex;
            alpha = 0.92f;
            padding = new Thickness(4);

            column.orientation = StackPanelControl.Orientation.Vertical;
            column.maskAsset = AssetRegistries.GetAsset<TextureAsset>("invisible");
            AddChild(column);
        }

        // False means this control offered nothing, which is what tells the walk to keep going up.
        public bool Open(VulkanControl owner)
        {
            // right-clicking the menu itself is not a request for another one, and there is nothing
            // above it worth walking to either
            if (Owns(owner)) return true;

            List<ContextEntry> entries = ContextMenus.Compose(owner);
            if (entries.Count == 0) return false;

            RenderWindow source = RenderWindow.Of(owner);
            if (source == null) return false;

            if (isOpen) Detach();

            Fill(entries);
            Measure(new Vector2D<float>(float.MaxValue, float.MaxValue));
            Attach(source, source.ui.ToDesignSpace(source.mousePos));

            isOpen = true;
            return true;
        }

        public void Close()
        {
            if (!isOpen) return;

            Detach();
            isOpen = false;
        }

        // Dismissal is the host's business, and a menu living in the window it was opened from has
        // nothing to watch — the press that lands outside it takes it down.
        public virtual void Tick() { }

        protected virtual void Attach(RenderWindow source, Vector2D<float> point) =>
            source.ui.uiRoot.AddOverlay(this, point);

        protected virtual void Detach() => (parent as WindowControl)?.RemoveOverlay();

        // Whether a control is this menu or sits inside it.
        private bool Owns(VulkanControl control)
        {
            while (control != null)
            {
                if (ReferenceEquals(control, this)) return true;
                control = control.parent as VulkanControl;
            }
            return false;
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
                    Close();
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
