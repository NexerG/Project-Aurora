using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Testing;
using ArctisAurora.Core.UI;
using System.Numerics;

namespace Thorium.Tests
{
    internal static class RibbonTests
    {
        [A_XSDActionDependency("Ribbon.Shows", "Test")]
        private static IEnumerator<int> Shows(TestContext t)
        {
            string note = TempFile(".xml");
            string sheet = TempFile(SheetDocument.extension);
            (RibbonControl ribbon, WorkspaceControl workspace, InspectorControl inspector) = ShowShell(t);
            WorkspacePageControl general = workspace.AddPage("General", WorkspaceKind.General);
            TabViewControl generalView = workspace.LoadPane(general);
            generalView.AddChild(SessionLayout.tabFactory(note));
            WorkspacePageControl docs = workspace.AddPage("Docs", WorkspaceKind.Docs);
            TabViewControl docsView = workspace.LoadPane(docs);
            docsView.AddChild(SessionLayout.tabFactory(note));
            WorkspacePageControl sheets = workspace.AddPage("Sheets", WorkspaceKind.Sheets);
            TabViewControl sheetsView = workspace.LoadPane(sheets);
            sheetsView.AddChild(SessionLayout.tabFactory(sheet));
            yield return 2;

            t.Check(ribbon.hidden && inspector.hidden, "a General workspace has neither ribbon nor inspector");
            t.Check(ToolsOnRow(generalView), "and its note keeps its tab-row tools");

            workspace.Show(docs);
            yield return 3;
            t.Check(!ribbon.hidden && !inspector.hidden, "a Docs workspace shows both");
            t.Check(Captions(ribbon, "Format", "Insert", "Page"), "with Format, Insert and Page");
            t.Check(MathF.Abs(inspector.arrangedRect.width - 240f) < 1f, $"the inspector is 240 px wide: {inspector.arrangedRect.width}");
            t.Check(!ToolsOnRow(docsView), "the note's tab-row tools give way to the ribbon");

            workspace.Show(sheets);
            yield return 3;
            t.Check(!ribbon.hidden && Captions(ribbon, "Format", "Insert", "Data"), "a Sheets workspace's ribbon has Format, Insert and Data");
            t.Check(!ToolsOnRow(sheetsView), "and the sheet's tab-row tools give way too");

            t.Show(new StackPanelControl());
            yield return 2;
            File.Delete(note);
            File.Delete(sheet);
        }

        [A_XSDActionDependency("Ribbon.Docs", "Test")]
        private static IEnumerator<int> Docs(TestContext t)
        {
            string note = TempFile(".xml");
            (RibbonControl ribbon, WorkspaceControl workspace, _) = ShowShell(t);
            WorkspacePageControl docs = workspace.AddPage("Docs", WorkspaceKind.Docs);
            workspace.LoadPane(docs).AddChild(SessionLayout.tabFactory(note));
            yield return 3;

            DocumentEditorControl editor = TabViewControl.EditorOf(TabViewControl.TabViews(docs).First().Items.First());
            editor.FocusCaret();
            editor.SelectAll();
            yield return 2;
            yield return t.Golden("Format", ribbon);

            yield return t.Click(ButtonOf(Icon(ribbon, "bold")!));
            yield return t.Click(ButtonOf(Icon(ribbon, "strikethrough")!));
            yield return 2;
            t.Check(editor.StyleSource?.bold == true && editor.StyleSource?.strikethrough == true,
                "Bold and S on the ribbon style the focused note's selection");

            yield return t.Click(Button(ribbon, "Insert")!);
            yield return 2;
            t.Check(docs.ribbonCategory == "Insert" && Label(ribbon, "Quote") != null && Icon(ribbon, "bold") == null,
                "a category tab swaps the strip");

            yield return t.Click(Button(ribbon, "Quote")!);
            yield return 2;
            t.Check(editor.CaretBlockStyling == TextStyleType.Quote, $"Quote restyles the caret's paragraph: {editor.CaretBlockStyling}");

            t.Show(new StackPanelControl());
            yield return 2;
            File.Delete(note);
        }

