using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Testing;
using ArctisAurora.Core.UI;

namespace Thorium.Tests
{
    internal static class WorkspaceTests
    {
        [A_XSDActionDependency("Workspace.Switch", "Test")]
        private static IEnumerator<int> Switch(TestContext t)
        {
            string note = TempNote("shared");
            WorkspaceControl workspace = ShowWorkspace(t);
            WorkspacePageControl a = workspace.AddPage("General", WorkspaceKind.General);
            workspace.LoadPane(a).AddChild(SessionLayout.tabFactory(note));
            WorkspacePageControl b = workspace.AddPage("Docs", WorkspaceKind.Docs);
            workspace.LoadPane(b).AddChild(SessionLayout.tabFactory(note));
            yield return 2;

            DocumentEditorControl inA = Editor(a), inB = Editor(b);
            t.Check(ReferenceEquals(workspace.shown, a) && !a.hidden && b.hidden, "the first workspace is shown and the second hidden");
            t.Check(ReferenceEquals(inA.session, inB.session) && inA.session.views == 2, "a note open in two workspaces is one session with two views");

            workspace.Show(b);
            yield return 2;
            t.Check(ReferenceEquals(workspace.shown, b) && a.hidden && !b.hidden && inB.arrangedRect.width > 0f,
                "showing the second hides the first and lays the second out");
            t.Check(ReferenceEquals(WorkspacePageControl.Of(TabViewControl.focused), b), "the shown workspace's pane takes the focus");

            inB.session.document.InsertText((NoteBlock)inB.session.document.blocks[0], new DocumentAddress(0, 0), "x");
            workspace.Show(a);
            yield return 2;
            t.Check(((NoteBlock)inA.session.document.blocks[0]).run.text == "xshared" && inA.arrangedRect.width > 0f,
                "an edit made in the hidden workspace is there when it is shown again");

            t.Show(new StackPanelControl());
            yield return 2;
            File.Delete(note);
        }

        [A_XSDActionDependency("Workspace.Menu", "Test")]
        private static IEnumerator<int> Menu(TestContext t)
        {
            string note = TempNote("menu");
            WorkspaceControl workspace = ShowWorkspace(t);
            WorkspacePageControl first = workspace.AddPage("General", WorkspaceKind.General);
            workspace.LoadPane(first).AddChild(SessionLayout.tabFactory(note));
            yield return 2;

            WorkspaceActions.Duplicate();
            yield return 2;
            WorkspacePageControl copy = workspace.shown!;
            t.Check(workspace.Pages.Count() == 2 && !ReferenceEquals(copy, first) && copy.title == "General" && Paths(copy) == note,
                $"Duplicate adds a workspace with the same files and shows it: {Paths(copy)}");

            WorkspaceActions.NewSheets();
            yield return 2;
            t.Check(workspace.Pages.Count() == 3 && workspace.shown!.kind == WorkspaceKind.Sheets
                && TabViewControl.TabViews(workspace.shown).Count() == 1 && Paths(workspace.shown) == "",
                "New Sheets workspace adds an empty pane of that kind and shows it");

            WorkspaceActions.Close();
            yield return 2;
            WorkspaceActions.Close();
            yield return 2;
            t.Check(workspace.Pages.Count() == 1 && ReferenceEquals(workspace.shown, first) && Editor(first).session.views == 1,
                "Close drops a workspace and its views");

            WorkspaceActions.Close();
            yield return 2;
            t.Check(workspace.Pages.Count() == 1 && Paths(first) == note, "the last workspace stays");

            t.Show(new StackPanelControl());
            yield return 2;
            File.Delete(note);
        }

