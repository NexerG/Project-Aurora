using ArctisAurora.Core.AssetRegistry;
using ArctisAurora.Core.ECS.EngineEntity;
using Silk.NET.Maths;

namespace ArctisAurora.Core.UISystem.Controls.Containers
{
    [A_XSDType("StackPanel", "UI", AllowedChildren = typeof(IXMLChild_UI))]
    public class StackPanelControl : AbstractContainerControl
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
        // settings
        [A_XSDElementProperty("Orientation", "UI", "")]
        public Orientation orientation = Orientation.Vertical;
        
        [A_XSDElementProperty("Spacing", "UI", "Space between children in pixels.")]
        public float Spacing = 0f;
        #endregion

        public StackPanelControl()
        {
            preferredWidth = 0;
            preferredHeight = 0;
        }

        public override Vector2D<float> Measure(Vector2D<float> availableSize)
        {
            LayoutRect inner = new LayoutRect(0, 0, availableSize.X, availableSize.Y)
                .Shrink(padding);

            float totalMain = 0f;
            float maxCross = 0f;
            int childCount = 0;
            float totalStarWeight = 0f;

            // Pass 1 — measure non-star children, accumulate star weights.
            foreach (Entity e in children)
            {
                if (e is not VulkanControl child) continue;
                childCount++;

                bool isStar = orientation == Orientation.Vertical ? child.IsHeightStar : child.IsWidthStar;
                if (isStar)
                {
                    totalStarWeight += orientation == Orientation.Vertical ? child.heightStar : child.widthStar;
                    // Cross axis: still measure with full cross offer so we know cross desired size.
                    Vector2D<float> crossOffer = orientation == Orientation.Vertical
                        ? new Vector2D<float>(inner.width, 0)
                        : new Vector2D<float>(0, inner.height);
                    child.Measure(crossOffer);

                    float childCross = orientation == Orientation.Vertical
                        ? child.DesiredSize.X + child.margin.totalHorizontal
                        : child.DesiredSize.Y + child.margin.totalVertical;
                    maxCross = MathF.Max(maxCross, childCross);
                }
                else
                {
                    Vector2D<float> offer = orientation == Orientation.Vertical
                        ? new Vector2D<float>(inner.width, float.MaxValue)
                        : new Vector2D<float>(float.MaxValue, inner.height);

                    Vector2D<float> desired = child.Measure(offer);

                    float childMain = orientation == Orientation.Vertical
                        ? desired.Y + child.margin.totalVertical
                        : desired.X + child.margin.totalHorizontal;
                    float childCross = orientation == Orientation.Vertical
                        ? desired.X + child.margin.totalHorizontal
                        : desired.Y + child.margin.totalVertical;

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
                    if (e is not VulkanControl child) continue;
                    bool isStar = orientation == Orientation.Vertical ? child.IsHeightStar : child.IsWidthStar;
                    if (!isStar) continue;

                    float starMain = (orientation == Orientation.Vertical ? child.heightStar : child.widthStar) * starUnit;

                    Vector2D<float> starOffer = orientation == Orientation.Vertical
                        ? new Vector2D<float>(inner.width, starMain)
                        : new Vector2D<float>(starMain, inner.height);

                    Vector2D<float> desired = child.Measure(starOffer);

                    float childCross = orientation == Orientation.Vertical
                        ? desired.X + child.margin.totalHorizontal
                        : desired.Y + child.margin.totalVertical;
                    maxCross = MathF.Max(maxCross, childCross);

                    totalMain += starMain + (orientation == Orientation.Vertical
                        ? child.margin.totalVertical
                        : child.margin.totalHorizontal);
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

            DesiredSize = new Vector2D<float>(w, h);
            isMeasureDirty = false;
            return DesiredSize;
        }

        public override void Arrange(LayoutRect finalRect)
        {
            arrangedRect = finalRect;

            transform.SetWorldPosition(new Vector3D<float>(
                finalRect.x + finalRect.width / 2f,
                finalRect.y + finalRect.height / 2f,
                parent.transform.GetEntityPosition().Z + 0.001f));
            transform.SetWorldScale(new Vector3D<float>(finalRect.width, finalRect.height, 1));

            ClipRect = parent is VulkanControl p
                ? (clipOutOfBounds ? LayoutRect.Intersect(finalRect, p.ClipRect) : p.ClipRect)
                : finalRect;

            LayoutRect inner = finalRect.Shrink(padding);

            // Recompute star allocation against the real final size.
            float totalFixed = 0f;
            float totalStarWeight = 0f;
            int childCount = 0;

            foreach (Entity e in children)
            {
                if (e is not VulkanControl child) continue;
                childCount++;
                bool isStar = orientation == Orientation.Vertical ? child.IsHeightStar : child.IsWidthStar;
                if (isStar)
                    totalStarWeight += orientation == Orientation.Vertical ? child.heightStar : child.widthStar;
                else
                    totalFixed += orientation == Orientation.Vertical
                        ? child.DesiredSize.Y + child.margin.totalVertical
                        : child.DesiredSize.X + child.margin.totalHorizontal;
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
                if (e is not VulkanControl child) continue;

                if (!first) cursor += Spacing;
                first = false;

                bool isStar = orientation == Orientation.Vertical ? child.IsHeightStar : child.IsWidthStar;

                if (orientation == Orientation.Vertical)
                {
                    float availCrossW = inner.width - child.margin.totalHorizontal;
                    float childW = child.horizontalAlignment == HorizontalAlignment.Stretch
                        ? availCrossW
                        : child.DesiredSize.X;
                    float childX = child.horizontalAlignment switch
                    {
                        HorizontalAlignment.Left => inner.x + child.margin.left,
                        HorizontalAlignment.Right => inner.x + child.margin.left + (availCrossW - childW),
                        HorizontalAlignment.Center => inner.x + child.margin.left + (availCrossW - childW) * 0.5f,
                        _ => inner.x + child.margin.left,
                    };

                    float childH = isStar
                        ? child.heightStar * starUnit - child.margin.totalVertical
                        : child.DesiredSize.Y;
                    childH = MathF.Max(0, childH);

                    float childY = cursor + child.margin.top;
                    child.Arrange(new LayoutRect(childX, childY, childW, childH));
                    cursor += childH + child.margin.totalVertical;
                }
                else
                {
                    float availCrossH = inner.height - child.margin.totalVertical;
                    float childH = child.verticalAlignment == VerticalAlignment.Stretch
                        ? availCrossH
                        : child.DesiredSize.Y;
                    float childY = child.verticalAlignment switch
                    {
                        VerticalAlignment.Top => inner.y + child.margin.top,
                        VerticalAlignment.Bottom => inner.y + child.margin.top + (availCrossH - childH),
                        VerticalAlignment.Center => inner.y + child.margin.top + (availCrossH - childH) * 0.5f,
                        _ => inner.y + child.margin.top,
                    };

                    float childW = isStar
                        ? child.widthStar * starUnit - child.margin.totalHorizontal
                        : child.DesiredSize.X;
                    childW = MathF.Max(0, childW);

                    float childX = cursor + child.margin.left;
                    child.Arrange(new LayoutRect(childX, childY, childW, childH));
                    cursor += childW + child.margin.totalHorizontal;
                }
            }

            isArrangeDirty = false;
        }
    }
}