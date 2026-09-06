using ArctisAurora.Core.Data;
using ArctisAurora.Core.Diagnostics;
using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.Registry;
using ArctisAurora.EngineWork;
using ArctisAurora.EngineWork.Rendering;
using ArctisAurora.EngineWork.Rendering.Modules;
using Silk.NET.Maths;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace ArctisAurora.Core.UI
{
    // Entry point of the UI, on the main thread. Poll() arrives at landing 3.
    public static class UIEngine
    {
        private static readonly LogChannel Log = LogChannel.For("UIEngine");

        private static DataPool _elements;
        private static DataPool _controls;

        public static DataPool Elements => _elements ??= DataManager.Get("UIElements");
        public static DataPool Controls => _controls ??= DataManager.Get("VulkanControls");

        #region ---- layout ----
        private static readonly HashSet<Control> _dirtyRoots = new HashSet<Control>();

        public static void RegisterDirtyRoot(Control control) => _dirtyRoots.Add(control);

        // Measures and arranges every dirty subtree, then refreshes its collision caches.
        public static void ResolveLayout()
        {
            if (_dirtyRoots.Count == 0) return;

            Control[] roots = new Control[_dirtyRoots.Count];
            _dirtyRoots.CopyTo(roots);
            _dirtyRoots.Clear();

            foreach (Control root in roots)
            {
                if (root.isMeasureDirty)
                {
                    // Pass 1 — offer the root its own current arranged size, or infinite if it has
                    // never been arranged.
                    Vector2D<float> offer = root.arrangedRect.size == Vector2D<float>.Zero
                        ? new Vector2D<float>(float.MaxValue, float.MaxValue)
                        : root.arrangedRect.size;

                    root.Measure(offer);

                    // Pass 2 — re-arrange from the root's current rect. A window root is fitted
                    // externally, on resize.
                    LayoutRect finalRect = root.arrangedRect.size == Vector2D<float>.Zero
                        ? new LayoutRect(0, 0, root.DesiredSize.X, root.DesiredSize.Y)
                        : root.arrangedRect;

                    root.Arrange(finalRect);
                }
                else if (root.isArrangeDirty)
                {
                    root.Arrange(root.arrangedRect);
                }

                root.RefreshSubtreeCache();
                VerifySubtreeCache(root);
            }
        }

        // Recomputes the caches independently and reports a mismatch. The maintained values are the
        // hit-test's early-out, so a wrong one loses clicks rather than drawing anything odd.
        [Conditional("DEBUG")]
        private static void VerifySubtreeCache(Control control)
        {
            LayoutRect bounds = control.arrangedRect;
            int count = 1;

            foreach (Entity e in control.children)
            {
                if (e is not Control child) continue;

                VerifySubtreeCache(child);
                bounds = LayoutRect.Union(bounds, child.arrange.subtreeBounds);
                count += child.arrange.subtreeCount;
            }

            ref ArrangeData a = ref control.arrange;
            if (a.subtreeCount != count)
                Log.Error($"'{control.name}' subtreeCount {a.subtreeCount} != recomputed {count}");
            if (a.subtreeBounds.x != bounds.x || a.subtreeBounds.y != bounds.y
                || a.subtreeBounds.width != bounds.width || a.subtreeBounds.height != bounds.height)
                Log.Error($"'{control.name}' subtreeBounds ({a.subtreeBounds.x}, {a.subtreeBounds.y}, " +
                          $"{a.subtreeBounds.width}, {a.subtreeBounds.height}) != recomputed " +
                          $"({bounds.x}, {bounds.y}, {bounds.width}, {bounds.height})");
        }
        #endregion

        #region ---- dense order ----
        // Canonical dense order for both pools = DFS pre-order of the control tree, which is painter
        // order and layout-dependency order at once. UIElements is keyed by the entity's own handle,
        // VulkanControls by the draw row it allocated, so the walk is shared and the key is not.
        [A_XSDActionDependency("UI.NextElementOrder", "PoolSort")]
        public static IReadOnlyList<int> NextElementOrder(DataPool pool) => DFSOrder(pool, true);

        [A_XSDActionDependency("UI.NextControlOrder", "PoolSort")]
        public static IReadOnlyList<int> NextControlOrder(DataPool pool) => DFSOrder(pool, false);

        private static IReadOnlyList<int> DFSOrder(DataPool pool, bool elements)
        {
            int count = pool.Count;
            List<int> order = new List<int>(count);

            HashSet<Control> windowRoots = new HashSet<Control>();
            foreach (RenderWindow window in Engine.windows.Values)
            {
                WindowRoot root = window.uiNext?.uiRoot;
                if (root == null || !windowRoots.Add(root)) continue;
                CollectDFS(root, order, elements);
            }

            // Detached subtrees are roots too, and no window draws them. They follow every window so
            // the window ranges stay contiguous from zero.
            for (int i = 0; i < count; i++)
            {
                if (pool.OwnerAt(i) is Control control && control.parent is not Control
                    && !windowRoots.Contains(control))
                    CollectDFS(control, order, elements);
            }
            return order;
        }

        private static void CollectDFS(Control control, List<int> order, bool elements)
        {
            order.Add(elements ? control.dataHandle.StableId : control.controlHandle.StableId);
            foreach (Entity child in control.children)
                if (child is Control childControl)
                    CollectDFS(childControl, order, elements);
        }
        #endregion

        #region ---- per-window instance ranges ----
        private static ulong _rangesStructural = ulong.MaxValue;
        private static ulong _rangesOrder = ulong.MaxValue;
        private static bool _rangesDirty = true;

        // A root swap moves no pool rows, so no pool version would report it.
        public static void InvalidateWindowRanges() => _rangesDirty = true;

        // Publishes each window module's slice of the shared draw pool. Dense order is DFS with the
        // windows in Engine.windows order, so a window's subtree is the contiguous run starting where
        // the previous window's ended. Runs at the frame edge, never per frame.
        public static void RefreshWindowRanges()
        {
            DataPool pool = Controls;
            if (!_rangesDirty && pool.StructuralVersion == _rangesStructural && pool.OrderVersion == _rangesOrder)
                return;

            _rangesStructural = pool.StructuralVersion;
            _rangesOrder = pool.OrderVersion;
            _rangesDirty = false;

            int first = 0;
            foreach (RenderWindow window in Engine.windows.Values)
            {
                UIEngineModule ui = window.uiNext;
                int count = ui.uiRoot == null ? 0 : CountSubtree(ui.uiRoot);
                Publish(ui, first, count);
                first += count;
            }
        }

        // The range rides in the recorded command buffer, so a module whose range moved has to
        // record again.
        private static void Publish(UIEngineModule ui, int first, int count)
        {
            if (ui.firstInstance == first && ui.instanceCount == count) return;

            ui.firstInstance = first;
            ui.instanceCount = count;

            if (ui.isDirty == null) return;
            for (int i = 0; i < ui.isDirty.Length; i++)
                ui.isDirty[i] = true;
        }

        private static int CountSubtree(Control control)
        {
            int count = 1;
            foreach (Entity child in control.children)
                if (child is Control childControl)
                    count += CountSubtree(childControl);
            return count;
        }
        #endregion

        [A_XSDActionDependency("UIEngine.Bootstrap", "Bootstrap")]
        public static bool Bootstrap()
        {
            Log.Info($"row sizes — ArrangeData {Unsafe.SizeOf<ArrangeData>()} B, " +
                     $"ControlGeometry {Unsafe.SizeOf<ControlGeometry>()} B, " +
                     $"VulkanControl {Unsafe.SizeOf<VulkanControl>()} B");

            BuildScaffolding(Engine.primary);
            return true;
        }

        // Landing 2 scaffolding: nesting, padding, margin, alignment and a clipped overflow, so the
        // arrange pass has something to be wrong about. Built back to front, so pool allocation
        // order is nothing like DFS order and the resequence has to earn its keep. The port at
        // landing 6 removes it.
        private static void BuildScaffolding(RenderWindow window)
        {
            Control bar = new Control
            {
                name = "bar",
                preferredHeight = 48,
                horizontalAlignment = HorizontalAlignment.Stretch,
                verticalAlignment = VerticalAlignment.Bottom,
                colorHex = "#06D6A0",
                cornerRadius = 6f
            };

            Control clipped = new Control
            {
                name = "clipped",
                preferredWidth = 200,
                preferredHeight = 140,
                horizontalAlignment = HorizontalAlignment.Right,
                verticalAlignment = VerticalAlignment.Top,
                clipOutOfBounds = true,
                colorHex = "#2A2F3A",
                cornerRadius = 8f
            };
            Control overflow = new Control
            {
                name = "overflow",
                preferredWidth = 320,
                preferredHeight = 260,
                colorHex = "#EF476F"
            };
            clipped.AddChild(overflow);

            Control card = new Control
            {
                name = "card",
                preferredWidth = 360,
                preferredHeight = 220,
                padding = new Thickness(16),
                horizontalAlignment = HorizontalAlignment.Left,
                verticalAlignment = VerticalAlignment.Top,
                colorHex = "#3AA6FF",
                cornerRadius = 16f,
                edgeColorHex = "#FFFFFF",
                edgeThickness = 2f
            };
            Control inner = new Control
            {
                name = "inner",
                padding = new Thickness(12),
                colorHex = "#12314A",
                cornerRadius = 8f
            };
            Control leaf = new Control
            {
                name = "leaf",
                preferredWidth = 120,
                preferredHeight = 60,
                colorHex = "#FFD166",
                cornerRadius = 4f
            };
            inner.AddChild(leaf);
            card.AddChild(inner);

            WindowRoot root = new WindowRoot { name = "root", padding = new Thickness(24) };
            root.AddChild(card);
            root.AddChild(clipped);
            root.AddChild(bar);

            window.uiNext.uiRoot = root;
        }
    }
}
