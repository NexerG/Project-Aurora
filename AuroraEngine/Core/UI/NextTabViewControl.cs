using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.Registry;
using ArctisAurora.EngineWork;
using ArctisAurora.EngineWork.Rendering;
using Silk.NET.Maths;

namespace ArctisAurora.Core.UI
{
    // A strip of tabs over one page. Only the active item is measured and arranged; the rest are
    // hidden. Closing a tab destroys it.
    [A_XSDType("NextTabView", "UI")]
    public class NextTabViewControl : ContainerControl
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
        [A_XSDElementProperty("TabInkColorHex", "UI", "Color of a tab's caption and close mark.")]
        public string tabInkColorHex = "#FFFFFF";

        // carried onto the splitter of any pane split off this one, and onto that pane in turn
        [A_XSDElementProperty("GripColorHex", "UI", "Ground of the splitter between panes.")]
        public string gripColorHex = "#2A2A2A";
        [A_XSDElementProperty("GripHoverColorHex", "UI", "Ground of a hovered pane splitter.")]
        public string gripHoverColorHex = "#3D3D3D";
        [A_XSDElementProperty("GripPressColorHex", "UI", "Ground of a held pane splitter.")]
        public string gripPressColorHex = "#4A4A4A";

        // menu each tab in the strip names
        [A_XSDElementProperty("TabContextMenu", "UI", "Menu a tab in the strip offers on right click.")]
        public string tabContextMenu = "tab";
        #endregion

        // caption and close button geometry
        protected const int captionSize = 14;
        private const float captionInset = 8f;
        private const float closeWidth = 20f;
        private const int closeCaptionSize = 12;
        private const string closeCaption = "x";
        private const string closeHoverColorHex = "#C42B1E";
        private const string closePressColorHex = "#A82318";

        // share of a side inside which a dropped tab splits instead of moving in
        private const float edgeBand = 0.25f;

        private readonly NextStackPanelControl strip = new NextStackPanelControl();

        // drop hint — the wash and the edge it is showing, null when nothing is being dragged over us
        private NextHintControl? hint;
        private NextSplitViewControl.SplitEdge? hintEdge;

        public NextTabItemControl? activeItem { get; private set; }

        public int ItemCount
        {
            get
            {
                int count = 0;
                foreach (NextTabItemControl _ in Items) count++;
                return count;
            }
        }

        public NextTabViewControl()
        {
            strip.orientation = NextStackPanelControl.Orientation.Horizontal;
            strip.alpha = 0f;
            strip.clipOutOfBounds = true;
            base.AddChild(strip);
        }

        public IEnumerable<NextTabItemControl> Items
        {
            get
            {
                foreach (Entity e in children)
                    if (e is NextTabItemControl item) yield return item;
            }
        }

        public override void AddChild(Entity entity)
        {
            if (entity is not NextTabItemControl item)
                throw new Exception("NextTabView children must be NextTabItem controls");

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

            if (entity is not NextTabItemControl item) return;

            NextTabItemControl next = wasActive ? Neighbour(item) : activeItem;
            if (wasActive) activeItem = null;

            RebuildStrip();
            if (activeItem == null && next != null) SetActive(next);
            InvalidateLayout();

            CloseIfEmptied();
        }

        #region ---- drop ----
        // What a drop at this point would do — an edge to split on, or null to take the tab in.
        // The button comes back out so a caller can tell "no edge" from "not a tab drag at all".
        private NextSplitViewControl.SplitEdge? PendingEdge(Control dropped, Vector2D<float> point, out NextTabStripButtonControl button)
        {
            button = dropped as NextTabStripButtonControl;
            if (button == null || !button.dragging) { button = null; return null; }

            // splitting off our own only tab would empty us and collapse the split straight back
            bool ownOnly = ReferenceEquals(button.item.parent, this) && ItemCount < 2;
            return ownOnly ? null : EdgeAt(point);
        }

