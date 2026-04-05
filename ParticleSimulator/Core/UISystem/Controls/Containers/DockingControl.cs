using ArctisAurora.Core.Registry;
using Silk.NET.Maths;

namespace ArctisAurora.Core.UISystem.Controls.Containers
{
    [A_XSDType("DockMode", "UI")]
    public enum DockMode
    {
        fill, left, right, top, bottom, unknown
    }

    /// <summary>
    /// A container that docks children to edges (top/bottom/left/right) in declaration
    /// order, with the last `fill` child taking the remaining space.
    /// 
    /// Layout algorithm (same for Measure and Arrange):
    ///   - Maintain a "remaining rect" starting at the full inner area.
    ///   - For each child in declaration order:
    ///     - left:   child takes the left slice, remaining rect shrinks from the left
    ///     - right:  child takes the right slice, remaining rect shrinks from the right
    ///     - top:    child takes the top slice, remaining rect shrinks from the top
    ///     - bottom: child takes the bottom slice, remaining rect shrinks from the bottom
    ///     - fill:   child gets the entire remaining rect (should be last)
    ///   - Multiple children can dock to the same edge — they stack in order.
    ///
    /// Scroll compatibility: reports true content size from Measure. If docked children
    /// exceed available space, a parent ScrollableControl will detect the overflow.
    /// </summary>
    [A_XSDType("Dock", "UI", AllowedChildren = typeof(IXMLChild_UI))]
    public class DockingControl : AbstractContainerControl
    {
        /// <summary>
        /// If true, the last child added without an explicit dockMode is treated as fill.
        /// Matches WPF DockPanel.LastChildFill behavior.
        /// </summary>
        [A_XSDElementProperty("LastChildFill", "UI", "If true, the last child fills remaining space regardless of its DockMode.")]
        public bool lastChildFill = true;

        // Quick-access references populated during Arrange.
        // Not used for layout logic — children list + dockMode is the source of truth.
        public VulkanControl top;
        public VulkanControl bottom;
        public VulkanControl left;
        public VulkanControl right;
        public VulkanControl center;

        public DockingControl()
        {
            preferredWidth = 0;
            preferredHeight = 0;
        }

        public override Vector2D<float> Measure(Vector2D<float> availableSize)
        {
            float w = preferredWidth > 0 ? preferredWidth : availableSize.X;
            float h = preferredHeight > 0 ? preferredHeight : availableSize.Y;

            LayoutRect remaining = new LayoutRect(0, 0, w, h).Shrink(padding);

            // Track the total extent of all docked children to report true content size.
            // usedLeft/usedRight accumulate horizontal carve-offs,
            // usedTop/usedBottom accumulate vertical carve-offs.
            float usedLeft = 0f;
            float usedRight = 0f;
            float usedTop = 0f;
            float usedBottom = 0f;
            float fillW = 0f;
            float fillH = 0f;

            for (int i = 0; i < children.Count; i++)
            {
                if (children[i] is not VulkanControl child) continue;

                DockMode mode = ResolveDockMode(child, i);
                Vector2D<float> offer = new Vector2D<float>(
                    MathF.Max(0, remaining.width),
                    MathF.Max(0, remaining.height));
                Vector2D<float> desired = child.Measure(offer);

                switch (mode)
                {
                    case DockMode.left:
                        usedLeft += desired.X + child.margin.totalHorizontal;
                        remaining = new LayoutRect(
                            remaining.x + desired.X + child.margin.totalHorizontal,
                            remaining.y,
                            MathF.Max(0, remaining.width - desired.X - child.margin.totalHorizontal),
                            remaining.height);
                        break;

                    case DockMode.right:
                        usedRight += desired.X + child.margin.totalHorizontal;
                        remaining = new LayoutRect(
                            remaining.x,
                            remaining.y,
                            MathF.Max(0, remaining.width - desired.X - child.margin.totalHorizontal),
                            remaining.height);
                        break;

                    case DockMode.top:
                        usedTop += desired.Y + child.margin.totalVertical;
                        remaining = new LayoutRect(
                            remaining.x,
                            remaining.y + desired.Y + child.margin.totalVertical,
                            remaining.width,
                            MathF.Max(0, remaining.height - desired.Y - child.margin.totalVertical));
                        break;

                    case DockMode.bottom:
                        usedBottom += desired.Y + child.margin.totalVertical;
                        remaining = new LayoutRect(
                            remaining.x,
                            remaining.y,
                            remaining.width,
                            MathF.Max(0, remaining.height - desired.Y - child.margin.totalVertical));
                        break;

                    case DockMode.fill:
                        fillW = desired.X + child.margin.totalHorizontal;
                        fillH = desired.Y + child.margin.totalVertical;
                        break;
                }
            }

            // True content size: horizontal edges + fill + padding
            float contentW = usedLeft + usedRight + MathF.Max(fillW, remaining.width) + padding.totalHorizontal;
            float contentH = usedTop + usedBottom + MathF.Max(fillH, remaining.height) + padding.totalVertical;

            // Preferred size as floor, not cap — scroll parents see the overflow
            float finalW = preferredWidth > 0 ? MathF.Max(contentW, preferredWidth) : contentW;
            float finalH = preferredHeight > 0 ? MathF.Max(contentH, preferredHeight) : contentH;

            DesiredSize = new Vector2D<float>(finalW, finalH);
            isMeasureDirty = false;
            return DesiredSize;
        }