        [A_XSDActionDependency("Workspace.SessionRoundTrip", "Test")]
        private static IEnumerator<int> SessionRoundTrip(TestContext t)
        {
            string note = TempNote("kept");
            string sheet = Path.GetFullPath(Path.Combine(Path.GetTempPath(), $"aurora-ws-{Guid.NewGuid():N}{SheetDocument.extension}"));
            SheetDocument.Blank("Budget").Save(sheet);
            string before = SessionLayout.scope;

            WorkspaceControl workspace = ShowWorkspace(t);
            SessionLayout.scope = "ws-a";
            WorkspacePageControl a = workspace.AddPage("General", WorkspaceKind.General);
            workspace.LoadPane(a).AddChild(SessionLayout.tabFactory(note));
            WorkspacePageControl b = workspace.AddPage("Sheets", WorkspaceKind.Sheets);
            workspace.LoadPane(b).AddChild(SessionLayout.tabFactory(sheet));
            workspace.Rename(b, "Budget");
            workspace.Show(b);
            yield return 2;

            bool restored = SessionLayout.ChangeScope("ws-b");
            yield return 2;
            t.Check(!restored && workspace.Pages.Count() == 1 && Paths(workspace.shown!) == "", "a scope with nothing recorded gets one empty workspace");

            restored = SessionLayout.ChangeScope("ws-a");
            yield return 2;
            List<WorkspacePageControl> pages = workspace.Pages.ToList();
            t.Check(restored && pages.Count == 2 && pages[0].title == "General" && pages[0].kind == WorkspaceKind.General
                && pages[1].title == "Budget" && pages[1].kind == WorkspaceKind.Sheets && ReferenceEquals(workspace.shown, pages[1]),
                "both workspaces come back with their names, kinds and which one was shown");
            t.Check(pages.Count == 2 && Paths(pages[0]) == note && Paths(pages[1]) == sheet,
                $"and each keeps its own files: {string.Join(" | ", pages.Select(Paths))}");

            SessionLayout.scope = before;
            t.Show(new StackPanelControl());
            yield return 2;
            File.Delete(note);
            File.Delete(sheet);
        }

        [A_XSDActionDependency("Workspace.LegacySession", "Test")]
        private static IEnumerator<int> LegacySession(TestContext t)
        {
            string note = TempNote("legacy");
            string before = SessionLayout.scope;
            SessionLayout layout = SettingsRegistry.Get<SessionLayout>();

            SessionPane leaf = new SessionPane();
            leaf.tabs.Add(new SessionTab { path = note });
            SessionWindow window = new SessionWindow { primary = true, width = 800, height = 600 };
            window.panes.Add(leaf);
            SessionScope legacy = new SessionScope { key = "ws-legacy" };
            legacy.windows.Add(window);
            layout.scopes.Add(legacy);

            WorkspaceControl workspace = ShowWorkspace(t);
            SessionLayout.scope = "ws-other";
            workspace.LoadEmpty();
            yield return 2;

            bool restored = SessionLayout.ChangeScope("ws-legacy");
            yield return 2;
            t.Check(restored && workspace.Pages.Count() == 1 && workspace.shown!.kind == WorkspaceKind.General && Paths(workspace.shown) == note,
                "a record from before workspaces opens as one General workspace");

            SessionLayout.ChangeScope("ws-other");
            yield return 2;
            SessionWindow saved = layout.scopes.First(s => s.key == "ws-legacy").windows.First(w => w.primary);
            t.Check(saved.panes.Count == 0 && saved.workspaces.Count == 1 && saved.workspaces[0].panes.Count == 1
                && saved.workspaces[0].panes[0].tabs.Count == 1 && saved.workspaces[0].panes[0].tabs[0].path == note,
                "it is written back as a workspace, with its tab");

            SessionLayout.scope = before;
            t.Show(new StackPanelControl());
            yield return 2;
            File.Delete(note);
        }

        private static WorkspaceControl ShowWorkspace(TestContext t)
        {
            WorkspaceControl workspace = new WorkspaceControl
            {
                paneDocument = "tab-pane",
                defaultDocument = "tab-pane",
                horizontalAlignment = HorizontalAlignment.Stretch,
                verticalAlignment = VerticalAlignment.Stretch
            };
            t.Show(workspace);
            return workspace;
        }

        private static string TempNote(string text)
        {
            string path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), $"aurora-ws-{Guid.NewGuid():N}.xml"));
            File.WriteAllText(path, $"<Document><Block><Run Text=\"{text}\"/></Block></Document>\n");
            return path;
        }

        private static DocumentEditorControl Editor(WorkspacePageControl page) =>
            TabViewControl.EditorOf(TabViewControl.TabViews(page).First().Items.First());

        private static string Paths(WorkspacePageControl page) => string.Join(",", TabViewControl.TabViews(page)
            .SelectMany(view => view.Items).Select(item => TabViewControl.FileEditorOf(item)?.path));
    }
}
