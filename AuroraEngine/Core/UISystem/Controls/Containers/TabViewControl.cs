using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Registry.Assets;
using ArctisAurora.Core.UISystem.Controls.Interactable;
using ArctisAurora.Core.UISystem.Controls.Text;
using ArctisAurora.Core.UISystem.Controls.Text.Document;
using ArctisAurora.EngineWork;
using ArctisAurora.EngineWork.Registry;
using ArctisAurora.EngineWork.Rendering;
using Silk.NET.Maths;

namespace ArctisAurora.Core.UISystem.Controls.Containers
{
    // A strip of tabs over one page. Only the active item is measured and arranged; the rest are
    // hidden. Closing a tab destroys it.
    [A_XSDType("TabView", "UI", AllowedChildren = typeof(IXMLChild_UI))]
    public class TabViewControl : AbstractContainerControl
    {
        #region properties
        // strip metrics
        [A_XSDElementProperty("TabHeight", "UI", "Height of the tab strip in pixels.")]
        public float tabHeight = 28f;
        [A_XSDElementProperty("TabWidth", "UI", "Width of a single tab in pixels.")]
        public float tabWidth = 160f;

        // strip palette
        [A_XSDElementProperty("TabColorHex", "UI", "Ground of an inactive tab.")]
        public string tabColorHex = "#2A2A2A";
        [A_XSDElementProperty("ActiveTabColorHex", "UI", "Ground of the active tab.")]
        public string activeTabColorHex = "#1E1E1E";
        [A_XSDElementProperty("TabHoverColorHex", "UI", "Ground of a hovered inactive tab.")]
        public string tabHoverColorHex = "#3A3A3A";

        // tear-off
        [A_XSDElementProperty("TearOffDocument", "UI", "UI document a tab dragged out of every window opens in.")]
        public string tearOffDocument = "";
        #endregion

        // caption and close button geometry
        private const int captionSize = 14;
        private const float captionInset = 8f;
        private const int closeWidth = 20;
        private const int closeCaptionSize = 12;
        private const string closeCaption = "x";
        private const string closeHoverColorHex = "#C42B1E";
        private const string closePressColorHex = "#A82318";

        // torn-off window geometry
        private const uint tearOffWidth = 900;
        private const uint tearOffHeight = 640;
        private static int _tornWindows;

        private readonly StackPanelControl strip = new StackPanelControl();

        public TabItemControl activeItem { get; private set; }

        public TabViewControl()
        {
            strip.orientation = StackPanelControl.Orientation.Horizontal;
            strip.maskAsset = AssetRegistries.GetAsset<TextureAsset>("invisible");
            strip.clipOutOfBounds = true;
            base.AddChild(strip);
        }

        private IEnumerable<TabItemControl> Items
        {
            get
            {
                foreach (Entity e in children)
                    if (e is TabItemControl item) yield return item;
            }
        }

        public override void AddChild(Entity entity)
        {
            if (entity is not TabItemControl item)
                throw new Exception("TabView children must be TabItem controls");

            base.AddChild(item);
            if (activeItem != null) item.Hide();

            RebuildStrip();
            if (activeItem == null) SetActive(item);
        }

        // A tab moved to another view takes its strip button's pending release with it, so an item
        // that is no longer ours is ignored rather than resurrected as the active one.
        public override void RemoveChild(Entity entity)
        {
            bool wasActive = ReferenceEquals(entity, activeItem);
            base.RemoveChild(entity);

            if (entity is not TabItemControl item) return;

            TabItemControl next = wasActive ? Neighbour(item) : activeItem;
            if (wasActive) activeItem = null;

            RebuildStrip();
            if (activeItem == null && next != null) SetActive(next);
            InvalidateLayout();

            CloseIfEmptied();
        }

