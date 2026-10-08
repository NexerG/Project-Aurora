using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Testing;
using ArctisAurora.Core.UI;
using ArctisAurora.EngineWork;
using ArctisAurora.EngineWork.Rendering;

namespace Thorium.Tests
{
    internal static class StickyTests
    {
        private const string stickyDocument = "sticky-window";

        [A_XSDActionDependency("Sticky.PinPinRestore", "Test")]
        private static IEnumerator<int> PinPinRestore(TestContext t)
        {
            string path = TempNote();
            string before = SessionLayout.scope;
            SessionLayout.scope = "sticky-a";
            WorkspaceControl workspace = new WorkspaceControl
            {
                paneDocument = "tab-pane",
                defaultDocument = "tab-pane",
                horizontalAlignment = HorizontalAlignment.Stretch,
                verticalAlignment = VerticalAlignment.Stretch
            };
            t.Show(workspace);
            WorkspacePageControl page = workspace.AddPage("General", WorkspaceKind.General);
            TabViewControl view = workspace.LoadPane(page);
            TabItemControl tab = SessionLayout.tabFactory(path);
            view.AddChild(tab);
            yield return 3;

            yield return t.Click(Find<TabStripButtonControl>(view, _ => true)!, Keys.MouseRight);
            yield return 20;
            ContextMenuControl.Row? submenu = MenuRow("Pin to desktop");
            t.Check(submenu != null, "the tab menu offers Pin to desktop");
            if (submenu == null) yield break;
            yield return t.MoveTo(submenu);
            yield return 20;
            ContextMenuControl.Row? fresh = MenuRow("New sticky");
            t.Check(fresh != null, "its submenu offers New sticky");
            if (fresh == null) yield break;
            yield return t.Click(fresh);
            yield return 3;

            RenderWindow? sticky = Sticky();
            t.Check(sticky != null, "a sticky window opens");
            if (sticky == null) yield break;
            TabViewControl? stuck = TabViewControl.TabViews(sticky.ui.uiRoot).FirstOrDefault();
            DocumentEditorControl source = TabViewControl.EditorOf(tab);
            DocumentEditorControl? copy = stuck?.activeItem != null ? TabViewControl.EditorOf(stuck.activeItem) : null;
            t.Check(view.Items.Contains(tab), "the tab stays where it was");
            t.Check(copy != null && ReferenceEquals(copy.session, source.session) && source.session.views == 2,
                "the sticky holds a second view of the same note");

            ButtonControl pin = Find<ButtonControl>(sticky.ui.uiRoot, b => b.children.OfType<LabelControl>().Any(l => l.name == "pin"))!;
            yield return t.Click(pin);
            LabelControl pinLabel = pin.children.OfType<LabelControl>().First();
            t.Check(sticky.pinned && pinLabel.role == PaletteRole.Accent, "the pin button keeps the sticky on top and lights");

            SessionLayout.ChangeScope("sticky-b");
            yield return 3;
            t.Check(Sticky() == null, "another scope closes the sticky");
            SessionLayout.ChangeScope("sticky-a");
            yield return 3;
            RenderWindow? back = Sticky();
            TabViewControl? backView = back != null ? TabViewControl.TabViews(back.ui.uiRoot).FirstOrDefault() : null;
            t.Check(back != null && back.pinned, "coming back reopens the sticky, still pinned");
            t.Check(backView?.activeItem != null && TabViewControl.FileEditorOf(backView.activeItem)?.path == path,
                "with the note in it");

            SessionLayout.scope = before;
            foreach (RenderWindow window in Engine.windows.Values.ToList())
                if (window.uiDocument == stickyDocument) Engine.CloseWindow(window);
            t.Show(new StackPanelControl());
            yield return 3;
            File.Delete(path);
        }

        private static RenderWindow? Sticky() =>
            Engine.windows.Values.FirstOrDefault(w => !w.closeRequested && w.uiDocument == stickyDocument);

        private static ContextMenuControl.Row? MenuRow(string text)
        {
            foreach (RenderWindow window in Engine.windows.Values)
                if (window.ui?.uiRoot != null && Find<ContextMenuControl.Row>(window.ui.uiRoot, r => Caption(r.entry) == text) is ContextMenuControl.Row row)
                    return row;
            return null;
        }

        private static string? Caption(ContextMenuEntry entry) => entry switch
        {
            ContextMenuButton button => button.text,
            ContextMenuSubmenu submenu => submenu.text,
            _ => null
        };

        private static T? Find<T>(Entity entity, Func<T, bool> match) where T : class
        {
            if (entity is T found && match(found)) return found;
            foreach (Entity child in entity.children)
                if (Find(child, match) is T hit) return hit;
            return null;
        }

        private static string TempNote()
        {
            string path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), $"aurora-sticky-{Guid.NewGuid():N}.xml"));
            File.WriteAllText(path, "<Document><Block><Run Text=\"sticky text\"/></Block></Document>\n");
            return path;
        }
    }
}
