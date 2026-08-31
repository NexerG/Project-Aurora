using ArctisAurora.Core.Diagnostics;
using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.UISystem.Controls;
using ArctisAurora.Core.UISystem.Controls.Containers;
using ArctisAurora.Core.UISystem.Controls.Interactable;
using ArctisAurora.EngineWork;
using ArctisAurora.EngineWork.Rendering;

namespace ArctisAurora.Core.UISystem
{
    // What a SessionPane may hold: more panes, or the tabs of a leaf.
    public interface ISessionChild { }

    [A_XSDType("SessionTab", "Settings")]
    public class SessionTab : ISessionChild
    {
        [A_XSDElementProperty("Path", "Settings", "Note the tab had open.")]
        public string path { get; set; } = "";
    }

    // A split when it holds panes, a leaf when it holds tabs.
    [A_XSDType("SessionPane", "Settings", AllowedChildren = typeof(ISessionChild))]
    public class SessionPane : ISessionChild
    {
        [A_XSDElementProperty("Orientation", "Settings", "Axis a split pane divides along.")]
        public StackPanelControl.Orientation orientation { get; set; } = StackPanelControl.Orientation.Horizontal;

        [A_XSDElementProperty("Size", "Settings", "Fixed main-axis size in pixels; 0 for the pane that takes the remainder.")]
        public int size { get; set; }

        [A_XSDElementProperty("Active", "Settings", "Index of the tab that was on top.")]
        public int active { get; set; }

        [A_XSDElementProperty("SessionTab", "Settings")]
        public List<SessionTab> tabs = new List<SessionTab>();

        [A_XSDElementProperty("SessionPane", "Settings")]
        public List<SessionPane> panes = new List<SessionPane>();
    }

    [A_XSDType("SessionWindow", "Settings", AllowedChildren = typeof(SessionPane))]
    public class SessionWindow
    {
        [A_XSDElementProperty("Document", "Settings", "UI document the window was built from.")]
        public string document { get; set; } = "";

        [A_XSDElementProperty("Primary", "Settings", "True for the window the application boots into.")]
        public bool primary { get; set; }

        // restore rect, which is where a maximized window goes back to
        [A_XSDElementProperty("X", "Settings")] public int x { get; set; }
        [A_XSDElementProperty("Y", "Settings")] public int y { get; set; }
        [A_XSDElementProperty("Width", "Settings")] public int width { get; set; }
        [A_XSDElementProperty("Height", "Settings")] public int height { get; set; }
        [A_XSDElementProperty("Maximized", "Settings")] public bool maximized { get; set; }

        [A_XSDElementProperty("SessionPane", "Settings")]
        public List<SessionPane> panes = new List<SessionPane>();
    }

    // One partition of the record. What a key means is the host's business — the engine only groups by it.
    [A_XSDType("SessionScope", "Settings", AllowedChildren = typeof(SessionWindow))]
    public class SessionScope
    {
        [A_XSDElementProperty("Key", "Settings", "What this set of windows belongs to.")]
        public string key { get; set; } = "";

        [A_XSDElementProperty("SessionWindow", "Settings")]
        public List<SessionWindow> windows = new List<SessionWindow>();
    }

    // The windows that were open, where they sat, and what was in them. Written by the shutdown
    // sequence and read back at boot into every Workspace.
    [A_XSDType("Session", "Settings", AllowedChildren = typeof(SessionScope))]
    public class SessionLayout : ISettingsGroup
    {
        private static readonly LogChannel Log = LogChannel.For("Session");

        [A_XSDElementProperty("SessionScope", "Settings")]
        public List<SessionScope> scopes = new List<SessionScope>();

        // Which partition capture writes and restore reads. The host sets it to whatever it wants
        // remembered separately; one application-wide session is the empty default.
        public static string scope = "";

        // How the host turns a saved path into a tab. The engine can build the editor but not what
        // the host binds to it, so restoring tabs needs this set.
        public static Func<string, TabItemControl> tabFactory;

