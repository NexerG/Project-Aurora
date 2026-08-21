using ArctisAurora.Core.Registry;
using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.UISystem.Controls.Interactable;
using Silk.NET.Maths;

namespace ArctisAurora.Core.UISystem.Controls.Containers
{
    [A_XSDType("Scrollable", "UI")]
    public enum ScrollDirection
    {
        Vertical, Horizontal, Both
    }

    [A_XSDType("ScrollableControl", "UI", allowedChildren: typeof(IXMLChild_UI), minChildren: 0, maxChildren: 1)]
    public class ScrollableControl : AbstractContainerControl
    {
        [A_XSDElementProperty("ScrollDirection", "UI", "Which axes can scroll. Default: Vertical.")]
        public ScrollDirection scrollDirection = ScrollDirection.Vertical;

        [A_XSDElementProperty("ScrollSensitivity", "UI", "Pixels per scroll wheel tick.")]
        public float scrollSensitivity = 30f;

        // ---- Scroll state ----

        /// <summary>
        /// Current scroll offset in pixels. Positive Y = content scrolled upward.
        /// </summary>
        private Vector2D<float> scrollOffset = new Vector2D<float>(0, 0);

        /// <summary>
        /// The child's full content size after measure — may exceed viewport.
        /// </summary>
        private Vector2D<float> contentSize = new Vector2D<float>(0, 0);

        /// <summary>
        /// The usable inner viewport size (arranged rect minus padding).
        /// </summary>
        private Vector2D<float> viewportSize = new Vector2D<float>(0, 0);

        public bool CanScrollHorizontal => scrollDirection == ScrollDirection.Horizontal || scrollDirection == ScrollDirection.Both;
        public bool CanScrollVertical => scrollDirection == ScrollDirection.Vertical || scrollDirection == ScrollDirection.Both;

        // ---- Scrollbar ----

        private const float barWidth = 8f;
        private const float barInset = 2f;
        private const float minThumbLength = 24f;

        private ScrollThumbControl thumb = null!;

        /// <summary>
        /// Width reserved down the right edge for the scrollbar. Always reserved when the axis can
        /// scroll, so that showing a thumb never re-wraps the content that decides whether it shows.
        /// </summary>
        private float Gutter => CanScrollVertical ? barWidth + barInset * 2f : 0f;

        /// <summary>
        /// How far the thumb can slide, in pixels. Zero when the content fits.
        /// </summary>
        public float ThumbTravel { get; private set; }

        /// <summary>
        /// The single content child. The thumb also lives in children and is never it, so this is
        /// read off the list rather than cached — DocumentEditorControl clears children wholesale.
        /// </summary>
        private VulkanControl Content
        {
            get
            {
                foreach (Entity e in children)
                    if (e is VulkanControl control && !ReferenceEquals(control, thumb)) return control;
                return null;
            }
        }

        /// <summary>
        /// How far the content can scroll on each axis. Zero if content fits.
        /// </summary>
        public Vector2D<float> MaxScrollOffset => new Vector2D<float>(
            MathF.Max(0, contentSize.X - viewportSize.X),
            MathF.Max(0, contentSize.Y - viewportSize.Y)
        );

        public ScrollableControl()
        {
            clipOutOfBounds = true; // always clip — this is a viewport
        }

        // -------------------------------------------------------------------
        //  Measure: pass our own viewport size to the child, not infinity.
        //  The child measures normally. If it comes back larger than the
        //  viewport, we know we need to scroll — but we still report our
        //  own viewport size as DesiredSize so the parent layout isn't
        //  affected by overflowing content.
        // -------------------------------------------------------------------
        public override Vector2D<float> Measure(Vector2D<float> availableSize)
        {
            // Our own preferred/min size, same logic as base VulkanControl
            float w = preferredWidth > 0 ? preferredWidth : MathF.Max(availableSize.X, minWidth);
            float h = preferredHeight > 0 ? preferredHeight : MathF.Max(availableSize.Y, minHeight);

            if (Content is VulkanControl child)
            {
                float innerW = MathF.Max(0, w - padding.totalHorizontal - Gutter);
                float innerH = MathF.Max(0, h - padding.totalVertical);

                // Key difference from base: we pass our viewport size, not
                // infinity. The child measures against real constraints.
                // Containers (StackPanel etc.) will sum their children and
                // may report a size LARGER than this — that's fine, it means
                // scrolling activates.
                Vector2D<float> childDesired = child.Measure(new Vector2D<float>(innerW, innerH));
                contentSize = childDesired;
            }
            else
            {
                contentSize = new Vector2D<float>(0, 0);
            }

            // We always report our own viewport size, never the child's overflow.
            DesiredSize = new Vector2D<float>(w, h);
            isMeasureDirty = false;
            return DesiredSize;
        }