        // Accepts a tab dragged out of any strip, including our own.
        public override bool ResolveDrop(VulkanControl dropped)
        {
            if (dropped is not TabStripButtonControl button || !button.dragging) return false;

            button.accepted = true;
            if (ReferenceEquals(button.item.parent, this)) return true;

            button.item.SetParent(this);
            SetActive(button.item);
            return true;
        }

        // Moves a tab into a window of its own, built from tearOffDocument and placed at the pointer.
        internal unsafe void TearOff(TabItemControl item)
        {
            if (string.IsNullOrEmpty(tearOffDocument)) return;

            RenderWindow source = RenderWindow.Of(this);
            AGlfwWindow._glfw.GetWindowPos(source.os.handle, out int wx, out int wy);

            RenderWindow torn = Engine.OpenWindow($"tab-{++_tornWindows}", tearOffWidth, tearOffHeight,
                wx + (int)source.mousePos.X, wy + (int)source.mousePos.Y);
            WindowControl root = (WindowControl)ParseXML(tearOffDocument);
            torn.ui.uiRoot = root;

            TabViewControl view = FirstTabView(root);
            if (view == null) return;

            item.SetParent(view);
            view.SetActive(item);
        }

        private static TabViewControl FirstTabView(VulkanControl control)
        {
            if (control is TabViewControl view) return view;
            foreach (Entity child in control.children)
                if (child is VulkanControl childControl && FirstTabView(childControl) is TabViewControl found)
                    return found;
            return null;
        }

        public void SetActive(TabItemControl item)
        {
            if (item != null && !children.Contains(item)) return;
            if (ReferenceEquals(activeItem, item)) return;

            activeItem?.Hide();
            activeItem = item;
            activeItem?.Show();

            ApplyTabColors();
            InvalidateLayout();
        }

        // Saves the page, then tears the whole subtree down — the strip button with it.
        public void CloseTab(TabItemControl item)
        {
            if (item == null || !children.Contains(item)) return;

            if (item.children.Count > 0 && item.children[0] is DocumentEditorControl editor)
                editor.Save();

            TabItemControl next = Neighbour(item);
            if (ReferenceEquals(item, activeItem)) activeItem = null;
            item.Destroy();

            RebuildStrip();
            if (activeItem == null && next != null) SetActive(next);
            InvalidateLayout();

            CloseIfEmptied();
        }

        // A window that exists to hold tabs has nothing left to be once the last one goes. The main
        // window stays — closing that is the application closing.
        private void CloseIfEmptied()
        {
            if (activeItem != null) return;

            RenderWindow window = RenderWindow.Of(this);
            if (window == null || window == Engine.primary) return;

            Engine.CloseWindow(window);
        }

        public TabItemControl FindTab(string tabName)
        {
            foreach (TabItemControl item in Items)
                if (item.name == tabName) return item;
            return null;
        }

        // The tab after this one, or the one before it if it is last.
        private TabItemControl Neighbour(TabItemControl item)
        {
            TabItemControl previous = null;
            bool passed = false;
            foreach (TabItemControl candidate in Items)
            {
                if (ReferenceEquals(candidate, item)) { passed = true; continue; }
                if (passed) return candidate;
                previous = candidate;
            }
            return previous;
        }

        #region ---- strip ----
        private void RebuildStrip()
        {
            foreach (Entity button in strip.children.ToArray())
                button.Destroy();

            foreach (TabItemControl item in Items)
                strip.AddChild(BuildTab(item));

            ApplyTabColors();
        }