        // secondary windows are renamed on restore, so a later tear-off cannot collide with one
        private const string restoredWindow = "session-";

        #region ---- restore ----
        // Rebuilds what the last session left in this scope. The primary's tree is already parsed and
        // assigned; every other window is opened here. Falls back to the authored arrangement.
        public static void Restore()
        {
            WorkspaceControl primary = WorkspaceControl.In(Engine.primary.ui.uiRoot);
            SessionScope recorded = SettingsRegistry.Get<SessionLayout>().Recorded(scope);

            if (recorded == null || recorded.windows.Count == 0)
            {
                primary?.LoadDefault();
                return;
            }

            int opened = 0;
            foreach (SessionWindow record in recorded.windows)
            {
                if (record.primary)
                {
                    Fill(primary, record);
                    Place(Engine.primary, record);
                    continue;
                }

                OpenRecorded(record, ++opened);
            }

            // GLFW focuses each window as it is created, so the one the user asked for goes last.
            Engine.primary.Focus();
            Log.Info($"restored {recorded.windows.Count} window(s) for '{scope}'");
        }

        private static void OpenRecorded(SessionWindow record, int index)
        {
            if (string.IsNullOrEmpty(record.document)) return;

            (int x, int y) = PositionFor(record);
            RenderWindow window = Engine.OpenWindow(restoredWindow + index,
                (uint)record.width, (uint)record.height, x, y);

            window.uiDocument = record.document;
            window.ui.uiRoot = (WindowControl)VulkanControl.ParseXML(record.document);

            Fill(WorkspaceControl.In(window.ui.uiRoot), record);
            if (record.maximized) window.os.Maximize();
        }

        // A window whose saved rect no longer lands on a screen goes back to the middle of the
        // primary one, at the size it had.
        private static (int x, int y) PositionFor(SessionWindow record)
        {
            if (AGlfwWindow.RectOnAnyMonitor(record.x, record.y, record.width, record.height))
                return (record.x, record.y);

            Log.Info($"saved rect {record.x},{record.y} {record.width}x{record.height} is off every screen — centring.");
            return AGlfwWindow.CenterOnPrimary(record.width, record.height);
        }

        private static void Place(RenderWindow window, SessionWindow record)
        {
            (int x, int y) = PositionFor(record);

            window.os.Resize((uint)record.width, (uint)record.height);
            window.os.SetPosition(x, y);
            if (record.maximized) window.os.Maximize();
        }

        // A window with no panes recorded still gets its authored arrangement, so a record written
        // before a workspace existed does not open an empty window.
        private static void Fill(WorkspaceControl workspace, SessionWindow record)
        {
            if (workspace == null) return;

            if (record.panes.Count == 0) { workspace.LoadDefault(); return; }

            TabViewControl seed = workspace.LoadPane();
            if (seed == null) { workspace.LoadDefault(); return; }

            Build(record.panes[0], seed);
        }

        // The arrangement replayed as the splits that would have produced it, so all of
        // SplitViewControl's grip and sizing rules are the ones that apply.
        private static void Build(SessionPane record, TabViewControl into)
        {
            if (record.panes.Count == 0) { FillTabs(record, into); return; }

            bool vertical = record.orientation == StackPanelControl.Orientation.Vertical;
            SplitViewControl.SplitEdge edge = vertical
                ? SplitViewControl.SplitEdge.Bottom
                : SplitViewControl.SplitEdge.Right;

            List<TabViewControl> panes = new List<TabViewControl>();
            TabViewControl current = into;

            for (int i = 0; i < record.panes.Count - 1; i++)
            {
                TabViewControl fresh = SplitViewControl.Split(current, edge);
                if (fresh == null) break;

                panes.Add(current);
                current = fresh;
            }
            panes.Add(current);

            for (int i = 0; i < panes.Count; i++)
            {
                SessionPane child = record.panes[i];

                // Split sizes the pane it makes; a recorded size is what that pane was dragged to.
                if (child.size > 0)
                {
                    if (vertical) panes[i].preferredHeight = child.size;
                    else panes[i].preferredWidth = child.size;
                }

                Build(child, panes[i]);
            }
        }

