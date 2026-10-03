using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.Filing;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Testing;
using ArctisAurora.Core.UI;
using ArctisAurora.EngineWork;
using System.Numerics;
using System.Xml.Linq;

namespace Thorium.Tests
{
    internal static class SheetTests
    {
        [A_XSDActionDependency("Sheet.XmlRoundTrip", "Test")]
        private static IEnumerator<int> XmlRoundTrip(TestContext t)
        {
            XElement source = XElement.Parse(
                "<Sheet Name=\"Budget\">" +
                "<Page Name=\"One\"><Column At=\"B\" Width=\"140\" /><Row At=\"3\" Height=\"30\" />" +
                "<Layer Name=\"Values\"><Cell At=\"A1\">Item</Cell><Cell At=\"B1\">12.5</Cell><Cell At=\"AA100\">far</Cell></Layer>" +
                "<Layer Name=\"Notes\" Visible=\"false\"><Cell At=\"A1\">hidden</Cell></Layer>" +
                "<Chart Kind=\"Bar\" /></Page>" +
                "<Page Name=\"Two\"><Layer Name=\"Layer 1\" /></Page>" +
                "</Sheet>");

            SheetDocument sheet = SheetXml.Parse(source);
            SheetPage page = sheet.pages[0];

            t.Check(XNode.DeepEquals(source, SheetXml.ToXml(sheet)), $"the sheet writes back as it was read: {SheetXml.ToXml(sheet)}");
            t.Check(sheet.pages.Count == 2 && page.layers.Count == 2, "both pages and both layers are read");
            t.Check(page.layers[0].cells.Count == 3, "only the written cells are stored");
            t.Check(page.extra.Count == 1, "an element the reader does not know is kept");
            t.Check(page.Shown(0, 0) == "Item", "a hidden layer does not cover the one under it");
            t.Check(page.ColumnWidth(1) == 140f && page.ColumnLeft(2) == 240f, "a sized column moves the ones after it");
            t.Check(page.ColumnAt(239f) == 1 && page.ColumnAt(240f) == 2, "a position finds the column it is in");
            t.Check(page.RowTop(3) == 78f && page.RowAt(77f) == 2, "a sized row moves the ones below it");
            t.Check(SheetDocument.ColumnName(26) == "AA" && SheetDocument.ColumnName(701) == "ZZ", "columns past Z take two letters");
            t.Check(SheetDocument.TryParseAddress("AA100", out int row, out int column) && row == 99 && column == 26, "an address reads back to its cell");
            yield break;
        }

        [A_XSDActionDependency("Sheet.TypeEnterUndo", "Test")]
        private static IEnumerator<int> TypeEnterUndo(TestContext t)
        {
            string path = TempSheet(SheetDocument.Blank("Typing"));
            SheetEditorControl editor = ShowSheet(t, path);
            yield return 2;

            SheetControl grid = Grid(editor);
            LayoutRect b2 = grid.CellRect(1, 1);
            Vector2 at = new Vector2(b2.x + b2.width * 0.5f, b2.y + b2.height * 0.5f);
            yield return t.Drag(grid, at, at, 1);
            t.Check(editor.activeRow == 1 && editor.activeColumn == 1, "a click selects the cell under it");

            yield return t.Type("42");
            t.Check(editor.editing, "typing opens the cell for editing");

            yield return t.Key(Keys.Enter);
            yield return 2;
            SheetPage page = editor.document.pages[0];
            t.Check(!editor.editing && page.Shown(1, 1) == "42", $"Enter commits what was typed: {page.Shown(1, 1)}");
            t.Check(editor.activeRow == 2 && editor.activeColumn == 1, "Enter steps down");

            yield return t.Key(Keys.Z, Keys.LeftControl);
            t.Check(page.Shown(1, 1) == null, "undo empties the cell again");

            yield return t.Key(Keys.Right, Keys.LeftShift);
            t.Check(editor.anchorColumn == 1 && editor.activeColumn == 2, "Shift+Right extends the selection");

            yield return t.Key(Keys.F2);
            yield return t.Type("x");
            yield return t.Key(Keys.Tab);
            yield return 2;
            t.Check(page.Shown(2, 2) == "x" && editor.activeColumn == 3, "F2, typing and Tab commit and step right");

            t.Show(new StackPanelControl());
            File.Delete(path);
        }

        [A_XSDActionDependency("Sheet.PasteBlock", "Test")]
        private static IEnumerator<int> PasteBlock(TestContext t)
        {
            string path = TempSheet(SheetDocument.Blank("Paste"));
            SheetEditorControl editor = ShowSheet(t, path);
            yield return 2;

            editor.Select(1, 1, false);
            editor.Paste("a\tb\r\nc\td\r\ne\tf\r\n");
            yield return 2;

            SheetPage page = editor.document.pages[0];
            t.Check(page.Shown(1, 1) == "a" && page.Shown(3, 2) == "f" && page.layers[0].cells.Count == 6, "three rows of two land from the active cell");
            t.Check(editor.anchorRow == 1 && editor.anchorColumn == 1 && editor.activeRow == 3 && editor.activeColumn == 2, "the pasted block is selected");

            editor.Copy();
            t.Check(ClipboardText.Get() == "a\tb\r\nc\td\r\ne\tf", $"copy writes the selection as tab-separated rows: {ClipboardText.Get()}");

            editor.Undo();
            t.Check(page.layers[0].cells.Count == 0, "one undo takes back all six");

            t.Show(new StackPanelControl());
            File.Delete(path);
        }

