using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.Registry;
using System.Numerics;

namespace ArctisAurora.Core.UI
{
    [A_XSDType("StackPanel", "UI")]
    public class StackPanelControl : ContainerControl
    {
        #region enums
        [A_XSDType("Orientation", "UI")]
        public enum Orientation
        {
            Horizontal,
            Vertical
        }
        #endregion

        #region properties
        // settings, mirrored into the row's LayoutNode
        [A_XSDElementProperty("Orientation", "UI", "")]
        public Orientation orientation
        {
            get => field;
            set
            {
                field = value;
                node.axis = (byte)value;
                InvalidateLayout();
            }
        }

        [A_XSDElementProperty("Spacing", "UI", "Space between children in pixels.")]
        public float Spacing
        {
            get => field;
            set
            {
                field = value;
                node.spacing = value;
                InvalidateLayout();
            }
        }
        #endregion

        public StackPanelControl()
        {
            orientation = Orientation.Vertical;
        }

        protected override Vector2 MeasureCore(Vector2 availableSize)
        {
            // A pinned axis is the box the children divide, not the offer that came in.
            float boxWidth = preferredWidth > 0 ? preferredWidth : availableSize.X;
            float boxHeight = preferredHeight > 0 ? preferredHeight : availableSize.Y;

            LayoutRect inner = new LayoutRect(0, 0, boxWidth, boxHeight)
                .Shrink(padding);

            float totalMain = 0f;
            float maxCross = 0f;
            int childCount = 0;
            float totalStarWeight = 0f;

            // Pass 1 — measure non-star children, accumulate star weights.
            foreach (Entity e in children)
            {
                if (e is not Control child) continue;
                ref ArrangeData ca = ref child.arrange;
                if (((ArrangeFlags)ca.flags & ArrangeFlags.Hidden) != 0) continue;
                childCount++;

                bool isStar = orientation == Orientation.Vertical ? ca.heightStar > 0f : ca.widthStar > 0f;
                if (isStar)
                {
                    // Cross size comes from pass 2, at the real main-axis allocation.
                    totalStarWeight += orientation == Orientation.Vertical ? ca.heightStar : ca.widthStar;
                }
                else
                {
                    Vector2 offer = orientation == Orientation.Vertical
                        ? new Vector2(inner.width, float.MaxValue)
                        : new Vector2(float.MaxValue, inner.height);

                    Thickness margin = ca.margin;
                    Vector2 desired = child.Measure(offer);

                    float childMain = orientation == Orientation.Vertical
                        ? desired.Y + margin.totalVertical
                        : desired.X + margin.totalHorizontal;
                    float childCross = orientation == Orientation.Vertical
                        ? desired.X + margin.totalHorizontal
                        : desired.Y + margin.totalVertical;

                    totalMain += childMain;
                    maxCross = MathF.Max(maxCross, childCross);
                }
            }

            if (childCount > 1)
                totalMain += Spacing * (childCount - 1);

            // Pass 2 — if there are star children, distribute the remaining main-axis space.
            if (totalStarWeight > 0f)
            {
                float availMain = orientation == Orientation.Vertical ? inner.height : inner.width;
                float remaining = MathF.Max(0, availMain - totalMain);
                float starUnit = remaining / totalStarWeight;

                foreach (Entity e in children)
                {
                    if (e is not Control child) continue;
                    ref ArrangeData ca = ref child.arrange;
                    if (((ArrangeFlags)ca.flags & ArrangeFlags.Hidden) != 0) continue;
                    bool isStar = orientation == Orientation.Vertical ? ca.heightStar > 0f : ca.widthStar > 0f;
                    if (!isStar) continue;

                    float starMain = (orientation == Orientation.Vertical ? ca.heightStar : ca.widthStar) * starUnit;
                    starMain = MathF.Max(starMain, orientation == Orientation.Vertical ? ca.minHeight : ca.minWidth);

                    Vector2 starOffer = orientation == Orientation.Vertical
                        ? new Vector2(inner.width, starMain)
                        : new Vector2(starMain, inner.height);

                    Thickness margin = ca.margin;
                    Vector2 desired = child.Measure(starOffer);

                    float childCross = orientation == Orientation.Vertical
                        ? desired.X + margin.totalHorizontal
                        : desired.Y + margin.totalVertical;
                    maxCross = MathF.Max(maxCross, childCross);

                    totalMain += starMain + (orientation == Orientation.Vertical
                        ? margin.totalVertical
                        : margin.totalHorizontal);
                }
            }

            float w = orientation == Orientation.Vertical
                ? maxCross + padding.totalHorizontal
                : totalMain + padding.totalHorizontal;
            float h = orientation == Orientation.Vertical
                ? totalMain + padding.totalVertical
                : maxCross + padding.totalVertical;

            if (preferredWidth > 0) w = MathF.Max(w, preferredWidth);
            if (preferredHeight > 0) h = MathF.Max(h, preferredHeight);

            arrange.desired = new Vector2(w, h);
            SetFlag(ArrangeFlags.MeasureDirty, false);
            return arrange.desired;
        }

