using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.EngineWork.Rendering;
using Silk.NET.Maths;

namespace ArctisAurora.Core.UI
{
    // One panel of an open context menu, built from its entries. Never authored, so untagged.
    public class NextContextMenuControl : NextStackPanelControl
    {
        // palette
        private const string panelColorHex = "#FFFFFF";
        private const string panelEdgeColorHex = "#DCDAD3";
        private const string rowHoverColorHex = "#EAE8E2";
        private const string rowPressColorHex = "#E3E1D9";
        private const string inkColorHex = "#34322D";
        private const string lineColorHex = "#E6E4DE";
        private const int captionSize = 13;

        // placement — position is in the origin window's design space, even when hosted in its own
        public int depth;
        public Vector2D<float> position;
        public RenderWindow? window;
        public Row? opener;

        public NextContextMenuControl(List<ContextMenuEntry> entries, int depth)
        {
            this.depth = depth;
            orientation = Orientation.Vertical;
            padding = new Thickness(4f);
            colorHex = panelColorHex;
            cornerRadius = new CornerRadii(6f);
            edgeColorHex = panelEdgeColorHex;
            edgeThickness = 1f;
            stopsContextMenu = true;

            foreach (ContextMenuEntry entry in entries)
                AddChild(entry is ContextMenuLine ? Line() : new Row(entry));
        }

        // At its own position and size, whatever box the root offers.
        public override void Arrange(LayoutRect finalRect)
        {
            Vector2D<float> at = window != null ? Vector2D<float>.Zero : position;
            base.Arrange(new LayoutRect(at.X, at.Y, DesiredSize.X, DesiredSize.Y));
        }

        private static Control Line() => new NextPanelControl
        {
            preferredWidth = 1f,
            preferredHeight = 1f,
            horizontalAlignment = HorizontalAlignment.Stretch,
            margin = new Thickness(4f, 0f, 4f, 0f),
            colorHex = lineColorHex
        };

        // A button or submenu entry: its caption, and an arrow flush right when it opens a submenu.
        public sealed class Row : NextButtonControl
        {
            private const float arrowGap = 24f;

            public readonly ContextMenuEntry entry;
            private readonly NextLabelControl caption;
            private readonly NextLabelControl? arrow;

            public Row(ContextMenuEntry entry)
            {
                this.entry = entry;
                padding = new Thickness(4f, 10f, 4f, 10f);
                cornerRadius = new CornerRadii(4f);
                hoverColorHex = rowHoverColorHex;
                pressColorHex = rowPressColorHex;
                colorHex = panelColorHex;

                string text = entry is ContextMenuSubmenu submenu ? submenu.text : ((ContextMenuButton)entry).text;
                caption = new NextLabelControl { text = text, fontSize = captionSize, colorHex = inkColorHex };
                AddChild(caption);

                if (entry is not ContextMenuSubmenu) return;
                arrow = new NextLabelControl { text = "›", fontSize = captionSize, colorHex = inkColorHex };
                AddChild(arrow);
            }

            public override bool takesActiveControl => false;

            public override void AddChild(Entity entity)
            {
                children.Add(entity);
                entity.parent = this;
                MarkTreeOrderDirty();
                InvalidateLayout();
            }

            public override Vector2D<float> Measure(Vector2D<float> availableSize)
            {
                Vector2D<float> c = caption.Measure(availableSize);
                Vector2D<float> a = arrow?.Measure(availableSize) ?? Vector2D<float>.Zero;

                float w = c.X + (arrow != null ? arrowGap + a.X : 0f) + padding.totalHorizontal;
                float h = MathF.Max(c.Y, a.Y) + padding.totalVertical;

                arrange.desired = new Vector2D<float>(w, h);
                SetFlag(ArrangeFlags.MeasureDirty, false);
                return arrange.desired;
            }

            public override void Arrange(LayoutRect finalRect)
            {
                WriteArranged(finalRect);
                LayoutRect inner = finalRect.Shrink(padding);

                Vector2D<float> c = caption.DesiredSize;
                caption.Arrange(new LayoutRect(inner.x, inner.y + (inner.height - c.Y) * 0.5f, c.X, c.Y));

                if (arrow != null)
                {
                    Vector2D<float> a = arrow.DesiredSize;
                    arrow.Arrange(new LayoutRect(inner.x + inner.width - a.X, inner.y + (inner.height - a.Y) * 0.5f, a.X, a.Y));
                }
                SetFlag(ArrangeFlags.ArrangeDirty, false);
            }

            public override bool OnPointerEnter(PointerEvent e)
            {
                base.OnPointerEnter(e);
                NextContextMenus.Entered((NextContextMenuControl)parent, this);
                return true;
            }

            public override bool OnPointerRelease(PointerEvent e)
            {
                base.OnPointerRelease(e);
                NextContextMenus.Clicked(this);
                return true;
            }
        }
    }
}
