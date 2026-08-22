using ArctisAurora.Core.Filing.Serialization;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.UISystem.Controls;
using Silk.NET.Maths;
using System.Reflection;
using System.Xml.Linq;

namespace ArctisAurora.Core.UISystem
{
    [A_XSDType("MenuItem", "UI")]
    public class ContextMenuItemDefinition
    {
        [A_XSDElementProperty("Caption", "UI", "Text shown for this entry.")]
        public string caption = "";
        [A_XSDElementProperty("Action", "UI", "Action run on release. Takes nothing, or the control that was right-clicked.")]
        public string action = "";
        [A_XSDElementProperty("EnabledWhen", "UI", "Predicate deciding whether the entry is live. Always live when unset.")]
        public string enabledWhen = "";

        // bound at load, so a bad name fails the boot rather than the right-click
        internal Action? invoke;
        internal Action<VulkanControl>? invokeOnTarget;
        internal Func<VulkanControl, bool>? isEnabled;
    }

    [A_XSDType("ContextMenu", "UI", AllowedChildren = typeof(ContextMenuItemDefinition))]
    public class ContextMenuDefinition
    {
        [A_XSDElementProperty("Name", "UI", "Name a control references this menu by.")]
        public string name = "";

        [A_XSDElementProperty("MenuItem", "UI", "")]
        public List<ContextMenuItemDefinition> items = new List<ContextMenuItemDefinition>();
    }

    [A_XSDType("ContextMenus", "UI", AllowedChildren = typeof(ContextMenuDefinition), Description = "Root container for named context menu definitions")]
    public class ContextMenuMap { }

    // One line of an open menu. The action is already bound to whatever was right-clicked, so
    // invoking it needs no further context.
    public readonly struct ContextEntry
    {
        public readonly string caption;
        public readonly Action invoke;
        public readonly bool enabled;
        public readonly bool separatorBefore;

        public ContextEntry(string caption, Action invoke, bool enabled, bool separatorBefore)
        {
            this.caption = caption;
            this.invoke = invoke;
            this.enabled = enabled;
            this.separatorBefore = separatorBefore;
        }
    }

    // Collects the entries of one control's menu. Every entry acts on that control, so nothing has
    // to work out what the pointer was over.
    public class ContextMenuBuilder
    {
        public VulkanControl owner { get; }

        internal readonly List<ContextEntry> entries = new List<ContextEntry>();

        private bool pendingSeparator;

        internal ContextMenuBuilder(VulkanControl owner)
        {
            this.owner = owner;
        }

        // A new named menu starts, so its first entry carries the divider.
        internal void BeginMenu() => pendingSeparator = entries.Count > 0;

        public void Add(string caption, Action? invoke, bool enabled = true)
        {
            // captured, so an action taking no argument can still find what the menu was opened on
            VulkanControl target = owner;
            entries.Add(new ContextEntry(caption, () => ContextMenus.InvokeFor(target, invoke), enabled, pendingSeparator));
            pendingSeparator = false;
        }

        // An authored entry. The owner is closed over here, and EnabledWhen is asked once, now.
        internal void Add(ContextMenuItemDefinition item)
        {
            VulkanControl target = owner;
            Action<VulkanControl>? onTarget = item.invokeOnTarget;
            Action? invoke = onTarget != null ? () => onTarget(target) : item.invoke;
            Add(item.caption, invoke, item.isEnabled == null || item.isEnabled(target));
        }
    }

    // Named menus authored in ContextMenus.xml. A control names one and the walk up the tree
    // concatenates them, so an entry declared once is reachable from every control under it.
    public static class ContextMenus
    {
        private static readonly Dictionary<string, ContextMenuDefinition> menus =
            new Dictionary<string, ContextMenuDefinition>(StringComparer.OrdinalIgnoreCase);

        // The control the running entry's menu was opened on. Null unless an entry is mid-invoke.
        public static VulkanControl? invoker { get; private set; }

        // The one live menu — one pointer, so one open at a time. Kept between opens, because a
        // windowed one owns a window that must not be rebuilt per right click.
        private static ContextMenuControl? live;

        // What a right click builds. Menus open in the window they came from unless a host swaps this
        // for a windowed one at startup.
        public static Func<ContextMenuControl> menuFactory = () => new ContextMenuControl();

        public static bool Open(VulkanControl owner)
        {
            live ??= menuFactory();
            return live.Open(owner);
        }

        public static void Tick() => live?.Tick();

        // The menu the hit-test runs against in place of this tree's root. An open menu is the
        // top-most thing drawn, and the walk takes the first child that hits rather than the last, so
        // it would otherwise be the one thing under the pointer that cannot be reached.
        internal static ContextMenuControl? OpenIn(VulkanControl root)
        {
            if (live == null || !live.isOpen) return null;

            VulkanControl top = live;
            while (top.parent is VulkanControl parent) top = parent;

            return ReferenceEquals(top, root) ? live : null;
        }