        protected override void ArrangeCore(LayoutRect finalRect)
        {
            WriteArranged(finalRect);

            LayoutRect inner = finalRect.Shrink(padding);

            // Recompute star allocation against the real final size.
            float totalFixed = 0f;
            float totalStarWeight = 0f;
            int childCount = 0;

            foreach (Entity e in children)
            {
                if (e is not Control child) continue;
                ref ArrangeData ca = ref child.arrange;
                if (((ArrangeFlags)ca.flags & ArrangeFlags.Hidden) != 0) continue;
                childCount++;
                bool isStar = orientation == Orientation.Vertical ? ca.heightStar > 0f : ca.widthStar > 0f;
                if (isStar)
                    totalStarWeight += orientation == Orientation.Vertical ? ca.heightStar : ca.widthStar;
                else
                    totalFixed += orientation == Orientation.Vertical
                        ? ca.desired.Y + ca.margin.totalVertical
                        : ca.desired.X + ca.margin.totalHorizontal;
            }

            if (childCount > 1)
                totalFixed += Spacing * (childCount - 1);

            float availMain = orientation == Orientation.Vertical ? inner.height : inner.width;
            float starPool = totalStarWeight > 0f ? MathF.Max(0, availMain - totalFixed) : 0f;
            float starUnit = totalStarWeight > 0f ? starPool / totalStarWeight : 0f;

            float cursor = orientation == Orientation.Vertical ? inner.y : inner.x;
            bool first = true;

            foreach (Entity e in children)
            {
                if (e is not Control child) continue;
                ref ArrangeData ca = ref child.arrange;
                if (((ArrangeFlags)ca.flags & ArrangeFlags.Hidden) != 0) continue;

                if (!first) cursor += Spacing;
                first = false;

                bool isStar = orientation == Orientation.Vertical ? ca.heightStar > 0f : ca.widthStar > 0f;
                Thickness margin = ca.margin;

                if (orientation == Orientation.Vertical)
                {
                    // Auto (preferredWidth 0) or Stretch fills the cross axis, anything else is
                    // clamped to it. Measure offers a loose size and Arrange tightens, so an
                    // unclamped DesiredSize overflows the panel.
                    float availCrossW = inner.width - margin.totalHorizontal;
                    float childW = ca.preferredWidth == 0 || (HorizontalAlignment)ca.horizontalAlignment == HorizontalAlignment.Stretch
                        ? availCrossW
                        : MathF.Min(ca.desired.X, availCrossW);
                    float childX = (HorizontalAlignment)ca.horizontalAlignment switch
                    {
                        HorizontalAlignment.Left => inner.x + margin.left,
                        HorizontalAlignment.Right => inner.x + margin.left + (availCrossW - childW),
                        HorizontalAlignment.Center => inner.x + margin.left + (availCrossW - childW) * 0.5f,
                        _ => inner.x + margin.left,
                    };

                    // clamped to what is left of the panel: an unsized child measures the whole offer
                    float childH = isStar
                        ? ca.heightStar * starUnit - margin.totalVertical
                        : ca.desired.Y;
                    childH = Math.Clamp(childH, 0, MathF.Max(0, inner.Bottom - cursor - margin.totalVertical));
                    if (isStar) childH = MathF.Max(childH, ca.minHeight);

                    float childY = cursor + margin.top;
                    child.Arrange(new LayoutRect(childX, childY, childW, childH));
                    cursor += childH + margin.totalVertical;
                }
                else
                {
                    float availCrossH = inner.height - margin.totalVertical;
                    float childH = ca.preferredHeight == 0 || (VerticalAlignment)ca.verticalAlignment == VerticalAlignment.Stretch
                        ? availCrossH
                        : MathF.Min(ca.desired.Y, availCrossH);
                    float childY = (VerticalAlignment)ca.verticalAlignment switch
                    {
                        VerticalAlignment.Top => inner.y + margin.top,
                        VerticalAlignment.Bottom => inner.y + margin.top + (availCrossH - childH),
                        VerticalAlignment.Center => inner.y + margin.top + (availCrossH - childH) * 0.5f,
                        _ => inner.y + margin.top,
                    };

                    float childW = isStar
                        ? ca.widthStar * starUnit - margin.totalHorizontal
                        : ca.desired.X;
                    childW = Math.Clamp(childW, 0, MathF.Max(0, inner.Right - cursor - margin.totalHorizontal));
                    if (isStar) childW = MathF.Max(childW, ca.minWidth);

                    float childX = cursor + margin.left;
                    child.Arrange(new LayoutRect(childX, childY, childW, childH));
                    cursor += childW + margin.totalHorizontal;
                }
            }

            SetFlag(ArrangeFlags.ArrangeDirty, false);
        }
    }
}