        // -------------------------------------------------------------------
        //  Arrange: give the child its full desired size (which may exceed
        //  our viewport), but shift it by -scrollOffset so only the visible
        //  portion appears in the clipped viewport.
        // -------------------------------------------------------------------
        public override void Arrange(LayoutRect finalRect)
        {
            arrangedRect = finalRect;

            // Position and scale ourselves normally
            WriteArrangedTransform(finalRect);

            // Clip rect: always intersect with parent since clipOutOfBounds = true
            if (parent is VulkanControl parentControl)
                ClipRect = LayoutRect.Intersect(finalRect, parentControl.ClipRect);
            else
                ClipRect = finalRect;

            // Inner viewport after our padding, less the scrollbar gutter
            LayoutRect innerRect = finalRect.Shrink(padding);
            innerRect.width = MathF.Max(0, innerRect.width - Gutter);
            viewportSize = new Vector2D<float>(innerRect.width, innerRect.height);

            // Clamp scroll offset to valid range
            ClampScrollOffset();

            if (Content is VulkanControl child)
            {
                // Child gets its full content size, but is positioned
                // shifted by the scroll offset. Content that falls outside
                // ClipRect won't render (handled by your existing clip system).
                float childW = CanScrollHorizontal
                    ? MathF.Max(child.DesiredSize.X, innerRect.width)
                    : innerRect.width;
                float childH = CanScrollVertical
                    ? MathF.Max(child.DesiredSize.Y, innerRect.height)
                    : innerRect.height;

                float childX = innerRect.x - scrollOffset.X;
                float childY = innerRect.y - scrollOffset.Y;

                child.Arrange(new LayoutRect(childX, childY, childW, childH));
            }

            ArrangeThumb(finalRect, innerRect);

            isArrangeDirty = false;
        }

        // -------------------------------------------------------------------
        //  The thumb sits in the gutter reserved down the right edge, and
        //  collapses to nothing when the content fits — a zero-scale quad
        //  draws no pixels and fails the hit-test.
        // -------------------------------------------------------------------
        private void ArrangeThumb(LayoutRect finalRect, LayoutRect innerRect)
        {
            if (!CanScrollVertical) return;

            EnsureThumb();

            float maxScroll = MaxScrollOffset.Y;
            if (maxScroll <= 0f || innerRect.height <= 0f)
            {
                ThumbTravel = 0f;
                thumb.Arrange(LayoutRect.Empty);
                return;
            }

            float length = MathF.Max(minThumbLength, innerRect.height * innerRect.height / contentSize.Y);
            length = MathF.Min(length, innerRect.height);
            ThumbTravel = innerRect.height - length;

            thumb.Arrange(new LayoutRect(
                finalRect.Right - barWidth - barInset,
                innerRect.y + ThumbTravel * scrollOffset.Y / maxScroll,
                barWidth,
                length));
        }

        // Inserted at the head, not appended: hit-testing walks children forward and takes the first
        // match, so the thumb has to precede the content to win a press inside it. Rebuilt when it
        // goes missing, because a subclass may clear children to swap its content.
        private void EnsureThumb()
        {
            if (thumb != null && children.Contains(thumb)) return;

            thumb = new ScrollThumbControl(this) { parent = this };
            children.Insert(0, thumb);
            MarkTreeOrderDirty();
        }

