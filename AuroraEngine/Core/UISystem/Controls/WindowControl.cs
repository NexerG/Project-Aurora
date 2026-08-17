using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Data;
using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.EngineWork;
using ArctisAurora.EngineWork.Registry;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using ArctisAurora.Core.Registry.Assets;

namespace ArctisAurora.Core.UISystem.Controls
{

    [A_XSDType("Window", "UI", AllowedChildren = typeof(IXMLChild_UI), MaxChildren = 1)]
    public class WindowControl : VulkanControl
    {
        [A_XSDType("WindowingModeEnum", "UI")]
        public enum WindowingMode
        {
            KeepLocal, ScaleUp, WindowSize
        }
        [A_XSDElementProperty("WindowingMode", "UI")]
        public WindowingMode windowingMode = WindowingMode.WindowSize;

        [A_XSDType("ContentScalingModeEnum", "UI")]
        public enum ScalingMode
        {
            Vertical, Horizontal, Both, None
        }
        [A_XSDElementProperty("ContentScalingMode", "UI")]
        public ScalingMode contentScalingMode = ScalingMode.Vertical;

        public WindowControl()
        {
            maskAsset = AssetRegistries.GetAsset<TextureAsset>("invisible");
        }

        // The box the tree is laid out and projected in. Window pixels unless the content scales,
        // in which case it is the window divided by the scale the chosen axis implies.
        public Vector2D<float> ViewportSize(Extent2D window)
        {
            if (windowingMode != WindowingMode.ScaleUp || preferredWidth <= 0 || preferredHeight <= 0)
                return new Vector2D<float>(window.Width, window.Height);

            switch (contentScalingMode)
            {
                case ScalingMode.Both:
                    return new Vector2D<float>(preferredWidth, preferredHeight);
                case ScalingMode.Vertical:
                    return new Vector2D<float>(window.Width * preferredHeight / window.Height, preferredHeight);
                case ScalingMode.Horizontal:
                    return new Vector2D<float>(preferredWidth, window.Height * preferredWidth / window.Width);
                default:
                    return new Vector2D<float>(window.Width, window.Height);
            }
        }

        // Re-lays the tree into the window. KeepLocal keeps the size the XML gave it.
        public void FitTo(Extent2D window)
        {
            if (windowingMode == WindowingMode.KeepLocal) return;
            if (window.Width == 0 || window.Height == 0) return;

            Vector2D<float> box = ViewportSize(window);
            arrangedRect = new LayoutRect(0, 0, box.X, box.Y);

            ref TransformData t = ref transform;
            t.position = new Vector3D<float>(box.X / 2f, box.Y / 2f, t.position.Z);
            t.scale = new Vector3D<float>(box.X, box.Y, 1);
            CommitTransform();

            isMeasureDirty = true;
            isArrangeDirty = true;
            UILayout.RegisterDirtyRoot(this);
        }

        // Window pixels to the units the tree is laid out in — identity unless the content scales.
        public static Vector2D<float> ToDesignSpace(Vector2D<float> windowPoint)
        {
            if (EntityRegistry.uiTree is not WindowControl root) return windowPoint;

            Extent2D window = Engine.window.windowSize;
            if (window.Width == 0 || window.Height == 0) return windowPoint;

            Vector2D<float> box = root.ViewportSize(window);
            return new Vector2D<float>(windowPoint.X * box.X / window.Width,
                                       windowPoint.Y * box.Y / window.Height);
        }

        // The root measures at the box it was fitted to. The XML Width/Height are the design size
        // that ViewportSize reads, not a cap the tree inherits.
        public override Vector2D<float> Measure(Vector2D<float> availableSize)
        {
            if (windowingMode == WindowingMode.KeepLocal) return base.Measure(availableSize);

            Vector2D<float> inner = new Vector2D<float>(
                MathF.Max(0, availableSize.X - padding.totalHorizontal),
                MathF.Max(0, availableSize.Y - padding.totalVertical));

            foreach (Entity e in children)
                if (e is VulkanControl child)
                    child.Measure(inner);

            DesiredSize = availableSize;
            isMeasureDirty = false;
            return DesiredSize;
        }

        public override void Arrange(LayoutRect finalRect)
        {
            arrangedRect = finalRect;

            WriteArrangedTransform(finalRect);

            LayoutRect inner = finalRect.Shrink(padding);

            foreach (Entity e in children)
            {
                if (e is not VulkanControl child) continue;

                // Compute child size respecting alignment
                float childW = child.horizontalAlignment == HorizontalAlignment.Stretch
                    ? inner.width
                    : MathF.Min(child.DesiredSize.X, inner.width);

                float childH = child.verticalAlignment == VerticalAlignment.Stretch
                    ? inner.height
                    : MathF.Min(child.DesiredSize.Y, inner.height);

                // Compute child position within the window's inner rect
                float childX = child.horizontalAlignment switch
                {
                    HorizontalAlignment.Left => inner.x,
                    HorizontalAlignment.Right => inner.x + inner.width - childW,
                    HorizontalAlignment.Center => inner.x + (inner.width - childW) * 0.5f,
                    _ => inner.x,  // Stretch
                };

                float childY = child.verticalAlignment switch
                {
                    VerticalAlignment.Top => inner.y,
                    VerticalAlignment.Bottom => inner.y + inner.height - childH,
                    VerticalAlignment.Center => inner.y + (inner.height - childH) * 0.5f,
                    _ => inner.y,  // Stretch
                };

                child.Arrange(new LayoutRect(childX, childY, childW, childH));
            }

            isArrangeDirty = false;
        }
    }
}