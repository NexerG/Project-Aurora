using ArctisAurora.Core.Diagnostics;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Registry.Assets;
using ArctisAurora.EngineWork;
using ArctisAurora.EngineWork.Registry;
using ArctisAurora.EngineWork.Rendering;
using Silk.NET.GLFW;
using Silk.NET.Maths;
using Silk.NET.Vulkan;

namespace ArctisAurora.Core.UI
{
    // Resolves, opens, hosts and closes the context menu on the new stack.
    public static class NextContextMenus
    {
        private static readonly LogChannel Log = LogChannel.For("ContextMenus");

        // the control the open menu was opened on
        public static Control? target { get; private set; }

        // menus by name — parsed documents and ones registered from code
        private static readonly Dictionary<string, ContextMenu> _menus = new Dictionary<string, ContextMenu>();

        // open panels, index = depth
        private static readonly List<NextContextMenuControl> _open = new List<NextContextMenuControl>();
        private static RenderWindow? _origin;
        private static int _windowSerial;

        private static readonly ContextMenuLine groupLine = new ContextMenuLine();

        #region ---- menus ----
        // Makes a menu built in code nameable by ContextMenu="…".
        public static void Register(string name, ContextMenu menu) => _menus[name] = menu;

        // A registered menu, or the document of that name, parsed on first use.
        public static ContextMenu? Get(string name)
        {
            if (_menus.TryGetValue(name, out ContextMenu? menu)) return menu;

            Dictionary<string, ContextMenuAsset>? documents =
                AssetRegistries.GetRegistryByValueType<string, ContextMenuAsset>(typeof(ContextMenuAsset));
            if (documents == null || !documents.TryGetValue(name, out ContextMenuAsset? asset))
            {
                Log.Warn($"no context menu named '{name}'");
                return null;
            }

            menu = Control.ParseMenu(asset.path);
            _menus[name] = menu;
            return menu;
        }

        // Entries from the control up, one group per menu, until a control stops the walk.
        public static List<ContextMenuEntry> Collect(Control control)
        {
            List<ContextMenuEntry> entries = new List<ContextMenuEntry>();
            for (Control? c = control; c != null; c = c.parent as Control)
            {
                ContextMenu? menu = c.contextMenu == null ? null : Get(c.contextMenu);
                if (menu != null && menu.entries.Count > 0)
                {
                    if (entries.Count > 0) entries.Add(groupLine);
                    entries.AddRange(menu.entries);
                }
                if (c.stopsContextMenu) break;
            }
            return entries;
        }
        #endregion

        #region ---- open and close ----
        internal static void OpenOn(Control? control, Vector2D<float> point)
        {
            if (control == null) return;
            Open(Collect(control), control, point);
        }

        // Opens a menu of entries on a control, at a point in its window's design space.
        public static void Open(List<ContextMenuEntry> entries, Control on, Vector2D<float> point)
        {
            Close();
            if (entries.Count == 0) return;

            RenderWindow? window = UIEngine.WindowOf(on);
            if (window?.uiNext.uiRoot == null) return;

            _origin = window;
            target = on;
            Host(new NextContextMenuControl(entries, 0) { position = point });
        }

        public static void Close() => CloseFrom(0);

        // Closes the panel at depth and every one opened from it.
        private static void CloseFrom(int depth)
        {
            for (int i = _open.Count - 1; i >= depth; i--)
            {
                NextContextMenuControl panel = _open[i];
                _open.RemoveAt(i);

                if (panel.window != null)
                {
                    panel.window.uiNext.uiRoot = null;
                    Engine.CloseWindow(panel.window);
                    continue;
                }
                (panel.parent as Control)?.RemoveChild(panel);
                panel.Destroy();
            }

            if (depth > 0) return;
            target = null;
            _origin = null;
        }

        // Over the origin window's tree when the panel fits inside it, in a window of its own when not.
        private static void Host(NextContextMenuControl panel)
        {
            WindowRoot root = _origin!.uiNext.uiRoot;
            Vector2D<float> viewport = root.arrangedRect.size;
            Vector2D<float> size = panel.Measure(viewport);
            Vector2D<float> at = panel.position;

            if (at.X >= 0 && at.Y >= 0 && at.X + size.X <= viewport.X && at.Y + size.Y <= viewport.Y)
                root.AddChild(panel);
            else
                HostInWindow(panel, size, viewport);

            _open.Add(panel);
        }

        private static unsafe void HostInWindow(NextContextMenuControl panel, Vector2D<float> size, Vector2D<float> viewport)
        {
            RenderWindow window = Engine.OpenMenuWindow($"context-menu-{_windowSerial++}",
                                                        (uint)MathF.Ceiling(size.X), (uint)MathF.Ceiling(size.Y));
            panel.window = window;

            WindowRoot root = new WindowRoot();
            root.AddChild(panel);
            window.uiNext.uiRoot = root;

            AGlfwWindow._glfw.GetWindowPos(_origin!.os.handle, out int x, out int y);
            Extent2D pixels = _origin.os.windowSize;
            window.os.SetPosition(x + (int)(panel.position.X * pixels.Width / viewport.X),
                                  y + (int)(panel.position.Y * pixels.Height / viewport.Y));
            window.os.Show();
        }
        #endregion

        #region ---- input ----
        // A hovered row closes the panels deeper than its own; a submenu row opens one beside it.
        internal static void Entered(NextContextMenuControl panel, NextContextMenuControl.Row row)
        {
            int deeper = panel.depth + 1;
            if (_open.Count > deeper && ReferenceEquals(_open[deeper].opener, row)) return;

            CloseFrom(deeper);
            if (row.entry is not ContextMenuSubmenu submenu || submenu.entries.Count == 0) return;

            LayoutRect r = row.arrangedRect;
            LayoutRect p = panel.arrangedRect;
            Vector2D<float> at = panel.position + new Vector2D<float>(r.x + r.width - p.x, r.y - p.y - panel.padding.top);

            Host(new NextContextMenuControl(submenu.entries, deeper) { position = at, opener = row });
        }

        // A clicked button row runs its action, then the whole menu closes.
        internal static void Clicked(NextContextMenuControl.Row row)
        {
            if (row.entry is not ContextMenuButton button) return;

            button.action?.Invoke();
            Close();
        }

        // A press anywhere but on an open panel closes the menu.
        internal static void DismissUnlessInside(Control? pressed)
        {
            if (_open.Count == 0) return;

            for (Control? c = pressed; c != null; c = c.parent as Control)
                if (c is NextContextMenuControl) return;
            Close();
        }

        // Closes the menu once no window of the application has focus.
        internal static unsafe void Tick()
        {
            if (_open.Count == 0) return;

            foreach (RenderWindow window in Engine.windows.Values)
                if (!window.closeRequested && AGlfwWindow._glfw.GetWindowAttrib(window.os.handle, WindowAttributeGetter.Focused))
                    return;
            Close();
        }
        #endregion
    }
}