        [A_XSDActionDependency("Ribbon.Sheets", "Test")]
        private static IEnumerator<int> Sheets(TestContext t)
        {
            string note = TempFile(".xml");
            string sheet = TempFile(SheetDocument.extension);
            (RibbonControl ribbon, WorkspaceControl workspace, _) = ShowShell(t);
            WorkspacePageControl docs = workspace.AddPage("Docs", WorkspaceKind.Docs);
            workspace.LoadPane(docs).AddChild(SessionLayout.tabFactory(note));
            WorkspacePageControl sheets = workspace.AddPage("Sheets", WorkspaceKind.Sheets);
            workspace.LoadPane(sheets).AddChild(SessionLayout.tabFactory(sheet));
            docs.ribbonCategory = "Insert";
            workspace.Show(sheets);
            yield return 3;

            SheetEditorControl editor = (SheetEditorControl)TabViewControl.FileEditorOf(TabViewControl.TabViews(sheets).First().Items.First());
            editor.Select(0, 0, false);
            yield return 2;
            yield return t.Golden("Format", ribbon);

            yield return t.Click(Button(ribbon, "$")!);
            Control strip = (Control)ButtonOf(Label(ribbon, "$")!).parent!;
            List<TabToolsControl.TabToolButton> swatches = strip.children.OfType<TabToolsControl.TabToolButton>().ToList();
            yield return t.Click(swatches[^6]);
            yield return 2;
            SheetFormat format = editor.page.Format(0, 0);
            t.Check(format.number == "€#,##0.00", $"$ formats the active cell as currency: {format.number}");
            t.Check(format.fill == MarkdownFormat.DefaultHighlightHex, $"the second swatch fills it yellow: {format.fill}");

            yield return t.Click(Button(ribbon, "Data")!);
            yield return 2;
            workspace.Show(docs);
            yield return 3;
            t.Check(Label(ribbon, "Quote") != null, "each workspace keeps its own category");
            workspace.Show(sheets);
            yield return 3;
            t.Check(sheets.ribbonCategory == "Data" && Label(ribbon, "Export page as CSV") != null, "and comes back to it");

            t.Show(new StackPanelControl());
            yield return 2;
            File.Delete(note);
            File.Delete(sheet);
        }

        [A_XSDActionDependency("Inspector.Docs", "Test")]
        private static IEnumerator<int> InspectorDocs(TestContext t)
        {
            string note = TempFile(".xml");
            (RibbonControl ribbon, WorkspaceControl workspace, InspectorControl inspector) = ShowShell(t);
            WorkspacePageControl docs = workspace.AddPage("Docs", WorkspaceKind.Docs);
            workspace.LoadPane(docs).AddChild(SessionLayout.tabFactory(note));
            yield return 3;

            DocumentEditorControl editor = TabViewControl.EditorOf(TabViewControl.TabViews(docs).First().Items.First());
            editor.FocusCaret();
            yield return 2;
            yield return t.Golden("Panel", inspector);

            TextBoxControl top = Find<TextBoxControl>(inspector).First();
            top.text = "30";
            top.onCommit!("30");
            yield return t.Click(Button(inspector, "Landscape")!);
            yield return 2;
            t.Check(editor.Page?.marginTop == 30f && editor.Page?.landscape == true,
                $"the margin field and Landscape change the note's page: {editor.Page?.marginTop}, {editor.Page?.landscape}");

            editor.Undo();
            yield return 2;
            t.Check(editor.Page?.landscape == false && top.text == "30", "undo turns the page back and the field follows what is left");

            SplitterControl grip = ((Control)inspector.parent!).children.OfType<SplitterControl>().First();
            Vector2 middle = new Vector2(grip.arrangedRect.x + grip.arrangedRect.width / 2f, grip.arrangedRect.y + grip.arrangedRect.height / 2f);
            yield return t.Drag(grip, middle - new Vector2(60f, 0f));
            yield return 2;
            t.Check(MathF.Abs(inspector.arrangedRect.width - 300f) < 2f, $"dragging the splitter left widens the inspector: {inspector.arrangedRect.width}");

            yield return t.Click(ButtonOf(Icon(ribbon, "panel-right")!));
            yield return 2;
            t.Check(inspector.hidden && grip.hidden && !docs.inspectorShown, "the ribbon's Inspector toggle hides it and its splitter");
            yield return t.Click(ButtonOf(Icon(ribbon, "panel-right")!));
            yield return 2;
            t.Check(!inspector.hidden && !grip.hidden, "and shows them again");

            t.Show(new StackPanelControl());
            yield return 2;
            File.Delete(note);
        }

