using ArctisAurora.EngineWork.Rendering.UI.Controls;
using Silk.NET.Maths;
using static ArctisAurora.EngineWork.Rendering.UI.Controls.VulkanControl;

namespace ArctisAurora.Core.UISystem
{
    public class UILayout
    {
        private static readonly HashSet<VulkanControl> _dirtyRoots = new HashSet<VulkanControl>();

        public static void RegisterDirtyRoot(VulkanControl vulkanControl)
        {
            _dirtyRoots.Add(vulkanControl);
        }

        public static void ResolveLayout()
        {
            if (_dirtyRoots.Count < 0) return;

            VulkanControl[] roots = new VulkanControl[_dirtyRoots.Count];
            _dirtyRoots.CopyTo(roots);
            _dirtyRoots.Clear();

            foreach (VulkanControl root in roots)
            {
                if (root.IsMeasureDirty)
                {
                    // Pass 1 — offer the root its own current arranged size (or infinite
                    // if it has never been arranged, meaning it's a window root).
                    Vector2D<float> offer = root.arrangedRect.size == Vector2D<float>.Zero
                        ? new Vector2D<float>(float.MaxValue, float.MaxValue)
                        : root.arrangedRect.size;

                    root.Measure(offer);

                    // Pass 2 — re-arrange from the root's current rect.
                    // Window roots have their ArrangedRect set externally (on window resize).
                    LayoutRect finalRect = root.arrangedRect.size == Vector2D<float>.Zero
                        ? new LayoutRect(root.transform.GetEntityPosition().X,
                                         root.transform.GetEntityPosition().Y,
                                         root.DesiredSize.X,
                                         root.DesiredSize.Y)
                        : root.arrangedRect;

                    root.Arrange(finalRect);
                }
                else if (root.isArrangeDirty)
                {
                    // Position-only change — skip measure, re-arrange from existing rect.
                    root.Arrange(root.arrangedRect);
                }
            }
        }
    }
}