        // A note that is no longer on disk is dropped rather than reopened, so the active index is
        // resolved against the tabs that were actually built.
        private static void FillTabs(SessionPane record, TabViewControl view)
        {
            if (tabFactory == null)
            {
                Log.Warn($"no tab factory — {record.tabs.Count} tab(s) not reopened.");
                return;
            }

            TabItemControl active = null;

            for (int i = 0; i < record.tabs.Count; i++)
            {
                string path = record.tabs[i].path;
                if (!File.Exists(path))
                {
                    Log.Info($"note is gone, not reopening: {path}");
                    continue;
                }

                TabItemControl tab = tabFactory(path);
                if (tab == null) continue;

                view.AddChild(tab);
                if (i == record.active) active = tab;
            }

            if (active != null) view.SetActive(active);
        }

        private SessionScope Recorded(string key)
        {
            foreach (SessionScope existing in scopes)
                if (string.Equals(existing.key, key, StringComparison.OrdinalIgnoreCase))
                    return existing;

            return null;
        }
        #endregion

        #region ---- capture ----
        [A_XSDActionDependency("Session.Capture", "Shutdown", "Records every open window's placement and panes")]
        public static bool Capture()
        {
            SettingsRegistry.Get<SessionLayout>().Record();
            return true;
        }

        private void Record()
        {
            List<SessionWindow> captured = new List<SessionWindow>();

            foreach (RenderWindow window in Engine.windows.Values)
            {
                if (string.IsNullOrEmpty(window.uiDocument) || window.closeRequested) continue;

                WorkspaceControl workspace = WorkspaceControl.In(window.ui.uiRoot);
                if (workspace == null) continue;

                (int x, int y, int width, int height, bool maximized) = window.os.GetPlacement();

                SessionWindow record = new SessionWindow
                {
                    document = window.uiDocument,
                    primary = window == Engine.primary,
                    x = x,
                    y = y,
                    width = width,
                    height = height,
                    maximized = maximized
                };

                if (workspace.children.Count > 0 && workspace.children[0] is VulkanControl content
                    && PaneOf(content) is SessionPane root)
                    record.panes.Add(root);

                captured.Add(record);
            }

            ScopeFor(scope).windows = captured;
            Log.Info($"captured {captured.Count} window(s) for '{scope}'");
        }

        private static SessionPane PaneOf(VulkanControl node)
        {
            if (node is SplitViewControl split) return SplitPane(split);
            if (node is TabViewControl view) return LeafPane(view);
            return null;
        }

        private static SessionPane SplitPane(SplitViewControl split)
        {
            SessionPane record = new SessionPane { orientation = split.orientation };
            bool vertical = split.orientation == StackPanelControl.Orientation.Vertical;

            foreach (Entity child in split.children)
            {
                if (child is not VulkanControl pane || pane is SplitterControl) continue;
                if (PaneOf(pane) is not SessionPane captured) continue;

                captured.size = vertical ? pane.preferredHeight : pane.preferredWidth;
                record.panes.Add(captured);
            }

            return record;
        }

        // A tab whose editor never loaded a file cannot be reopened, so it is left out and the active
        // index counts only what was written.
        private static SessionPane LeafPane(TabViewControl view)
        {
            SessionPane record = new SessionPane();

            foreach (TabItemControl item in view.Items)
            {
                string path = TabViewControl.EditorOf(item)?.session?.path;
                if (path == null) continue;

                if (ReferenceEquals(item, view.activeItem)) record.active = record.tabs.Count;
                record.tabs.Add(new SessionTab { path = path });
            }

            return record;
        }

        private SessionScope ScopeFor(string key)
        {
            if (Recorded(key) is SessionScope existing) return existing;

            SessionScope fresh = new SessionScope { key = key };
            scopes.Add(fresh);
            return fresh;
        }
        #endregion
    }
}
