using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.Registry;
using Silk.NET.Maths;
using Silk.NET.Vulkan;

namespace ArctisAurora.Core.UI
{
    // The control a window's tree hangs from. It is the one node that holds siblings, and the one
    // that knows the box the tree is laid out in — every control below it works in that box's units.
    [A_XSDType("NextWindow", "UI")]
    public class WindowRoot : Control
    {
        public enum WindowingMode
        {
            KeepLocal, WindowSize
        }

        public enum ScalingAxis
        {
            Vertical, Horizontal, HorizontalExclusive, VerticalExclusive, Both
        }

        // fitting
        public WindowingMode windowingMode = WindowingMode.WindowSize;
        public bool autoscaling = false;
        public ScalingAxis scalingAxis = ScalingAxis.Vertical;

        // A root is structural. Until the sampler set lands there is no invisible mask to opt out
        // with, so it stays transparent instead — an opaque root covers the whole window.
        public WindowRoot()
        {
            alpha = 0f;
        }

        // The box the tree is laid out and projected in. Window pixels unless the content scales,
        // in which case it is the window divided by the scale the chosen axis implies.
        public Vector2D<float> ViewportSize(Extent2D window)
        {
            if (!autoscaling || windowingMode == WindowingMode.KeepLocal
                || preferredWidth <= 0 || preferredHeight <= 0)
                return new Vector2D<float>(window.Width, window.Height);

            switch (scalingAxis)
            {
                case ScalingAxis.Both:
                    return new Vector2D<float>(preferredWidth, preferredHeight);
                case ScalingAxis.Vertical:
                    return new Vector2D<float>(window.Width * preferredHeight / window.Height, preferredHeight);
                case ScalingAxis.Horizontal:
                    return new Vector2D<float>(preferredWidth, window.Height * preferredWidth / window.Width);
                case ScalingAxis.HorizontalExclusive:
                    return new Vector2D<float>(preferredWidth, window.Height);
                case ScalingAxis.VerticalExclusive:
                    return new Vector2D<float>(window.Width, preferredHeight);
                default:
                    return new Vector2D<float>(window.Width, window.Height);
            }
        }

        // Re-lays the tree into the window. KeepLocal keeps the size it was given.
        public void FitTo(Extent2D window)
        {
            if (windowingMode == WindowingMode.KeepLocal) return;
            if (window.Width == 0 || window.Height == 0) return;

            Vector2D<float> box = ViewportSize(window);
            WriteArranged(new LayoutRect(0, 0, box.X, box.Y));

            SetFlag(ArrangeFlags.MeasureDirty, true);
            SetFlag(ArrangeFlags.ArrangeDirty, true);
            UIEngine.RegisterDirtyRoot(this);
        }

        // Window pixels to the units the tree is laid out in — identity unless the content scales.
        public Vector2D<float> ToDesignSpace(Vector2D<float> windowPoint, Extent2D window)
        {
            if (window.Width == 0 || window.Height == 0) return windowPoint;

            Vector2D<float> box = ViewportSize(window);
            return new Vector2D<float>(windowPoint.X * box.X / window.Width,
                                       windowPoint.Y * box.Y / window.Height);
        }

        // The root measures at the box it was fitted to. preferredWidth/Height are the design size
        // ViewportSize reads, not a cap the tree inherits.
        public override Vector2D<float> Measure(Vector2D<float> availableSize)
        {
            if (windowingMode == WindowingMode.KeepLocal) return base.Measure(availableSize);

            Thickness pad = arrange.padding;
            Vector2D<float> inner = new Vector2D<float>(
                MathF.Max(0, availableSize.X - pad.totalHorizontal),
                MathF.Max(0, availableSize.Y - pad.totalVertical));

            foreach (Entity e in children)
                if (e is Control child)
                    child.Measure(inner);

            arrange.desired = availableSize;
            SetFlag(ArrangeFlags.MeasureDirty, false);
            return availableSize;
        }

        public override void Arrange(LayoutRect finalRect)
        {
            WriteArranged(finalRect);

            LayoutRect inner = finalRect.Shrink(arrange.padding);

            foreach (Entity e in children)
            {
                if (e is not Control child) continue;

                ref ArrangeData ca = ref child.arrange;
                HorizontalAlignment ha = (HorizontalAlignment)ca.horizontalAlignment;
                VerticalAlignment va = (VerticalAlignment)ca.verticalAlignment;

                float childW = ha == HorizontalAlignment.Stretch
                    ? inner.width
                    : MathF.Min(ca.desired.X, inner.width);

                float childH = va == VerticalAlignment.Stretch
                    ? inner.height
                    : MathF.Min(ca.desired.Y, inner.height);

                float childX = ha switch
                {
                    HorizontalAlignment.Left => inner.x,
                    HorizontalAlignment.Right => inner.x + inner.width - childW,
                    HorizontalAlignment.Center => inner.x + (inner.width - childW) * 0.5f,
                    _ => inner.x,
                };

                float childY = va switch
                {
                    VerticalAlignment.Top => inner.y,
                    VerticalAlignment.Bottom => inner.y + inner.height - childH,
                    VerticalAlignment.Center => inner.y + (inner.height - childH) * 0.5f,
                    _ => inner.y,
                };

                child.Arrange(new LayoutRect(childX, childY, childW, childH));
            }

            SetFlag(ArrangeFlags.ArrangeDirty, false);
        }

        public override void AddChild(Entity entity)
        {
            if (entity is not Control)
                throw new Exception("Child entity must be a Control");

            children.Add(entity);
            entity.parent = this;
            MarkTreeOrderDirty();
            InvalidateLayout();
        }
    }
}