        // A press outside an open menu takes it down and goes no further — it dismisses, it does not
        // also reach whatever it landed on. Asked of the tree the press landed in, because the point
        // and the menu's rect are only in the same space when the menu hangs in that tree.
        internal static bool DismissedBy(VulkanControl root, Vector2D<float> point)
        {
            ContextMenuControl? menu = OpenIn(root);
            if (menu == null || menu.arrangedRect.Contains(point)) return false;

            menu.Close();
            return true;
        }

        public static ContextMenuDefinition Get(string name) => string.IsNullOrEmpty(name) ? null : menus.GetValueOrDefault(name);

        // Runs an entry with its owner published.
        internal static void InvokeFor(VulkanControl target, Action? invoke)
        {
            if (invoke == null) return;

            invoker = target;
            try { invoke(); }
            finally { invoker = null; }
        }

        // Everything one control offers: each menu it names, in order and divided, then whatever it
        // adds in code. An unknown name contributes nothing.
        public static List<ContextEntry> Compose(VulkanControl control)
        {
            ContextMenuBuilder builder = new ContextMenuBuilder(control);

            foreach (string name in control.contextMenus.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                ContextMenuDefinition menu = Get(name);
                if (menu == null) continue;

                builder.BeginMenu();
                foreach (ContextMenuItemDefinition item in menu.items)
                    builder.Add(item);
            }

            builder.BeginMenu();
            control.BuildContextMenu(builder);

            return builder.entries;
        }

        [A_XSDActionDependency("ContextMenus.LoadMenus", "Bootstrap")]
        public static bool LoadMenus()
        {
            menus.Clear();
            XElement root = XElement.Load(Paths.Doc("ContextMenus.xml"));

            foreach (XElement element in root.Elements())
            {
                ContextMenuDefinition menu = new ContextMenuDefinition { name = element.Attribute("Name")?.Value ?? "" };

                foreach (XElement itemElement in element.Elements())
                    menu.items.Add(ParseItem(itemElement));

                menus[menu.name] = menu;
            }

            return true;
        }

        private static ContextMenuItemDefinition ParseItem(XElement element)
        {
            ContextMenuItemDefinition item = new ContextMenuItemDefinition
            {
                caption = element.Attribute("Caption")?.Value ?? "",
                action = element.Attribute("Action")?.Value ?? "",
                enabledWhen = element.Attribute("EnabledWhen")?.Value ?? ""
            };

            BindAction(item);
            BindPredicate(item);
            return item;
        }

        // An entry's action takes nothing, or the control that was right-clicked.
        private static void BindAction(ContextMenuItemDefinition item)
        {
            if (string.IsNullOrEmpty(item.action)) return;

            MethodInfo method = FindAction(item.action);
            if (TakesTarget(method))
                item.invokeOnTarget = (Action<VulkanControl>)Delegate.CreateDelegate(typeof(Action<VulkanControl>), method);
            else
                item.invoke = (Action)Delegate.CreateDelegate(typeof(Action), method);
        }

        // The predicate form of the same lookup — bool return, evaluated once each time a menu opens.
        private static void BindPredicate(ContextMenuItemDefinition item)
        {
            if (string.IsNullOrEmpty(item.enabledWhen)) return;

            MethodInfo method = FindAction(item.enabledWhen);
            if (method.ReturnType != typeof(bool))
                throw new Exception($"EnabledWhen '{item.enabledWhen}' must return bool.");

            if (TakesTarget(method))
            {
                item.isEnabled = (Func<VulkanControl, bool>)Delegate.CreateDelegate(typeof(Func<VulkanControl, bool>), method);
                return;
            }

            Func<bool> global = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), method);
            item.isEnabled = _ => global();
        }

        private static bool TakesTarget(MethodInfo method)
        {
            ParameterInfo[] parameters = method.GetParameters();
            return parameters.Length == 1 && parameters[0].ParameterType == typeof(VulkanControl);
        }

        private static MethodInfo FindAction(string name)
        {
            MethodInfo method = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => a.GetTypes())
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                .FirstOrDefault(m =>
                {
                    A_XSDActionDependencyAttribute dependency = m.GetCustomAttribute<A_XSDActionDependencyAttribute>();
                    return dependency != null && string.Equals(dependency.Name, name, StringComparison.OrdinalIgnoreCase);
                });

            if (method == null)
                throw new Exception($"Action method '{name}' not found in A_XSDActionDependency.");
            if (!method.IsStatic)
                throw new Exception($"Action method '{name}' must be static — a menu entry binds no instance.");

            return method;
        }
    }
}