        [A_XSDActionDependency("Sheet.ScrollKeepsControls", "Test")]
        private static IEnumerator<int> ScrollKeepsControls(TestContext t)
        {
            SheetDocument sheet = SheetDocument.Blank("Long");
            for (int r = 0; r < 10000; r++)
                sheet.pages[0].layers[0].Set(r, 0, "r" + r);
            string path = TempSheet(sheet);

            SheetEditorControl editor = ShowSheet(t, path);
            yield return 3;
            int before = Count(editor);

            editor.SetScrollOffset(new Vector2(0f, editor.document.pages[0].RowTop(5000)));
            yield return 3;
            int after = Count(editor);

            t.Check(before < 1000, $"only the cells in view have controls: {before}");
            t.Check(after == before, $"scrolling reuses them: {before} then {after}");
            t.Check(Labels(editor).Contains("r5000"), "the rows scrolled to are drawn");

            t.Show(new StackPanelControl());
            File.Delete(path);
        }

        [A_XSDActionDependency("Sheet.TabLifecycle", "Test")]
        private static IEnumerator<int> TabLifecycle(TestContext t)
        {
            string path = TempSheet(SheetDocument.Blank("Lifecycle"));
            string before = SessionLayout.scope;
            WorkspaceControl workspace = new WorkspaceControl
            {
                paneDocument = "tab-pane",
                defaultDocument = "tab-pane",
                horizontalAlignment = HorizontalAlignment.Stretch,
                verticalAlignment = VerticalAlignment.Stretch
            };
            t.Show(workspace);
            SessionLayout.scope = "sheet-a";
            workspace.LoadPane()!.AddChild(SessionLayout.tabFactory(path));
            yield return 2;

            SheetEditorControl editor = (SheetEditorControl)TabViewControl.FileEditorOf(TabViewControl.FindOpenDocument(path, out _))!;
            editor.Select(3, 2, false);
            editor.Paste("kept");
            editor.Select(4, 1, false);
            editor.Save();
            yield return 2;

            SessionLayout.ChangeScope("sheet-b");
            yield return 2;
            SessionLayout.ChangeScope("sheet-a");
            yield return 2;

            TabItemControl item = TabViewControl.FindOpenDocument(path, out TabViewControl view);
            editor = TabViewControl.FileEditorOf(item) as SheetEditorControl;
            t.Check(editor != null && editor.document.pages[0].Shown(3, 2) == "kept", "the sheet tab comes back with its scope");
            t.Check(editor != null && editor.activeRow == 4 && editor.activeColumn == 1, "and with its active cell");

            editor!.Paste("closed");
            view.CloseTab(item);
            yield return 2;
            t.Check(SheetDocument.Load(path).pages[0].Shown(4, 1) == "closed", "closing the tab writes the edit");

            view.AddChild(SessionLayout.tabFactory(path));
            yield return 2;
            item = TabViewControl.FindOpenDocument(path, out _);
            editor = (SheetEditorControl)TabViewControl.FileEditorOf(item)!;
            item.onRename!("Renamed sheet");
            string renamed = Path.Combine(Path.GetDirectoryName(path)!, "Renamed sheet" + SheetDocument.extension);
            t.Check(File.Exists(renamed) && !File.Exists(path), $"a rename keeps the sheet extension: {editor.path}");
            t.Check(editor.path == renamed && editor.document.name == "Renamed sheet", "the open tab follows the rename");

            SessionLayout.scope = before;
            t.Show(new StackPanelControl());
            File.Delete(renamed);
            File.Delete(path);
        }

        [A_XSDActionDependency("Sheet.GridDraws", "Test")]
        private static IEnumerator<int> GridDraws(TestContext t)
        {
            SheetDocument sheet = SheetDocument.Blank("Grid");
            SheetLayer layer = sheet.pages[0].layers[0];
            for (int r = 0; r < 20; r++)
                for (int c = 0; c < 10; c++)
                    layer.Set(r, c, c == 0 ? "Item " + (r + 1) : ((r + 1) * (c + 1)).ToString());
            layer.Set(0, 3, "a long caption cut at the cell");
            string path = TempSheet(sheet);

            SheetEditorControl editor = new SheetEditorControl
            {
                preferredWidth = 640f,
                preferredHeight = 360f,
                horizontalAlignment = HorizontalAlignment.Left,
                verticalAlignment = VerticalAlignment.Top
            };
            editor.LoadPath(path);
            t.Show(editor);
            editor.Select(1, 1, false);
            editor.Select(4, 3, true);
            yield return 3;

            yield return t.Golden("Grid", editor);

            t.Show(new StackPanelControl());
            File.Delete(path);
        }

        #region ---- fixtures ----
        private static string TempSheet(SheetDocument sheet)
        {
            string path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), $"aurora-sheet-{Guid.NewGuid():N}{SheetDocument.extension}"));
            sheet.Save(path);
            return path;
        }

        private static SheetEditorControl ShowSheet(TestContext t, string path)
        {
            SheetEditorControl editor = new SheetEditorControl
            {
                horizontalAlignment = HorizontalAlignment.Stretch,
                verticalAlignment = VerticalAlignment.Stretch
            };
            editor.LoadPath(path);
            t.Show(editor);
            return editor;
        }

        private static SheetControl Grid(SheetEditorControl editor) => editor.children.OfType<SheetControl>().First();

        private static int Count(Entity entity)
        {
            int count = 1;
            foreach (Entity child in entity.children)
                count += Count(child);
            return count;
        }

        // Text of every label laid out on screen.
        private static HashSet<string> Labels(Entity entity)
        {
            HashSet<string> found = new HashSet<string>();
            Collect(entity, found);
            return found;

            static void Collect(Entity entity, HashSet<string> into)
            {
                if (entity is LabelControl label && label.arrangedRect.width > 0f) into.Add(label.text);
                foreach (Entity child in entity.children)
                    Collect(child, into);
            }
        }
        #endregion
    }
}