        [A_XSDActionDependency("Inspector.Sheets", "Test")]
        private static IEnumerator<int> InspectorSheets(TestContext t)
        {
            string sheet = TempFile(SheetDocument.extension);
            (RibbonControl ribbon, WorkspaceControl workspace, InspectorControl inspector) = ShowShell(t);
            WorkspacePageControl sheets = workspace.AddPage("Sheets", WorkspaceKind.Sheets);
            workspace.LoadPane(sheets).AddChild(SessionLayout.tabFactory(sheet));
            yield return 3;

            SheetEditorControl editor = (SheetEditorControl)TabViewControl.FileEditorOf(TabViewControl.TabViews(sheets).First().Items.First());
            editor.Select(2, 1, false);
            yield return 2;
            yield return t.Golden("Panel", inspector);
            t.Check(Label(inspector, "B3") != null, "the Cell header names the active cell");
            t.Check(Find<SheetLayersControl>(inspector).Any(), "the page's layers are listed");

            yield return t.Click(Button(ribbon, "Insert")!);
            yield return 2;
            yield return t.Click(Button(ribbon, "Layer")!);
            yield return 2;
            t.Check(Label(inspector, "Layer 1") != null, "a layer added from the ribbon shows in the inspector");

            t.Show(new StackPanelControl());
            yield return 2;
            File.Delete(sheet);
        }

        [A_XSDActionDependency("Ribbon.Session", "Test")]
        private static IEnumerator<int> Session(TestContext t)
        {
            string note = TempFile(".xml");
            string before = SessionLayout.scope;
            (_, WorkspaceControl workspace, _) = ShowShell(t);
            SessionLayout.scope = "rb-a";
            WorkspacePageControl docs = workspace.AddPage("Docs", WorkspaceKind.Docs);
            workspace.LoadPane(docs).AddChild(SessionLayout.tabFactory(note));
            docs.ribbonCategory = "Page";
            docs.inspectorShown = false;
            yield return 2;

            SessionLayout.ChangeScope("rb-b");
            yield return 2;
            bool restored = SessionLayout.ChangeScope("rb-a");
            yield return 2;
            WorkspacePageControl back = workspace.Pages.First();
            t.Check(restored && back.ribbonCategory == "Page" && !back.inspectorShown,
                $"the category and the inspector toggle come back: {back.ribbonCategory}, {back.inspectorShown}");

            SessionLayout.scope = before;
            t.Show(new StackPanelControl());
            yield return 2;
            File.Delete(note);
        }

        private static (RibbonControl, WorkspaceControl, InspectorControl) ShowShell(TestContext t)
        {
            RibbonControl ribbon = new RibbonControl();
            WorkspaceControl workspace = new WorkspaceControl
            {
                paneDocument = "tab-pane",
                defaultDocument = "tab-pane",
                widthStar = 1f,
                verticalAlignment = VerticalAlignment.Stretch
            };
            InspectorControl inspector = new InspectorControl();

            StackPanelControl body = new StackPanelControl { orientation = StackPanelControl.Orientation.Horizontal, heightStar = 1f, horizontalAlignment = HorizontalAlignment.Stretch };
            body.AddChild(workspace);
            body.AddChild(new SplitterControl { preferredWidth = 3f });
            body.AddChild(inspector);
            StackPanelControl shell = new StackPanelControl { horizontalAlignment = HorizontalAlignment.Stretch, verticalAlignment = VerticalAlignment.Stretch };
            shell.AddChild(ribbon);
            shell.AddChild(body);
            t.Show(shell);
            return (ribbon, workspace, inspector);
        }

        private static bool ToolsOnRow(TabViewControl view)
        {
            Control? tools = view.activeItem != null ? TabViewControl.FileEditorOf(view.activeItem)?.tools : null;
            return tools != null && ReferenceEquals(tools.parent, view);
        }

        private static bool Captions(Control root, params string[] captions) => captions.All(caption => Label(root, caption) != null);

        // The shown descendants of a type, depth first.
        private static IEnumerable<T> Find<T>(Control root) where T : Control
        {
            foreach (Entity child in root.children)
            {
                if (child is not Control control || control.hidden) continue;
                if (control is T found) yield return found;
                foreach (T deeper in Find<T>(control)) yield return deeper;
            }
        }

        private static LabelControl? Label(Control root, string text) => Find<LabelControl>(root).FirstOrDefault(label => label.text == text);

        private static IconControl? Icon(Control root, string icon) => Find<IconControl>(root).FirstOrDefault(ink => ink.iconName == icon);

        private static Control? Button(Control root, string caption) => Label(root, caption) is LabelControl label ? ButtonOf(label) : null;

        private static Control ButtonOf(Control part)
        {
            Control control = part;
            while (control is not ButtonControl && control.parent is Control up) control = up;
            return control;
        }

        // A temp note holding one paragraph, or a blank sheet.
        private static string TempFile(string extension)
        {
            string path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), $"aurora-ribbon-{Guid.NewGuid():N}{extension}"));
            if (extension == SheetDocument.extension) SheetDocument.Blank("Ribbon").Save(path);
            else File.WriteAllText(path, "<Document><Block><Run Text=\"ribbon text\"/></Block></Document>\n");
            return path;
        }
    }
}