        // Accepts a tab dragged out of any strip, including our own. A drop in the outer band of a
        // side splits this view instead of taking the tab in.
        public override bool FinishDrag(Control dragged, Vector2D<float> point)
        {
            NextSplitViewControl.SplitEdge? edge = PendingEdge(dragged, point, out NextTabStripButtonControl button);
            if (button == null) return false;

            if (edge != null)
            {
                NextTabViewControl pane = NextSplitViewControl.Split(this, edge.Value);
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
        public override bool DraggingOverStart(Control dragged, Vector2D<float> point) => ShowHint(dragged, point);

        public override bool DraggingOver(Control dragged, Vector2D<float> point) => ShowHint(dragged, point);

        public override bool DraggingOverEnd(Control dragged)
        {
            SetHint(null);
            return false;
        }

        private bool ShowHint(Control dragged, Vector2D<float> point)
        {
            NextSplitViewControl.SplitEdge? edge = PendingEdge(dragged, point, out NextTabStripButtonControl button);
            if (button == null) return false;

            SetHint(edge);
            return true;
        }

        // The wash is built on the first hint this view ever shows and kept after that. It has to be
        // the last child every time it is raised, because dense order is DFS and tabs are appended.
        private void SetHint(NextSplitViewControl.SplitEdge? edge)
        {
            if (hintEdge == edge) return;
            hintEdge = edge;

            if (edge != null)
            {
                if (hint == null)
                {
                    hint = new NextHintControl { hitTestable = false };
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
            NextSplitViewControl.SplitEdge.Left => new LayoutRect(inner.x, inner.y, inner.width * 0.5f, inner.height),
            NextSplitViewControl.SplitEdge.Right => new LayoutRect(inner.x + inner.width * 0.5f, inner.y, inner.width * 0.5f, inner.height),
            NextSplitViewControl.SplitEdge.Top => new LayoutRect(inner.x, inner.y, inner.width, inner.height * 0.5f),
            NextSplitViewControl.SplitEdge.Bottom => new LayoutRect(inner.x, inner.y + inner.height * 0.5f, inner.width, inner.height * 0.5f),
            _ => new LayoutRect(inner.x, inner.y, 0, 0)
        };

        // The outer band of a side, nearest side winning. Null in the middle and anywhere over the
        // strip, where a drop means "put the tab here".
        private NextSplitViewControl.SplitEdge? EdgeAt(Vector2D<float> point)
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

            if (nearest == left) return NextSplitViewControl.SplitEdge.Left;
            if (nearest == right) return NextSplitViewControl.SplitEdge.Right;
            if (nearest == top) return NextSplitViewControl.SplitEdge.Top;
            return NextSplitViewControl.SplitEdge.Bottom;
        }
        #endregion

        public void SetActive(NextTabItemControl item)
        {
            if (item != null && !children.Contains(item)) return;
            if (ReferenceEquals(activeItem, item)) return;

            activeItem?.Hide();
            activeItem = item;
            activeItem?.Show();

            ApplyTabColors();
            InvalidateLayout();
        }

        // Tears the whole subtree down — the strip button with it.
        public void CloseTab(NextTabItemControl item)
        {
            if (item == null || !children.Contains(item)) return;

            FinishClose(item);
        }

        // Snapshotted, because closing detaches the tab from children as it goes.
        public void CloseOthers(NextTabItemControl keep)
        {
            foreach (NextTabItemControl item in Items.ToArray())
                if (!ReferenceEquals(item, keep)) CloseTab(item);
        }

        public void CloseToTheRight(NextTabItemControl from)
        {
            bool passed = false;
            foreach (NextTabItemControl item in Items.ToArray())
            {
                if (ReferenceEquals(item, from)) { passed = true; continue; }
                if (passed) CloseTab(item);
            }
        }

        // Moves one named tab into a new pane beside this view. Splitting off our own only tab would
        // empty us and collapse the split straight back, which is the same guard a drop applies.
        public void SplitOff(NextTabItemControl item, NextSplitViewControl.SplitEdge edge)
        {
            if (item == null) return;
            if (ReferenceEquals(item.parent, this) && ItemCount < 2) return;

            NextTabViewControl pane = NextSplitViewControl.Split(this, edge);
            if (pane == null) return;

            item.SetParent(pane);
            pane.SetActive(item);
        }

        // A view of this kind, for a split to fill.
        protected internal virtual NextTabViewControl NewOfSameKind() => new NextTabViewControl();

        private void FinishClose(NextTabItemControl item)
        {
            if (item == null || !children.Contains(item)) return;

            NextTabItemControl next = Neighbour(item);
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

            if (parent is NextSplitViewControl split)
            {
                NextSplitViewControl.Collapse(split, this);
                return;
            }

            RenderWindow window = UIEngine.WindowOf(this);
            if (window == null || window == Engine.primary) return;

            Engine.CloseWindow(window);
        }

        // The tab after this one, or the one before it if it is last.
        private NextTabItemControl Neighbour(NextTabItemControl item)
        {
            NextTabItemControl previous = null;
            bool passed = false;
            foreach (NextTabItemControl candidate in Items)
            {
                if (ReferenceEquals(candidate, item)) { passed = true; continue; }
                if (passed) return candidate;
                previous = candidate;
            }
            return previous;
        }

        #region ---- open notes ----
        // Recaptions one tab; the strip is drawn from the headers, so it is rebuilt with it.
        public void Retitle(NextTabItemControl item, string header)
        {
            if (item == null) return;

            item.header = header;
            RebuildStrip();
        }

        public static NextDocumentEditorControl EditorOf(NextTabItemControl item) =>
            item.children.Count > 0 ? item.children[0] as NextDocumentEditorControl : null;

        // The tab showing this note, in whichever window it is open. Identity is the file the editor
        // loaded, not the tab's caption — a tab seeded from a UI document carries no name to match on.
        public static NextTabItemControl FindOpenDocument(string path, out NextTabViewControl owner)
        {
            foreach ((NextTabItemControl item, NextTabViewControl view) in FindOpenDocuments(path))
            {
                owner = view;
                return item;
            }

            owner = null;
            return null;
        }

        // Every tab showing this note. The tree is the register of what is open, so a rename that
        // walks it cannot be told about a view that has since been closed.
        public static IEnumerable<(NextTabItemControl item, NextTabViewControl view)> FindOpenDocuments(string path)
        {
            string target = Path.GetFullPath(path);

            foreach (RenderWindow window in Engine.windows.Values)
            {
                if (window.uiNext?.uiRoot == null) continue;

                foreach (NextTabViewControl view in TabViews(window.uiNext.uiRoot))
                    foreach (NextTabItemControl item in view.Items)
                    {
                        string open = EditorOf(item)?.session?.path;
                        if (open != null && string.Equals(open, target, StringComparison.OrdinalIgnoreCase))
                            yield return (item, view);
                    }
            }
        }

        // Every tab view under a control, itself included.
        public static IEnumerable<NextTabViewControl> TabViews(Control control)
        {
            if (control == null) yield break;
            if (control is NextTabViewControl view) yield return view;

            foreach (Entity child in control.children)
                if (child is Control childControl)
                    foreach (NextTabViewControl found in TabViews(childControl))
                        yield return found;
        }
        #endregion

        #region ---- strip ----
        private void RebuildStrip()
        {
            foreach (Entity button in strip.children.ToArray())
                button.Destroy();

            foreach (NextTabItemControl item in Items)
                strip.AddChild(BuildTab(item));

            ApplyTabColors();
        }

        // The row tiles the tab exactly and the wrapper carries the caption inset, so no container
        // inside a tab has bare area a press can land on.
        private Control BuildTab(NextTabItemControl item)
        {
            NextTabStripButtonControl tab = new NextTabStripButtonControl
            {
                preferredWidth = tabWidth,
                preferredHeight = tabHeight,
                hoverColorHex = tabHoverColorHex,
                pressColorHex = tabHoverColorHex,
                item = item,
                owner = this,
                contextMenu = tabContextMenu,
                stopsContextMenu = true
            };
            tab.RegisterOnRelease(e => { SetActive(item); return true; });

            NextStackPanelControl row = new NextStackPanelControl
            {
                orientation = NextStackPanelControl.Orientation.Horizontal,
                alpha = 0f
            };

            // preferredHeight pins the cross axis: a stack probes a star child at main-axis 0, which
            // wraps the caption one character per line, and maxCross keeps that height.
            NextPanelControl wrapper = new NextPanelControl
            {
                widthStar = 1f,
                preferredHeight = tabHeight,
                padding = new Thickness(0, 0, 0, captionInset),
                alpha = 0f
            };
            wrapper.AddChild(BuildCaption(item, tab));

            row.AddChild(wrapper);
            row.AddChild(BuildCloseButton(item));
            tab.AddChild(row);
            return tab;
        }

        // The caption for one tab, with the strip button a derivative may bind gestures on.
        protected virtual Control BuildCaption(NextTabItemControl item, NextTabStripButtonControl tab) =>
            new NextLabelControl
            {
                colorHex = tabInkColorHex,
                text = item.header,
                fontSize = captionSize,
                horizontalPosition = 0f
            };

        private Control BuildCloseButton(NextTabItemControl item)
        {
            CloseButtonControl close = new CloseButtonControl
            {
                preferredWidth = closeWidth,
                preferredHeight = tabHeight,
                hoverColorHex = closeHoverColorHex,
                pressColorHex = closePressColorHex
            };
            close.AddChild(new NextLabelControl { colorHex = tabInkColorHex, text = closeCaption, fontSize = closeCaptionSize });
            close.RegisterOnRelease(e => { CloseTab(item); return true; });
            return close;
        }

        // Enter and exit bubble so the tab keeps its hover tint; release does not, so closing never
        // also activates.
        private sealed class CloseButtonControl : NextButtonControl
        {
            public override bool OnPointerEnter(PointerEvent e)
            {
                base.OnPointerEnter(e);
                return false;
            }

            public override bool OnPointerExit(PointerEvent e)
            {
                base.OnPointerExit(e);
                return false;
            }
        }

        private void ApplyTabColors()
        {
            int i = 0;
            foreach (NextTabItemControl item in Items)
            {
                string hex = ReferenceEquals(item, activeItem) ? activeTabColorHex : tabColorHex;
                if (i < strip.children.Count && strip.children[i] is NextButtonControl tab)
                {
                    tab.colorHex = hex;
                    NextButtonControl close = CloseButtonOf(tab);
                    if (close != null) close.colorHex = hex;
                }
                i++;
            }
        }

        // tab -> row -> [caption wrapper, close]
        private static NextButtonControl CloseButtonOf(Control tab) =>
            tab.children.Count > 0 && tab.children[0] is Control row && row.children.Count > 1
                ? row.children[1] as NextButtonControl
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

            arrange.desired = new Vector2D<float>(w, h);
            SetFlag(ArrangeFlags.MeasureDirty, false);
            return arrange.desired;
        }

        public override void Arrange(LayoutRect finalRect)
        {
            WriteArranged(finalRect);

            LayoutRect inner = finalRect.Shrink(padding);

            strip.Arrange(new LayoutRect(inner.x, inner.y, inner.width, tabHeight));

            activeItem?.Arrange(new LayoutRect(inner.x, inner.y + tabHeight, inner.width,
                MathF.Max(0, inner.height - tabHeight)));

            hint?.Arrange(HintRect(inner));

            SetFlag(ArrangeFlags.ArrangeDirty, false);
        }
        #endregion
    }
}
