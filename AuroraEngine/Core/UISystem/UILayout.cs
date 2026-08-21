using ArctisAurora.Core.Data;
using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.UISystem.Controls;
using ArctisAurora.EngineWork;
using ArctisAurora.EngineWork.Rendering;
using ArctisAurora.EngineWork.Rendering.Modules;
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

            HashSet<VulkanControl> windowRoots = new HashSet<VulkanControl>();
            foreach (RenderWindow window in Engine.windows.Values)
            {
                WindowControl root = window.ui.uiRoot;
                if (root == null || !windowRoots.Add(root)) continue;
                CollectDFS(root, order);
            }

            // Detached subtrees are roots too, and no window draws them. They follow every window so
            // the window ranges stay contiguous from zero.
            for (int i = 0; i < count; i++)
            {
                if (pool.OwnerAt(i) is VulkanControl control && control.parent is not VulkanControl
                    && !windowRoots.Contains(control))
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

        // ---- per-window instance ranges ----

        private static DataPool? _controlPool;
        private static ulong _rangesStructural = ulong.MaxValue;
        private static ulong _rangesOrder = ulong.MaxValue;
        private static bool _rangesDirty = true;

        // A root swap moves no pool rows, so no pool version would report it.
        public static void InvalidateWindowRanges() => _rangesDirty = true;

        // Publishes each window module's slice of the shared control pool. Dense order is DFS with
        // the windows first in Engine.windows order, so a window's subtree is the contiguous run
        // starting where the previous window's ended. Runs at the frame edge, after the pool has
        // compacted and resequenced — never per frame.
        public static void RefreshWindowRanges()
        {
            DataPool pool = _controlPool ??= DataManager.Get("UIControls");
            if (!_rangesDirty && pool.StructuralVersion == _rangesStructural && pool.OrderVersion == _rangesOrder)
                return;

            _rangesStructural = pool.StructuralVersion;
            _rangesOrder = pool.OrderVersion;
            _rangesDirty = false;

            int first = 0;
            foreach (RenderWindow window in Engine.windows.Values)
            {
                if (window.isGhost) continue;

                UIModule ui = window.ui;
                int count = ui.uiRoot == null ? 0 : CountSubtree(ui.uiRoot);
                Publish(ui, first, count);
                first += count;
            }

            // A ghost draws a control that lives in some other window's tree, so its range is that
            // subtree's own — contiguous, because dense order is DFS.
            foreach (RenderWindow window in Engine.windows.Values)
            {
                if (!window.isGhost) continue;

                UIModule ui = window.ui;
                Publish(ui,
                    ui.rangeRoot == null ? 0 : pool.DenseOf(ui.rangeRoot.dataHandle),
                    ui.rangeRoot == null ? 0 : CountSubtree(ui.rangeRoot));
            }
        }

        // The range rides in the recorded command buffer, so a module whose range moved has to record
        // again. A ghost's range moves with no pool version behind it, which nothing else would catch.
        private static void Publish(UIModule ui, int first, int count)
        {
            if (ui.firstInstance == first && ui.instanceCount == count) return;

            ui.firstInstance = first;
            ui.instanceCount = count;

            if (ui.isDirty == null) return;
            for (int i = 0; i < ui.isDirty.Length; i++)
                ui.isDirty[i] = true;
        }

        private static int CountSubtree(VulkanControl control)
        {
            int count = 1;
            foreach (Entity child in control.children)
                if (child is VulkanControl childControl)
                    count += CountSubtree(childControl);
            return count;
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