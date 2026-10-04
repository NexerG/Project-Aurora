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

            editor!.Paste("settled");
            NoteActions.SaveEdited();
            t.Check(SheetDocument.Load(path).pages[0].Shown(4, 1) == "settled", "the shutdown save writes a dirty sheet");

            editor.Select(5, 1, false);
            editor.TypeOver(new Queue<char>("typed"));
            yield return 2;
            editor.Save();
            t.Check(!editor.editing && SheetDocument.Load(path).pages[0].Shown(5, 1) == "typed", "saving commits an open cell edit");

            editor.Select(4, 1, false);
            editor.Paste("closed");
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

        [A_XSDActionDependency("Sheet.FormulaEval", "Test")]
        private static IEnumerator<int> FormulaEval(TestContext t)
        {
            SheetDocument sheet = SheetDocument.Blank("Eval");
            SheetPage one = sheet.pages[0];
            SheetPage two = SheetPage.Blank("My page");
            sheet.pages.Add(two);
            SheetLayer cells = one.layers[0];
            cells.Set(0, 0, "2");
            cells.Set(1, 0, "3");
            cells.Set(2, 0, "text");
            cells.Set(5, 5, "1.50");
            two.layers[0].Set(0, 0, "10");
            two.layers[0].Set(1, 0, "5");
            SheetBook.Register(sheet);

            string? Eval(string formula)
            {
                cells.Set(9, 9, formula);
                SheetBook.calc.Changed(one, new[] { (9, 9) });
                return SheetBook.calc.Value(one, 9, 9).Display();
            }

            void Expect(string formula, string shown) =>
                t.Check(Eval(formula) == shown, $"{formula} shows {shown}: {Eval(formula)}");

            Expect("=1+2*3", "7");
            Expect("=(1+2)*3", "9");
            Expect("=-2^2", "4");
            Expect("=2^3^2", "64");
            Expect("=2^-1", "0.5");
            Expect("=0.1+0.2", "0.3");
            Expect("=A1+A2*2", "8");
            Expect("=Z99", "0");
            Expect("=SUM(A1:A3)", "5");
            Expect("=sum(A1, 4, 'My page'!A1:A2)", "21");
            Expect("= 'My page'!A1 * 2", "20");
            Expect("=A3+1", SheetFormula.value);
            Expect("=A1:A2", SheetFormula.value);
            Expect("=1/0", SheetFormula.div0);
            Expect("=1/0+FOO()", SheetFormula.div0);
            Expect("=SUM(A1, 1/0)", SheetFormula.div0);
            Expect("=FOO(1)", SheetFormula.name);
            Expect("=Nope!A1", SheetFormula.reference);
            Expect("=XFE1", SheetFormula.reference);
            Expect("=1+", SheetFormula.parse);
            Expect("=10^400", SheetFormula.num);

            SheetValue typed = SheetBook.calc.Value(one, 5, 5);
            t.Check(typed.kind == SheetValueKind.Number && typed.Display() == "1.50", "a typed number keeps the text it was typed as");
            SheetBook.Unregister(sheet);
            yield break;
        }

        [A_XSDActionDependency("Sheet.FormulaRecalc", "Test")]
        private static IEnumerator<int> FormulaRecalc(TestContext t)
        {
            SheetDocument sheet = SheetDocument.Blank("Recalc");
            SheetPage one = sheet.pages[0];
            SheetPage two = SheetPage.Blank("My page");
            sheet.pages.Add(two);
            SheetLayer cells = one.layers[0];
            SheetLayer other = two.layers[0];
            SheetCalc calc = SheetBook.calc;

            string? Shown(SheetPage page, string address)
            {
                SheetDocument.TryParseAddress(address, out int row, out int column);
                return calc.Value(page, row, column).Display();
            }

            void Write(SheetPage page, params (string address, string? raw)[] writes)
            {
                List<(int, int)> changed = new List<(int, int)>();
                foreach ((string address, string? raw) in writes)
                {
                    SheetDocument.TryParseAddress(address, out int row, out int column);
                    page.layers[^1].Set(row, column, raw);
                    changed.Add((row, column));
                }
                calc.Changed(page, changed);
            }

            cells.Set(0, 0, "1");
            cells.Set(0, 1, "=A1+1");
            cells.Set(0, 2, "=B1*10");
            other.Set(0, 0, "10");
            cells.Set(0, 3, "='My page'!A1*2");
            SheetBook.Register(sheet);
            t.Check(Shown(one, "C1") == "20" && Shown(one, "D1") == "20", "a load evaluates every formula");

            Write(one, ("A1", "5"));
            t.Check(Shown(one, "C1") == "60", $"a chain follows its first cell: {Shown(one, "C1")}");

            Write(two, ("A1", "7"));
            t.Check(Shown(one, "D1") == "14", $"a reference into another page follows it: {Shown(one, "D1")}");

            Write(one, ("E1", "=F1"), ("F1", "=E1"), ("G1", "=E1+1"));
            t.Check(Shown(one, "E1") == SheetFormula.cycle && Shown(one, "F1") == SheetFormula.cycle, "two cells reading each other are a cycle");
            t.Check(Shown(one, "G1") == SheetFormula.cycle, "a cell reading a cycle shows the cycle");

            Write(one, ("F1", "3"));
            t.Check(Shown(one, "E1") == "3" && Shown(one, "G1") == "4", "breaking the cycle recovers both");

            Write(one, ("H1", "='My page'!B1"));
            Write(two, ("B1", "='Sheet 1'!H1"));
            t.Check(Shown(one, "H1") == SheetFormula.cycle && Shown(two, "B1") == SheetFormula.cycle, "a cycle across pages is found");

            Write(one, ("I1", "=I1+1"));
            t.Check(Shown(one, "I1") == SheetFormula.cycle, "a cell reading itself is a cycle");

            List<(int, int)> chain = new List<(int, int)>();
            cells.Set(0, 20, "1");
            for (int r = 1; r < 10000; r++)
            {
                cells.Set(r, 20, "=U" + r + "+1");
                chain.Add((r, 20));
            }
            calc.Changed(one, chain);
            t.Check(Shown(one, "U10000") == "10000", $"a 10,000-cell chain evaluates: {Shown(one, "U10000")}");

            Write(one, ("U1", "2"));
            t.Check(Shown(one, "U10000") == "10001", "and follows its first cell");
            SheetBook.Unregister(sheet);
            yield break;
        }

        [A_XSDActionDependency("Sheet.FormulaView", "Test")]
        private static IEnumerator<int> FormulaView(TestContext t)
        {
            string path = TempSheet(SheetDocument.Blank("Formulas"));
            SheetEditorControl editor = ShowSheet(t, path);
            yield return 2;

            editor.Select(0, 0, false);
            editor.Paste("3");
            SheetControl grid = Grid(editor);
            LayoutRect b1 = grid.CellRect(0, 1);
            Vector2 at = new Vector2(b1.x + b1.width * 0.5f, b1.y + b1.height * 0.5f);
            yield return t.Drag(grid, at, at, 1);
            yield return t.Type("=A1*2");
            yield return t.Key(Keys.Enter);
            yield return 2;

            SheetPage page = editor.document.pages[0];
            LabelControl? InB1() => Find<LabelControl>(editor, label => label.arrangedRect.width > 0f
                && label.arrangedRect.x >= b1.x && label.arrangedRect.Right <= b1.Right
                && label.arrangedRect.y >= b1.y && label.arrangedRect.Bottom <= b1.Bottom);

            t.Check(page.Shown(0, 1) == "=A1*2", $"the cell keeps the formula: {page.Shown(0, 1)}");
            LabelControl? value = InB1();
            t.Check(value?.text == "6", $"the grid shows its value: {value?.text}");
            t.Check(value != null && value.arrangedRect.x > b1.x + b1.width * 0.5f, "a computed number aligns right");

            editor.Select(0, 1, false);
            yield return t.Key(Keys.F2);
            TextBoxControl? field = Find<TextBoxControl>(editor, _ => true);
            t.Check(editor.editing && field?.text == "=A1*2", $"F2 opens the formula: {field?.text}");
            yield return t.Key(Keys.Escape);

            editor.Select(0, 0, false);
            editor.Paste("4");
            yield return 2;
            t.Check(InB1()?.text == "8", $"an edit recalculates what reads it: {InB1()?.text}");

            editor.Undo();
            yield return 2;
            t.Check(InB1()?.text == "6", $"undo recalculates too: {InB1()?.text}");

            editor.Select(0, 0, false);
            editor.Select(0, 1, true);
            editor.Copy();
            t.Check(ClipboardText.Get() == "3\t6", $"copy writes values: {ClipboardText.Get()}");

            editor.Save();
            t.Check(SheetDocument.Load(path).pages[0].Shown(0, 1) == "=A1*2", "the file keeps the formula");

            t.Show(new StackPanelControl());
            File.Delete(path);
        }

        [A_XSDActionDependency("Sheet.CrossFile", "Test")]
        private static IEnumerator<int> CrossFile(TestContext t)
        {
            string folder = TempFolder();
            Func<string, string?>? before = SheetBook.findSheet;
            SheetBook.findSheet = FolderResolver(folder);

            SheetDocument budget = SheetDocument.Blank("Budget");
            budget.pages[0].name = "Expenses";
            budget.pages[0].layers[0].Set(0, 0, "10");
            budget.pages[0].layers[0].Set(0, 1, "=A1*2");
            string budgetPath = Path.Combine(folder, "Budget" + SheetDocument.extension);
            budget.Save(budgetPath);

            SheetDocument summary = SheetDocument.Blank("Summary");
            SheetLayer cells = summary.pages[0].layers[0];
            cells.Set(0, 0, "=[Budget]Expenses!B1+1");
            cells.Set(1, 0, "='[Budget]Expenses'!A1");
            cells.Set(2, 0, "=[Nope]Expenses!A1");
            cells.Set(3, 0, "=[Budget]Missing!A1");
            string summaryPath = Path.Combine(folder, "Summary" + SheetDocument.extension);
            summary.Save(summaryPath);

            StackPanelControl both = new StackPanelControl { orientation = StackPanelControl.Orientation.Horizontal };
            SheetEditorControl editor = new SheetEditorControl { preferredWidth = 400f, preferredHeight = 300f };
            editor.LoadPath(summaryPath);
            both.AddChild(editor);
            t.Show(both);
            yield return 2;
            SheetPage page = editor.document.pages[0];
            string? Shown(int row) => SheetBook.calc.Value(page, row, 0).Display();

            t.Check(Shown(0) == "21", $"a formula reads a sheet that is not open: {Shown(0)}");
            t.Check(Shown(1) == "10", $"the quoted form reads it too: {Shown(1)}");
            t.Check(Shown(2) == SheetFormula.reference && Shown(3) == SheetFormula.reference, "a missing file or page is #REF!");

            SheetEditorControl other = new SheetEditorControl { preferredWidth = 400f, preferredHeight = 300f };
            other.LoadPath(budgetPath);
            both.AddChild(other);
            yield return 2;
            t.Check(ReferenceEquals(other.document, SheetBook.Get(budgetPath)), "a second editor shares the loaded copy");
            other.Select(0, 0, false);
            other.Paste("5");
            yield return 2;
            t.Check(Shown(0) == "11", $"an edit in the other sheet reaches this one: {Shown(0)}");
            LayoutRect a1 = Grid(editor).CellRect(0, 0);
            t.Check(Find<LabelControl>(editor, label => label.text == "11" && label.arrangedRect.x >= a1.x && label.arrangedRect.Right <= a1.Right
                && label.arrangedRect.y >= a1.y && label.arrangedRect.Bottom <= a1.Bottom) != null, "and its grid redraws");
            t.Check(!editor.unsaved && other.unsaved, "only the edited sheet is unsaved");

            other.Select(0, 2, false);
            other.Paste("='[Summary]Sheet 1'!A5");
            editor.Select(4, 0, false);
            editor.Paste("=[Budget]Expenses!C1");
            t.Check(Shown(4) == SheetFormula.cycle && SheetBook.calc.Value(other.document.pages[0], 0, 2).Display() == SheetFormula.cycle,
                "two files reading each other are a cycle");

            t.Show(new StackPanelControl());
            SheetBook.findSheet = before;
            SheetBook.Deleted(budgetPath);
            SheetBook.Deleted(summaryPath);
            Directory.Delete(folder, true);
        }

        [A_XSDActionDependency("Sheet.RenameRewrites", "Test")]
        private static IEnumerator<int> RenameRewrites(TestContext t)
        {
            string folder = TempFolder();
            Func<string, string?>? before = SheetBook.findSheet;
            SheetBook.findSheet = FolderResolver(folder);
            string PathOf(string name) => Path.Combine(folder, name + SheetDocument.extension);

            SheetDocument budget = SheetDocument.Blank("Budget");
            budget.pages[0].name = "Data";
            budget.pages[0].layers[0].Set(0, 0, "10");
            budget.Save(PathOf("Budget"));

            SheetDocument summary = SheetDocument.Blank("Summary");
            summary.pages[0].layers[0].Set(0, 0, "=[Budget]Data!A1*2");
            summary.pages[0].layers[0].Set(1, 0, "='[Budget]Data'!A1");
            summary.Save(PathOf("Summary"));

            SheetDocument unopened = SheetDocument.Blank("Unopened");
            unopened.pages[0].layers[0].Set(0, 0, "=[Budget]Data!A1+1");
            unopened.Save(PathOf("Unopened"));

            SheetDocument.Blank("Plain").Save(PathOf("Plain"));
            DateTime plainTime = File.GetLastWriteTimeUtc(PathOf("Plain"));

            SheetEditorControl editor = ShowSheet(t, PathOf("Summary"));
            yield return 2;
            SheetLayer cells = editor.document.pages[0].layers[0];
            t.Check(SheetBook.calc.Value(editor.document.pages[0], 0, 0).Display() == "20", "the summary reads the budget");

            File.Move(PathOf("Budget"), PathOf("Money"));
            SheetBook.Renamed(PathOf("Budget"), PathOf("Money"), Directory.EnumerateFiles(folder));
            yield return 2;

            t.Check(cells.Get(0, 0) == "=[Money]Data!A1*2" && cells.Get(1, 0) == "=[Money]Data!A1",
                $"a loaded sheet's references follow the rename: {cells.Get(0, 0)} {cells.Get(1, 0)}");
            t.Check(SheetBook.calc.Value(editor.document.pages[0], 0, 0).Display() == "20", "and keep their value");
            t.Check(SheetDocument.Load(PathOf("Unopened")).pages[0].Shown(0, 0) == "=[Money]Data!A1+1", "a sheet on disk is rewritten");
            t.Check(File.GetLastWriteTimeUtc(PathOf("Plain")) == plainTime, "a sheet without a reference is not touched");

            File.Move(PathOf("Money"), PathOf("My money"));
            SheetBook.Renamed(PathOf("Money"), PathOf("My money"), Directory.EnumerateFiles(folder));
            t.Check(cells.Get(0, 0) == "='[My money]Data'!A1*2", $"a name with a space is quoted: {cells.Get(0, 0)}");
            t.Check(SheetBook.calc.Value(editor.document.pages[0], 0, 0).Display() == "20", "and still reads");

            t.Show(new StackPanelControl());
            SheetBook.findSheet = before;
            foreach (string file in Directory.EnumerateFiles(folder))
                SheetBook.Deleted(file);
            Directory.Delete(folder, true);
        }

        [A_XSDActionDependency("Sheet.PasteLinks", "Test")]
        private static IEnumerator<int> PasteLinks(TestContext t)
        {
            string folder = TempFolder();
            Func<string, string?>? before = SheetBook.findSheet;
            SheetBook.findSheet = FolderResolver(folder);
            string PathOf(string name) => Path.Combine(folder, name + SheetDocument.extension);

            SheetDocument budget = SheetDocument.Blank("Budget");
            budget.pages[0].name = "Data";
            budget.pages[0].layers[0].Set(0, 0, "10");
            budget.pages[0].layers[0].Set(0, 1, "20");
            budget.Save(PathOf("Budget"));
            SheetDocument.Blank("Summary").Save(PathOf("Summary"));

            StackPanelControl both = new StackPanelControl { orientation = StackPanelControl.Orientation.Horizontal };
            SheetEditorControl summary = new SheetEditorControl { preferredWidth = 400f, preferredHeight = 300f };
            SheetEditorControl source = new SheetEditorControl { preferredWidth = 400f, preferredHeight = 300f };
            summary.LoadPath(PathOf("Summary"));
            source.LoadPath(PathOf("Budget"));
            both.AddChild(summary);
            both.AddChild(source);
            t.Show(both);
            yield return 2;
            SheetLayer cells = summary.document.pages[0].layers[0];
            string? Shown(int row, int column) => SheetBook.calc.Value(summary.document.pages[0], row, column).Display();

            summary.Select(0, 0, false);
            summary.Paste("1\t2");
            summary.Select(0, 0, false);
            summary.Select(0, 1, true);
            summary.Copy();
            summary.Select(2, 0, false);
            t.Check(summary.PasteLink(), "Paste link takes the copy");
            t.Check(cells.Get(2, 0) == "=A1" && cells.Get(2, 1) == "=B1", $"from the same page it reads plain addresses: {cells.Get(2, 0)} {cells.Get(2, 1)}");
            t.Check(Shown(2, 0) == "1" && Shown(2, 1) == "2", "and shows their values");
            t.Check(summary.anchorRow == 2 && summary.activeRow == 2 && summary.activeColumn == 1, "the pasted block is selected");

            source.Select(0, 0, false);
            source.Select(0, 1, true);
            source.Copy();
            UIEngine.SetActiveControl(summary);
            summary.Select(4, 0, false);
            yield return t.Key(Keys.V, Keys.LeftControl, Keys.LeftShift);
            t.Check(cells.Get(4, 0) == "=[Budget]Data!A1" && cells.Get(4, 1) == "=[Budget]Data!B1",
                $"Ctrl+Shift+V from another file names the file and page: {cells.Get(4, 0)} {cells.Get(4, 1)}");
            t.Check(Shown(4, 0) == "10" && Shown(4, 1) == "20", "and shows its values");

            summary.Undo();
            t.Check(cells.Get(4, 0) == null && cells.Get(4, 1) == null, "one undo takes the link back");

            ClipboardText.Set("x\ty");
            summary.Select(6, 0, false);
            summary.PasteLink();
            t.Check(cells.Get(6, 0) == "x" && cells.Get(6, 1) == "y", "a clipboard from elsewhere pastes plainly");

            t.Check(SheetFormula.Prefix(null, "Sheet 1") == "'Sheet 1'" && SheetFormula.Prefix("My money", "Data") == "'[My money]Data'"
                && SheetFormula.Prefix("Finance/Budget", "Data") == "[Finance/Budget]Data", "a page or file name with a space is quoted");

            t.Show(new StackPanelControl());
            SheetBook.findSheet = before;
            SheetBook.Deleted(PathOf("Budget"));
            SheetBook.Deleted(PathOf("Summary"));
            Directory.Delete(folder, true);
        }

        #region ---- fixtures ----
        private static string TempFolder()
        {
            string folder = Path.Combine(Path.GetTempPath(), $"aurora-sheets-{Guid.NewGuid():N}");
            Directory.CreateDirectory(folder);
            return folder;
        }

        private static Func<string, string?> FolderResolver(string folder) => file =>
        {
            string path = Path.Combine(folder, file + SheetDocument.extension);
            return File.Exists(path) ? path : null;
        };

        private static T? Find<T>(Entity entity, Func<T, bool> match) where T : class
        {
            if (entity is T found && match(found)) return found;
            foreach (Entity child in entity.children)
                if (Find(child, match) is T hit) return hit;
            return null;
        }

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
