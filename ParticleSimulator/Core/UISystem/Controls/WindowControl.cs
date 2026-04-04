using ArctisAurora.Core.AssetRegistry;
using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.EngineWork.AssetRegistry;
using Silk.NET.Maths;

namespace ArctisAurora.Core.UISystem.Controls
{

    [A_XSDType("Window", "UI", AllowedChildren = typeof(IXMLChild_UI), MaxChildren = 1)]
    public class WindowControl : VulkanControl
    {
        [A_XSDElementProperty("Fill", "UI")]
        public bool fillWindow = true;

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

        public override void Arrange(LayoutRect finalRect)
        {
            arrangedRect = finalRect;

            transform.SetWorldPosition(new Vector3D<float>(
                finalRect.x + finalRect.width / 2f,
                finalRect.y + finalRect.height / 2f,
                transform.GetEntityPosition().Z));
            transform.SetWorldScale(new Vector3D<float>(finalRect.width, finalRect.height, 1));

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