        public override void Arrange(LayoutRect finalRect)
        {
            arrangedRect = finalRect;

            transform.SetWorldPosition(new Vector3D<float>(
                finalRect.x + finalRect.width / 2f,
                finalRect.y + finalRect.height / 2f,
                parent != null
                    ? parent.transform.GetEntityPosition().Z + 0.001f
                    : transform.GetEntityPosition().Z));
            transform.SetWorldScale(new Vector3D<float>(finalRect.width, finalRect.height, 1));

            ClipRect = parent is VulkanControl p
                ? (clipOutOfBounds ? LayoutRect.Intersect(finalRect, p.ClipRect) : p.ClipRect)
                : finalRect;

            // Reset quick-access references
            top = null;
            bottom = null;
            left = null;
            right = null;
            center = null;

            LayoutRect remaining = finalRect.Shrink(padding);

            for (int i = 0; i < children.Count; i++)
            {
                if (children[i] is not VulkanControl child) continue;

                DockMode mode = ResolveDockMode(child, i);
                LayoutRect childRect;

                switch (mode)
                {
                    case DockMode.left:
                        {
                            float sliceW = child.DesiredSize.X + child.margin.totalHorizontal;
                            childRect = new LayoutRect(remaining.x, remaining.y, sliceW, remaining.height)
                                .Shrink(child.margin);

                            // Apply vertical alignment within the slice
                            childRect = AlignVertically(child, childRect);

                            remaining = new LayoutRect(
                                remaining.x + sliceW,
                                remaining.y,
                                MathF.Max(0, remaining.width - sliceW),
                                remaining.height);
                            left ??= child;
                            break;
                        }

                    case DockMode.right:
                        {
                            float sliceW = child.DesiredSize.X + child.margin.totalHorizontal;
                            float sliceX = remaining.x + remaining.width - sliceW;
                            childRect = new LayoutRect(sliceX, remaining.y, sliceW, remaining.height)
                                .Shrink(child.margin);

                            childRect = AlignVertically(child, childRect);

                            remaining = new LayoutRect(
                                remaining.x,
                                remaining.y,
                                MathF.Max(0, remaining.width - sliceW),
                                remaining.height);
                            right ??= child;
                            break;
                        }

                    case DockMode.top:
                        {
                            float sliceH = child.DesiredSize.Y + child.margin.totalVertical;
                            childRect = new LayoutRect(remaining.x, remaining.y, remaining.width, sliceH)
                                .Shrink(child.margin);

                            childRect = AlignHorizontally(child, childRect);

                            remaining = new LayoutRect(
                                remaining.x,
                                remaining.y + sliceH,
                                remaining.width,
                                MathF.Max(0, remaining.height - sliceH));
                            top ??= child;
                            break;
                        }

                    case DockMode.bottom:
                        {
                            float sliceH = child.DesiredSize.Y + child.margin.totalVertical;
                            float sliceY = remaining.y + remaining.height - sliceH;
                            childRect = new LayoutRect(remaining.x, sliceY, remaining.width, sliceH)
                                .Shrink(child.margin);

                            childRect = AlignHorizontally(child, childRect);

                            remaining = new LayoutRect(
                                remaining.x,
                                remaining.y,
                                remaining.width,
                                MathF.Max(0, remaining.height - sliceH));
                            bottom ??= child;
                            break;
                        }

                    case DockMode.fill:
                    default:
                        {
                            // Fill child gets whatever remains
                            childRect = remaining.Shrink(child.margin);
                            center ??= child;
                            // Remaining is now consumed — but we don't break the loop
                            // in case there are more children (they'd get zero-size rects)
                            remaining = LayoutRect.Empty;
                            break;
                        }
                }

                child.Arrange(childRect);
            }

            isArrangeDirty = false;
        }

        /// <summary>
        /// Determines the dock mode for a child. If lastChildFill is true and this
        /// is the last VulkanControl child, it's forced to fill regardless of its
        /// declared dockMode.
        /// </summary>
        private DockMode ResolveDockMode(VulkanControl child, int index)
        {
            if (lastChildFill && IsLastVulkanChild(index))
                return DockMode.fill;

            if (child.dockMode == DockMode.unknown)
                return DockMode.fill;

            return child.dockMode;
        }

        private bool IsLastVulkanChild(int fromIndex)
        {
            for (int i = fromIndex + 1; i < children.Count; i++)
            {
                if (children[i] is VulkanControl) return false;
            }
            return true;
        }

        /// <summary>
        /// For left/right slices: applies vertical alignment within the slice.
        /// Stretch fills the full slice height. Otherwise the child uses its DesiredSize.Y.
        /// </summary>
        private static LayoutRect AlignVertically(VulkanControl child, LayoutRect slot)
        {
            if (child.verticalAlignment == VerticalAlignment.Stretch)
                return slot;

            float childH = MathF.Min(child.DesiredSize.Y, slot.height);
            float oy = child.verticalAlignment switch
            {
                VerticalAlignment.Center => (slot.height - childH) * 0.5f,
                VerticalAlignment.Bottom => slot.height - childH,
                _ => 0f // Top
            };
            return new LayoutRect(slot.x, slot.y + oy, slot.width, childH);
        }

        /// <summary>
        /// For top/bottom slices: applies horizontal alignment within the slice.
        /// Stretch fills the full slice width. Otherwise the child uses its DesiredSize.X.
        /// </summary>
        private static LayoutRect AlignHorizontally(VulkanControl child, LayoutRect slot)
        {
            if (child.horizontalAlignment == HorizontalAlignment.Stretch)
                return slot;

            float childW = MathF.Min(child.DesiredSize.X, slot.width);
            float ox = child.horizontalAlignment switch
            {
                HorizontalAlignment.Center => (slot.width - childW) * 0.5f,
                HorizontalAlignment.Right => slot.width - childW,
                _ => 0f // Left
            };
            return new LayoutRect(slot.x + ox, slot.y, childW, slot.height);
        }
    }
}