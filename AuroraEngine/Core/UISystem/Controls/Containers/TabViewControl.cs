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

        [A_XSDElementProperty("TabContextMenu", "UI", "Menu in ContextMenus.xml a tab in the strip offers on right click.")]
        public string tabContextMenu = "tab";
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

        // share of a side inside which a dropped tab splits instead of moving in
        private const float edgeBand = 0.25f;

        private readonly StackPanelControl strip = new StackPanelControl();

        // drop hint — the wash and the edge it is showing, null when nothing is being dragged over us
        private HintControl? hint;
        private SplitViewControl.SplitEdge? hintEdge;

        public TabItemControl? activeItem { get; private set; }

        public int ItemCount
        {
            get
            {
                int count = 0;
                foreach (TabItemControl _ in Items) count++;
                return count;
            }
        }

        public TabViewControl()
        {
            strip.orientation = StackPanelControl.Orientation.Horizontal;
            strip.maskAsset = AssetRegistries.GetAsset<TextureAsset>("invisible");
            strip.clipOutOfBounds = true;
            base.AddChild(strip);
        }

        public IEnumerable<TabItemControl> Items
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

        // What a drop at this point would do — an edge to split on, or null to take the tab in.
        // The button comes back out so a caller can tell "no edge" from "not a tab drag at all".
        private SplitViewControl.SplitEdge? PendingEdge(VulkanControl dropped, Vector2D<float> point, out TabStripButtonControl button)
        {
            button = dropped as TabStripButtonControl;
            if (button == null || !button.dragging) { button = null; return null; }

            // splitting off our own only tab would empty us and collapse the split straight back
            bool ownOnly = ReferenceEquals(button.item.parent, this) && ItemCount < 2;
            return ownOnly ? null : EdgeAt(point);
        }

        // Accepts a tab dragged out of any strip, including our own. A drop in the outer band of a
        // side splits this view instead of taking the tab in.
        public override bool ResolveDrop(VulkanControl dropped, Vector2D<float> point)
        {
            SplitViewControl.SplitEdge? edge = PendingEdge(dropped, point, out TabStripButtonControl button);
            if (button == null) return false;

            button.accepted = true;

            if (edge != null)
            {
                TabViewControl pane = SplitViewControl.Split(this, edge.Value);
                if (pane != null)
                {
                    button.item.SetParent(pane);
                    pane.SetActive(button.item);
                    return true;
                }
            }

            if (ReferenceEquals(button.item.parent, this)) return true;

            button.item.SetParent(this);
            SetActive(button.item);
            return true;
        }

        // Claims the hint for any live tab drag over us, whether or not it is over an edge — the
        // middle is a drop we take too, it just splits nothing and so washes nothing.
        public override bool ResolveDropHint(VulkanControl dropped, Vector2D<float> point)
        {
            SplitViewControl.SplitEdge? edge = PendingEdge(dropped, point, out TabStripButtonControl button);
            if (button == null) return false;

            SetHint(edge);
            return true;
        }

        public override void ClearDropHint() => SetHint(null);

        // The wash is built on the first hint this view ever shows and kept after that. It has to be
        // the last child every time it is raised, because dense order is DFS and tabs are appended.
        private void SetHint(SplitViewControl.SplitEdge? edge)
        {
            if (hintEdge == edge) return;
            hintEdge = edge;

            if (edge != null)
            {
                if (hint == null)
                {
                    hint = new HintControl { hitTestable = false };
                    base.AddChild(hint);
                }
                children.Remove(hint);
                children.Add(hint);
                MarkTreeOrderDirty();
            }

            InvalidateLayout();
        }

        // The half the new pane would take, and nothing at all when there is no edge.
        private LayoutRect HintRect(LayoutRect inner) => hintEdge switch
        {
            SplitViewControl.SplitEdge.Left => new LayoutRect(inner.x, inner.y, inner.width * 0.5f, inner.height),
            SplitViewControl.SplitEdge.Right => new LayoutRect(inner.x + inner.width * 0.5f, inner.y, inner.width * 0.5f, inner.height),
            SplitViewControl.SplitEdge.Top => new LayoutRect(inner.x, inner.y, inner.width, inner.height * 0.5f),
            SplitViewControl.SplitEdge.Bottom => new LayoutRect(inner.x, inner.y + inner.height * 0.5f, inner.width, inner.height * 0.5f),
            _ => new LayoutRect(inner.x, inner.y, 0, 0)
        };

        // The outer band of a side, nearest side winning. Null in the middle and anywhere over the
        // strip, where a drop means "put the tab here".
        private SplitViewControl.SplitEdge? EdgeAt(Vector2D<float> point)
        {
            LayoutRect rect = arrangedRect;
            if (rect.width <= 0f || rect.height <= 0f) return null;
            if (point.Y < rect.y + tabHeight) return null;

            float left = (point.X - rect.x) / rect.width;
            float top = (point.Y - rect.y) / rect.height;
            float right = 1f - left;
            float bottom = 1f - top;

            float nearest = MathF.Min(MathF.Min(left, right), MathF.Min(top, bottom));
            if (nearest > edgeBand) return null;

            if (nearest == left) return SplitViewControl.SplitEdge.Left;
            if (nearest == right) return SplitViewControl.SplitEdge.Right;
            if (nearest == top) return SplitViewControl.SplitEdge.Top;
            return SplitViewControl.SplitEdge.Bottom;
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

        // Saves the page, then tears the whole subtree down — the strip button with it. An edited
        // note that has never been named asks for one first, and the teardown waits for the answer;
        // abandoning the naming abandons the close.
        public void CloseTab(TabItemControl item)
        {
            if (item == null || !children.Contains(item)) return;

            DocumentEditorControl editor = EditorOf(item);
            if (editor != null && editor.needsNaming)
            {
                editor.SaveNamed(() => FinishClose(item), () => FinishClose(item));
                return;
            }

            editor?.Save();
            FinishClose(item);
        }

        // Closes a tab without writing it, for a note that is no longer on disk.
        public void DiscardTab(TabItemControl item) => FinishClose(item);

        // Snapshotted, because closing detaches the tab from children as it goes.
        public void CloseOthers(TabItemControl keep)
        {
            foreach (TabItemControl item in Items.ToArray())
                if (!ReferenceEquals(item, keep)) CloseTab(item);
        }

        public void CloseToTheRight(TabItemControl from)
        {
            bool passed = false;
            foreach (TabItemControl item in Items.ToArray())
            {
                if (ReferenceEquals(item, from)) { passed = true; continue; }
                if (passed) CloseTab(item);
            }
        }

        // Moves one named tab into a new pane beside this view. Splitting off our own only tab would
        // empty us and collapse the split straight back, which is the same guard a drop applies.
        public void SplitOff(TabItemControl item, SplitViewControl.SplitEdge edge)
        {
            if (item == null) return;
            if (ReferenceEquals(item.parent, this) && ItemCount < 2) return;

            TabViewControl pane = SplitViewControl.Split(this, edge);
            if (pane == null) return;

            item.SetParent(pane);
            pane.SetActive(item);
        }

        private void FinishClose(TabItemControl item)
        {
            if (item == null || !children.Contains(item)) return;

            TabItemControl next = Neighbour(item);
            if (ReferenceEquals(item, activeItem)) activeItem = null;
            item.Destroy();

            RebuildStrip();
            if (activeItem == null && next != null) SetActive(next);
            InvalidateLayout();

            CloseIfEmptied();
        }

        // A pane that empties hands its split back to its neighbour. A window that exists to hold tabs
        // has nothing left to be once the last one goes. The main window stays — closing that is the
        // application closing.
        private void CloseIfEmptied()
        {
            if (activeItem != null) return;

            if (parent is SplitViewControl split)
            {
                SplitViewControl.Collapse(split, this);
                return;
            }

            RenderWindow window = RenderWindow.Of(this);
            if (window == null || window == Engine.primary) return;

            Engine.CloseWindow(window);
        }

        // The tab showing this note, in whichever window it is open. Identity is the file the editor
        // loaded, not the tab's caption — a tab seeded from a UI document carries no name to match
        // on, so matching by name reopened notes that were already on screen.
        public static TabItemControl FindOpenDocument(string path, out TabViewControl owner)
        {
            foreach ((TabItemControl item, TabViewControl view) in FindOpenDocuments(path))
            {
                owner = view;
                return item;
            }

            owner = null;
            return null;
        }

        // Every tab showing this note. The tree is the register of what is open, so a rename that
        // walks it cannot be told about a view that has since been closed.
        public static IEnumerable<(TabItemControl item, TabViewControl view)> FindOpenDocuments(string path)
        {
            string target = Path.GetFullPath(path);

            foreach (RenderWindow window in Engine.windows.Values)
            {
                if (window.ui.uiRoot == null) continue;

                foreach (TabViewControl view in TabViews(window.ui.uiRoot))
                    foreach (TabItemControl item in view.Items)
                    {
                        string open = EditorOf(item)?.session?.path;
                        if (open != null && string.Equals(open, target, StringComparison.OrdinalIgnoreCase))
                            yield return (item, view);
                    }
            }
        }

        // Recaptions one tab; the strip is drawn from the headers, so it is rebuilt with it.
        public void Retitle(TabItemControl item, string header)
        {
            if (item == null || !children.Contains(item)) return;

            item.header = header;
            RebuildStrip();
        }

        private static IEnumerable<TabViewControl> TabViews(VulkanControl control)
        {
            if (control is TabViewControl view) yield return view;

            foreach (Entity child in control.children)
                if (child is VulkanControl childControl)
                    foreach (TabViewControl found in TabViews(childControl))
                        yield return found;
        }

        public static DocumentEditorControl EditorOf(TabItemControl item) =>
            item.children.Count > 0 ? item.children[0] as DocumentEditorControl : null;

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
                contextMenus = tabContextMenu,
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
            hint?.Measure(new Vector2D<float>(innerW, innerH));

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

            hint?.Arrange(HintRect(inner));

            isArrangeDirty = false;
        }
        #endregion
    }
}