        // The row tiles the tab exactly and the wrapper carries the caption inset, so no container
        // inside a tab has bare area a press can land on.
        private VulkanControl BuildTab(TabItemControl item)
        {
            TabStripButtonControl tab = new TabStripButtonControl
            {
                preferredWidth = (int)tabWidth,
                preferredHeight = (int)tabHeight,
                hoverColorHex = tabHoverColorHex,
                pressColorHex = tabHoverColorHex,
                item = item,
                owner = this
            };
            tab.RegisterOnRelease(() => SetActive(item));

            StackPanelControl row = new StackPanelControl
            {
                orientation = StackPanelControl.Orientation.Horizontal,
                maskAsset = AssetRegistries.GetAsset<TextureAsset>("invisible")
            };
            row.BubbleAll();

            // preferredHeight pins the cross axis: StackPanel probes a star child at main-axis 0,
            // which wraps the caption one character per line, and maxCross keeps that height.
            PanelControl wrapper = new PanelControl
            {
                widthStar = 1f,
                preferredHeight = (int)tabHeight,
                padding = new Thickness(0, 0, 0, captionInset),
                maskAsset = AssetRegistries.GetAsset<TextureAsset>("invisible")
            };
            wrapper.BubbleAll();
            wrapper.AddChild(new LabelControl
            {
                text = item.header,
                fontSize = captionSize,
                horizontalPosition = 0f
            });

            row.AddChild(wrapper);
            row.AddChild(BuildCloseButton(item));
            tab.AddChild(row);
            return tab;
        }

        // Enter and exit bubble so the tab keeps its hover tint; release does not, so closing never
        // also activates.
        private VulkanControl BuildCloseButton(TabItemControl item)
        {
            ButtonControl close = new ButtonControl
            {
                preferredWidth = closeWidth,
                preferredHeight = (int)tabHeight,
                hoverColorHex = closeHoverColorHex,
                pressColorHex = closePressColorHex,
                bubbleEnter = true,
                bubbleExit = true
            };
            close.AddChild(new LabelControl { text = closeCaption, fontSize = closeCaptionSize });
            close.RegisterOnRelease(() => CloseTab(item));
            return close;
        }

        private void ApplyTabColors()
        {
            int i = 0;
            foreach (TabItemControl item in Items)
            {
                string hex = ReferenceEquals(item, activeItem) ? activeTabColorHex : tabColorHex;
                if (i < strip.children.Count && strip.children[i] is ButtonControl tab)
                {
                    tab.controlColorHex = hex;
                    ButtonControl close = CloseButtonOf(tab);
                    if (close != null) close.controlColorHex = hex;
                }
                i++;
            }
        }

        // tab -> row -> [caption wrapper, close]
        private static ButtonControl CloseButtonOf(VulkanControl tab) =>
            tab.children.Count > 0 && tab.children[0] is VulkanControl row && row.children.Count > 1
                ? row.children[1] as ButtonControl
                : null;
        #endregion

        #region ---- layout ----
        public override Vector2D<float> Measure(Vector2D<float> availableSize)
        {
            float w = preferredWidth > 0 ? preferredWidth : MathF.Max(minWidth, availableSize.X);
            float h = preferredHeight > 0 ? preferredHeight : MathF.Max(minHeight, availableSize.Y);

            float innerW = MathF.Max(0, w - padding.totalHorizontal);
            float innerH = MathF.Max(0, h - padding.totalVertical);

            strip.Measure(new Vector2D<float>(innerW, tabHeight));
            activeItem?.Measure(new Vector2D<float>(innerW, MathF.Max(0, innerH - tabHeight)));

            DesiredSize = new Vector2D<float>(w, h);
            isMeasureDirty = false;
            return DesiredSize;
        }

        public override void Arrange(LayoutRect finalRect)
        {
            arrangedRect = finalRect;

            WriteArrangedTransform(finalRect);

            ClipRect = parent is VulkanControl p
                ? (clipOutOfBounds ? LayoutRect.Intersect(finalRect, p.ClipRect) : p.ClipRect)
                : finalRect;

            LayoutRect inner = finalRect.Shrink(padding);

            strip.Arrange(new LayoutRect(inner.x, inner.y, inner.width, tabHeight));

            activeItem?.Arrange(new LayoutRect(inner.x, inner.y + tabHeight, inner.width,
                MathF.Max(0, inner.height - tabHeight)));

            isArrangeDirty = false;
        }
        #endregion
    }
}
