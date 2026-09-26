using ArctisAurora.Core.Data;
using ArctisAurora.Core.Diagnostics;
using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.EngineWork;
using ArctisAurora.EngineWork.Rendering;
using System.Diagnostics;
using System.Reflection;

namespace ArctisAurora.Core.UI
{
    // UIElements rows as tree ranges: each row's parent, subtree size and layout kind.
    public static class LayoutEngine
    {
        private static readonly LogChannel Log = LogChannel.For("Layout");

        // the pool order the structure describes
        private static ulong _builtOrder = ulong.MaxValue;

        private static readonly Dictionary<Type, LayoutNodeKind> _kinds = new Dictionary<Type, LayoutNodeKind>();

        // Rewrites parent, count and kind for every row a window's tree reaches, once per order change.
        internal static void BuildStructure()
        {
            DataPool pool = UIEngine.Elements;
            ulong order = pool.OrderVersion;
            if (order == _builtOrder) return;
            _builtOrder = order;

            Profiling.Zone.Start("Layout.Structure");
            Span<LayoutNode> nodes = pool.GetSpan<LayoutNode>();
            for (int i = 0; i < nodes.Length; i++)
                nodes[i].count = 0;

            HashSet<Control> walked = new HashSet<Control>();
            foreach (RenderWindow window in Engine.windows.Values)
            {
                WindowRoot? root = window.ui?.uiRoot;
                if (root != null && walked.Add(root))
                    Walk(root, -1, nodes, pool);
            }
            Profiling.Zone.End("Layout.Structure");

            VerifyStructure(walked, nodes, pool);
        }

        private static int Walk(Control control, int parent, Span<LayoutNode> nodes, DataPool pool)
        {
            int row = pool.DenseOf(control.dataHandle);
            int count = 1;
            foreach (Entity e in control.children)
                if (e is Control child)
                    count += Walk(child, row, nodes, pool);

            ref LayoutNode node = ref nodes[row];
            node.parent = parent;
            node.count = count;
            node.kind = KindOf(control);
            return count;
        }

        // Custom when the type lays out through its own MeasureCore/ArrangeCore.
        private static LayoutNodeKind KindOf(Control control)
        {
            Type type = control.GetType();
            if (_kinds.TryGetValue(type, out LayoutNodeKind kind)) return kind;

            kind = Overrides(type, "MeasureCore") || Overrides(type, "ArrangeCore") ? LayoutNodeKind.Custom
                 : control is StackPanelControl ? LayoutNodeKind.Stack
                 : LayoutNodeKind.Single;
            _kinds[type] = kind;
            return kind;
        }

        private static bool Overrides(Type type, string method)
        {
            Type declaring = type.GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.DeclaringType!;
            return declaring != typeof(Control) && declaring != typeof(StackPanelControl);
        }

        // DEBUG: every reached row sits at its pre-order position, so its subtree is the rows [row, row + count).
        [Conditional("DEBUG")]
        private static void VerifyStructure(HashSet<Control> roots, Span<LayoutNode> nodes, DataPool pool)
        {
            foreach (Control root in roots)
            {
                int next = pool.DenseOf(root.dataHandle);
                if (!VerifyRange(root, ref next, nodes, pool)) return;
            }
        }

        private static bool VerifyRange(Control control, ref int next, Span<LayoutNode> nodes, DataPool pool)
        {
            int row = pool.DenseOf(control.dataHandle);
            if (row != next)
            {
                Log.Error($"'{control.name}' ({control.GetType().Name}) is row {row}, pre-order puts it at {next}");
                return false;
            }

            next = row + 1;
            foreach (Entity e in control.children)
                if (e is Control child && !VerifyRange(child, ref next, nodes, pool))
                    return false;

            if (next != row + nodes[row].count)
            {
                Log.Error($"'{control.name}' ({control.GetType().Name}) counts {nodes[row].count} rows, its subtree ends at {next}");
                return false;
            }
            return true;
        }
    }
}