        /// <summary>
        /// Call this from your input system when a scroll wheel event hits this control.
        /// deltaX/deltaY are in "ticks" (positive = scroll down/right).
        /// </summary>
        public void OnScrollInput(float deltaX, float deltaY)
        {
            float prevX = scrollOffset.X;
            float prevY = scrollOffset.Y;

            if (CanScrollVertical)
                scrollOffset.Y += deltaY * scrollSensitivity;
            if (CanScrollHorizontal)
                scrollOffset.X += deltaX * scrollSensitivity;

            ClampScrollOffset();

            // Only invalidate arrange if the offset actually changed —
            // sizes haven't changed, just the viewport position
            if (scrollOffset.X != prevX || scrollOffset.Y != prevY)
                InvalidateArrange();
        }

        /// <summary>
        /// Programmatic scroll — sets offset directly in pixels.
        /// </summary>
        public void SetScrollOffset(Vector2D<float> offset)
        {
            scrollOffset = offset;
            ClampScrollOffset();
            InvalidateArrange();
        }

        public Vector2D<float> GetScrollOffset() => scrollOffset;

        /// <summary>
        /// Scroll to make a specific child rect visible within the viewport.
        /// Useful for "scroll to selection" in lists/editors.
        /// </summary>
        public void ScrollIntoView(LayoutRect targetRect)
        {
            LayoutRect innerRect = arrangedRect.Shrink(padding);

            if (CanScrollVertical)
            {
                // Target is above the viewport
                if (targetRect.y < innerRect.y)
                    scrollOffset.Y -= innerRect.y - targetRect.y;
                // Target is below the viewport
                else if (targetRect.Bottom > innerRect.Bottom)
                    scrollOffset.Y += targetRect.Bottom - innerRect.Bottom;
            }

            if (CanScrollHorizontal)
            {
                if (targetRect.x < innerRect.x)
                    scrollOffset.X -= innerRect.x - targetRect.x;
                else if (targetRect.Right > innerRect.Right)
                    scrollOffset.X += targetRect.Right - innerRect.Right;
            }

            ClampScrollOffset();
            InvalidateArrange();
        }

        private void ClampScrollOffset()
        {
            Vector2D<float> max = MaxScrollOffset;
            scrollOffset.X = MathF.Max(0, MathF.Min(scrollOffset.X, max.X));
            scrollOffset.Y = MathF.Max(0, MathF.Min(scrollOffset.Y, max.Y));
        }

        // -------------------------------------------------------------------
        //  AddChild — single child only, same as base VulkanControl
        // -------------------------------------------------------------------
        public override void AddChild(Entity entity)
        {
            if (entity is not VulkanControl)
                throw new Exception("Child entity must be a VulkanControl");
            if (Content != null)
                throw new Exception("ScrollableControl supports only one child. Wrap multiple children in a container.");
            entity.parent = this;
            children.Add(entity);
            MarkTreeOrderDirty();
            scrollOffset = new Vector2D<float>(0, 0); // reset scroll when content changes
            InvalidateLayout();
        }

        public override bool ResolveOnScrollUp()
        {
            // If a handler is registered, let it consume
            if (onScrollUp != null)
            {
                onScrollUp.Invoke();
                return true;
            }

            // Otherwise consume it ourselves if we can actually scroll up
            if (CanScrollVertical && scrollOffset.Y > 0)
            {
                OnScrollInput(0, -1);
                return true;
            }
            if (CanScrollHorizontal && scrollOffset.X > 0)
            {
                OnScrollInput(-1, 0);
                return true;
            }
            // Can't scroll further — let it bubble to a parent scrollable
            return false;
        }

        public override bool ResolveOnScrollDown()
        {
            if (onScrollDown != null)
            {
                onScrollDown.Invoke();
                return true;
            }
            Vector2D<float> max = MaxScrollOffset;
            if (CanScrollVertical && scrollOffset.Y < max.Y)
            {
                OnScrollInput(0, 1);
                return true;
            }
            if (CanScrollHorizontal && scrollOffset.X < max.X)
            {
                OnScrollInput(1, 0);
                return true;
            }
            return false;
        }
    }
}