using ArctisAurora.Core.Data;
using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.UISystem.Controls;
using Silk.NET.Maths;
using static ArctisAurora.Core.UISystem.Controls.VulkanControl;

namespace ArctisAurora.Core.UISystem
{
    public class UILayout
    {
        private static readonly HashSet<VulkanControl> _dirtyRoots = new HashSet<VulkanControl>();

        // Canonical UIControls dense order = DFS pre-order of the control tree (parent before
        // children = painter order AND layout-dependency order). Resolved by DataManager for the
        // pool's SortAction="UI.DFSOrder"; returns the live stableIds in the desired dense order.
        // Registered but only consumed when something marks the pool orderDirty (insert / reparent
        // / bring-to-front) — plain adds and destroys keep dense order without it.
        [A_XSDActionDependency("UI.DFSOrder", "PoolSort")]
        public static IReadOnlyList<int> DFSOrder(DataPool pool)
        {
            int count = pool.Count;
            List<int> order = new List<int>(count);
            for (int i = 0; i < count; i++)
            {
                // roots = live controls with no VulkanControl parent; DFS each in dense-scan order
                if (pool.OwnerAt(i) is VulkanControl control && control.parent is not VulkanControl)
                    CollectDFS(control, order);
            }
            return order;
        }

        private static void CollectDFS(VulkanControl control, List<int> order)
        {
            order.Add(control.dataHandle.StableId);
            foreach (Entity child in control.children)
                if (child is VulkanControl childControl)
                    CollectDFS(childControl, order);
        }

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
                if (root.isMeasureDirty)
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
                        ? new LayoutRect(root.transform.position.X,
                                         root.transform.position.Y,
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