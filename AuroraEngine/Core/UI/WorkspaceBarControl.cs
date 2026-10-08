using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.Registry;
using ArctisAurora.EngineWork;

namespace ArctisAurora.Core.UI
{
    // The workspaces of this window's Workspace as title-bar tabs. A press shows one, a double click
    // renames it in place, a right click offers the workspace menu.
    [A_XSDType("WorkspaceBar", "UI")]
    public class WorkspaceBarControl : StackPanelControl
    {
        [A_XSDElementProperty("TabContextMenu", "UI", "Menu a workspace tab offers on right click.")]
        public string tabContextMenu = "workspace";

        // tab geometry
        private const float tabHeight = 22f;
        private const float tabInset = 10f;
        private const int captionSize = 12;

        private bool rebuildPending;

        public WorkspaceBarControl()
        {
            orientation = Orientation.Horizontal;
            Spacing = 2f;
            alpha = 0f;
            WorkspaceControl.changed += OnWorkspaceChanged;
        }

        public override void OnDestroy()
        {
            WorkspaceControl.changed -= OnWorkspaceChanged;
            base.OnDestroy();
        }

        // One workspace tab.
        public sealed class WorkspaceButton : ButtonControl
        {
            public WorkspacePageControl page = null!;
            public EditableLabelControl caption = null!;
        }

        // Posted, because a rename commits from inside the caption this would destroy.
        private void OnWorkspaceChanged(WorkspaceControl workspace)
        {
            if (rebuildPending || !ReferenceEquals(workspace, Workspace())) return;
            rebuildPending = true;
            Engine.Post(() =>
            {
                rebuildPending = false;
                if (!destroyed) Rebuild(workspace);
            });
        }

        private WorkspaceControl? Workspace()
        {
            Control root = this;
            while (root.parent is Control up) root = up;
            return WorkspaceControl.In(root);
        }

        private void Rebuild(WorkspaceControl workspace)
        {
            foreach (Entity child in children.ToList()) child.Destroy();

            foreach (WorkspacePageControl page in workspace.Pages)
                AddChild(BuildTab(workspace, page));
        }

        private WorkspaceButton BuildTab(WorkspaceControl workspace, WorkspacePageControl page)
        {
            bool shown = ReferenceEquals(page, workspace.shown);
            WorkspaceButton tab = new WorkspaceButton
            {
                page = page,
                preferredHeight = tabHeight,
                padding = new Thickness(0f, tabInset, 0f, tabInset),
                margin = new Thickness(26f - tabHeight, 0f, 0f, 0f),
                cornerRole = CornerRole.Tab,
                contextMenu = tabContextMenu,
                stopsContextMenu = true
            };
            tab.PaintOr(null, shown ? PaletteRole.Ground : PaletteRole.Clear);
            if (shown) tab.accentRole = AccentRole.Tab;

            tab.caption = new EditableLabelControl
            {
                text = page.title,
                fontSize = captionSize,
                horizontalPosition = 0.5f,
                verticalPosition = 0.5f
            };
            tab.caption.PaintText(null, shown ? PaletteRole.Ink : PaletteRole.MutedInk);
            tab.AddChild(tab.caption);

            tab.RegisterOnRelease(e => { workspace.Show(page); return true; });
            tab.RegisterOnTap(e =>
            {
                if (e.tapCount != 2) return false;
                BeginRename(tab);
                return true;
            });
            return tab;
        }

        private static void BeginRename(WorkspaceButton tab) =>
            tab.caption.BeginEdit(name => (tab.page.parent as WorkspaceControl)?.Rename(tab.page, name));

        // Starts renaming a workspace in whichever bar shows it.
        public static void BeginRename(WorkspacePageControl page)
        {
            if (page.parent is not WorkspaceControl workspace) return;

            Control root = workspace;
            while (root.parent is Control up) root = up;
            foreach (WorkspaceButton tab in Buttons(root))
                if (ReferenceEquals(tab.page, page)) { BeginRename(tab); return; }
        }

        private static IEnumerable<WorkspaceButton> Buttons(Control control)
        {
            if (control is WorkspaceButton tab) yield return tab;
            foreach (Entity child in control.children)
                if (child is Control childControl)
                    foreach (WorkspaceButton found in Buttons(childControl))
                        yield return found;
        }
    }
}
