using ArctisAurora.Core.Filing;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Testing;
using ArctisAurora.Core.UI;
using ArctisAurora.EngineWork;
using System.Numerics;
using System.Xml.Linq;

namespace Thorium.Tests
{
    internal static class TextInputTests
    {
        [A_XSDActionDependency("TextInput.TypeWritesText", "Test")]
        private static IEnumerator<int> TypeWritesText(TestContext t)
        {
            TextBoxControl box = new TextBoxControl
            {
                preferredWidth = 240f,
                preferredHeight = 28f,
                horizontalAlignment = HorizontalAlignment.Left,
                verticalAlignment = VerticalAlignment.Top
            };
            t.Show(box);
            yield return 2;

            yield return t.Click(box);
            t.Check(ReferenceEquals(UIEngine.activeControl, box), "a click focuses the text box");

            yield return t.Type("Hi 5!");
            t.Check(box.text == "Hi 5!", "typing writes letters, digits, a space and a symbol");
        }

        #region ---- basic editing ----
        [A_XSDActionDependency("TextInput.WordMoves", "Test")]
        private static IEnumerator<int> WordMoves(TestContext t)
        {
            DocumentEditorControl editor = ShowParagraphs(t, "one two, three", "four");
            DocumentControl content = Content(editor);
            List<BlockControl> p = Paragraphs(editor);
            yield return 2;

            yield return t.Key(Keys.Right, Keys.LeftControl);
            t.Check(content.caretOffset == 4, "Ctrl+Right lands at the start of the next word");
            yield return t.Key(Keys.Right, Keys.LeftControl);
            t.Check(content.caretOffset == 7, "a word stops where punctuation starts");
            yield return t.Key(Keys.Right, Keys.LeftControl);
            t.Check(content.caretOffset == 9, "punctuation and the space after it are one step");

            yield return t.Key(Keys.Left, Keys.LeftControl, Keys.LeftShift);
            t.Check(content.caretOffset == 7 && content.HasSelection, "Ctrl+Shift+Left extends by a word");

            yield return t.Key(Keys.End, Keys.LeftControl);
            t.Check(content.caretBlock == p[1] && content.caretOffset == 4, "Ctrl+End goes to the end of the note");
            yield return t.Key(Keys.Home, Keys.LeftControl);
            t.Check(content.caretBlock == p[0] && content.caretOffset == 0, "Ctrl+Home goes to the start of the note");

            content.SetCaret(p[1], 0);
            yield return t.Key(Keys.Left, Keys.LeftControl);
            t.Check(content.caretBlock == p[0] && content.caretOffset == 14, "Ctrl+Left at a block's start crosses into the previous one");

            yield return t.Key(Keys.Backspace, Keys.LeftControl);
            t.Check(p[0].text == "one two, ", "Ctrl+Backspace deletes the word before the caret");

            content.SetCaret(p[0], 0);
            yield return t.Key(Keys.Delete, Keys.LeftControl);
            t.Check(p[0].text == "two, ", "Ctrl+Delete deletes the word and the space after it");

            yield return t.Key(Keys.Z, Keys.LeftControl);
            t.Check(p[0].text == "one two, " && content.caretOffset == 0 && !content.HasSelection,
                "undoing a word delete puts the caret back, not a selection");
            yield return t.Key(Keys.Z, Keys.LeftControl);
            t.Check(p[0].text == "one two, three" && content.caretOffset == 14, "undoing Ctrl+Backspace restores the word");
        }

        [A_XSDActionDependency("TextInput.UndoReselects", "Test")]
        private static IEnumerator<int> UndoReselects(TestContext t)
        {
            DocumentEditorControl editor = ShowParagraphs(t, "hello world");
            DocumentControl content = Content(editor);
            BlockControl p = Paragraphs(editor)[0];
            yield return 2;

            content.SetCaret(p, 6);
            content.SetCaret(p, 11, true);
            yield return t.Key(Keys.X, Keys.LeftControl);
            t.Check(p.text == "hello " && ClipboardText.Get() == "world", "Ctrl+X removes the selection onto the clipboard");

            yield return t.Key(Keys.Z, Keys.LeftControl);
            t.Check(p.text == "hello world", "undo brings the cut text back");
            t.Check(editor.SelectedRange(out DocumentAddress from, out DocumentAddress to) && from.offset == 6 && to.offset == 11
                && content.caretOffset == 11, "and selects it again, caret at the end it was on");

            content.SetCaret(p, 11);
            content.SetCaret(p, 6, true);
            yield return t.Key(Keys.Delete);
            yield return t.Key(Keys.Z, Keys.LeftControl);
            t.Check(content.HasSelection && content.caretOffset == 6, "a backwards selection comes back backwards");

            content.SetCaret(p, 3);
            yield return t.Key(Keys.Backspace);
            yield return t.Key(Keys.Z, Keys.LeftControl);
            t.Check(p.text == "hello world" && content.caretOffset == 3 && !content.HasSelection,
                "undoing Backspace leaves the caret after the restored character");

            yield return t.Key(Keys.Delete);
            yield return t.Key(Keys.Z, Keys.LeftControl);
            t.Check(content.caretOffset == 3 && !content.HasSelection, "undoing Delete leaves the caret before it");
        }

        [A_XSDActionDependency("TextInput.PasteRoundTrip", "Test")]
        private static IEnumerator<int> PasteRoundTrip(TestContext t)
        {
            RichTextDocument fixture = DocumentXml.Parse(new XElement("Document",
                new XElement("Block", new XElement("Run", new XAttribute("Text", "bold"), new XAttribute("Bold", "true"))),
                Block("plain")));
            DocumentEditorControl editor = ShowFixture(t, fixture);
            DocumentControl content = Content(editor);
            List<BlockControl> p = Paragraphs(editor);
            yield return 2;

            string before = DocumentXml.ToXml(editor.session.document).ToString();
            t.Check(p[0].StyleAt(2).IsBold, "the fixture's first paragraph is bold");

            content.SetCaret(p[0], 0);
            content.SetCaret(p[1], 5, true);
            yield return t.Key(Keys.C, Keys.LeftControl);
            t.Check(ClipboardText.Get() == "bold" + Environment.NewLine + "plain", "Ctrl+C puts the selection on the clipboard as lines");

            content.SetCaret(p[1], 5);
            yield return t.Key(Keys.V, Keys.LeftControl);
            p = Paragraphs(editor);
            t.Check(p.Count == 3 && p[1].text == "plainbold" && p[2].text == "plain", "a two-paragraph paste splits the block it lands in");
            t.Check(p.Count == 3 && p[1].StyleAt(8).IsBold && !p[1].StyleAt(4).IsBold, "our own copy pastes with its formatting");
            t.Check(content.caretBlock == p[^1] && content.caretOffset == 5, "the caret ends after the pasted text");

            string pasted = DocumentXml.ToXml(editor.session.document).ToString();
            yield return t.Key(Keys.Z, Keys.LeftControl);
            t.Check(DocumentXml.ToXml(editor.session.document).ToString() == before, "undo restores the note exactly");
            yield return t.Key(Keys.Y, Keys.LeftControl);
            t.Check(DocumentXml.ToXml(editor.session.document).ToString() == pasted, "redo pastes it exactly again");
        }

        [A_XSDActionDependency("TextInput.PastePlainLines", "Test")]
        private static IEnumerator<int> PastePlainLines(TestContext t)
        {
            DocumentEditorControl editor = ShowParagraphs(t, "x");
            DocumentControl content = Content(editor);
            content.SetCaret(Paragraphs(editor)[0], 1);
            yield return 2;

            ClipboardText.Set("a\r\nb\tc\nd");
            yield return t.Key(Keys.V, Keys.LeftControl);
            List<BlockControl> p = Paragraphs(editor);
            t.Check(p.Count == 3 && p[0].text == "xa" && p[1].text == "b c" && p[2].text == "d",
                "plain text pastes as paragraphs, a tab as a space");

            yield return t.Key(Keys.Z, Keys.LeftControl);
            p = Paragraphs(editor);
            t.Check(p.Count == 1 && p[0].text == "x", "one undo takes the whole paste back");
        }

        [A_XSDActionDependency("TextInput.FieldClipboardAndUndo", "Test")]
        private static IEnumerator<int> FieldClipboardAndUndo(TestContext t)
        {
            TextBoxControl box = new TextBoxControl
            {
                preferredWidth = 240f,
                preferredHeight = 28f,
                horizontalAlignment = HorizontalAlignment.Left,
                verticalAlignment = VerticalAlignment.Top
            };
            t.Show(box);
            yield return 2;

            yield return t.Click(box);
            yield return t.Type("hello world");
            yield return t.Key(Keys.A, Keys.LeftControl);
            yield return t.Key(Keys.C, Keys.LeftControl);
            t.Check(ClipboardText.Get() == "hello world", "Ctrl+C in a field copies its selection");

            yield return t.Key(Keys.End);
            ClipboardText.Set("a\nb");
            yield return t.Key(Keys.V, Keys.LeftControl);
            t.Check(box.text == "hello worlda b", "a field pastes line breaks as spaces");

            yield return t.Key(Keys.Z, Keys.LeftControl);
            t.Check(box.text == "hello world", "undo in a field takes the paste back");
            yield return t.Key(Keys.Y, Keys.LeftControl);
            t.Check(box.text == "hello worlda b", "redo in a field puts it back");

            yield return t.Key(Keys.Backspace, Keys.LeftControl);
            t.Check(box.text == "hello worlda ", "Ctrl+Backspace in a field deletes a word");
        }

        [A_XSDActionDependency("TextInput.DragMovesText", "Test")]
        private static IEnumerator<int> DragMovesText(TestContext t)
        {
            DocumentEditorControl editor = ShowParagraphs(t, "move me", "target");
            DocumentControl content = Content(editor);
            List<BlockControl> p = Paragraphs(editor);
            yield return 2;

            content.SetCaret(p[0], 0);
            content.SetCaret(p[0], 7, true);
            yield return t.Drag(p[0], EndOf(p[1]));
            yield return 2;
            t.Check(p[0].text == "" && p[1].text == "targetmove me", "dragging a selection moves it to the drop");
            t.Check(content.HasSelection && content.caretBlock == p[1] && content.caretOffset == 13, "the moved text stays selected");

            yield return t.Key(Keys.Z, Keys.LeftControl);
            t.Check(p[0].text == "move me" && p[1].text == "target", "one undo puts the move back");

            content.SetCaret(p[0], 0);
            content.SetCaret(p[0], 7, true);
            yield return t.Click(p[0]);
            yield return 2;
            t.Check(!content.HasSelection && p[0].text == "move me", "a click inside the selection only places the caret");
        }

        [A_XSDActionDependency("TextInput.DragBetweenNotes", "Test")]
        private static IEnumerator<int> DragBetweenNotes(TestContext t)
        {
            DocumentEditorControl a = NewEditor();
            DocumentEditorControl b = NewEditor();
            StackPanelControl both = new StackPanelControl
            {
                orientation = StackPanelControl.Orientation.Vertical,
                horizontalAlignment = HorizontalAlignment.Stretch,
                verticalAlignment = VerticalAlignment.Stretch
            };
            a.preferredHeight = b.preferredHeight = 300f;
            both.AddChild(a);
            both.AddChild(b);
            t.Show(both);
            Load(a, Paragraphs("from"));
            Load(b, Paragraphs("to"));
            yield return 2;

            BlockControl source = Paragraphs(a)[0];
            BlockControl target = Paragraphs(b)[0];
            Content(a).SetCaret(source, 0);
            Content(a).SetCaret(source, 4, true);
            a.FocusCaret();
            yield return t.Drag(source, EndOf(target));
            yield return 2;
            t.Check(target.text == "tofrom" && source.text == "", "a drop into another note moves the text there");
            t.Check(ReferenceEquals(UIEngine.activeControl, b), "the note that took the drop takes the focus");

            yield return t.Key(Keys.Z, Keys.LeftControl);
            t.Check(target.text == "to" && source.text == "", "undo in the target takes back only its own half");
        }

        [A_XSDActionDependency("TextInput.RightModifiers", "Test")]
        private static IEnumerator<int> RightModifiers(TestContext t)
        {
            RichTextDocument fixture = DocumentXml.Parse(new XElement("Document",
                new XElement("Block", new XAttribute("List", "Bullet"), new XAttribute("Level", 1),
                    new XElement("Run", new XAttribute("Text", "item")))));
            DocumentEditorControl editor = ShowFixture(t, fixture);
            DocumentControl content = Content(editor);
            BlockControl p = Paragraphs(editor)[0];
            yield return 2;

            yield return t.Key(Keys.Tab, Keys.RightShift);
            t.Check(p.listLevel == 0, "Right Shift+Tab outdents");

            content.SetCaret(p, 0);
            content.SetCaret(p, 4, true);
            yield return t.Key(Keys.C, Keys.RightControl);
            t.Check(ClipboardText.Get() == "item", "Right Ctrl+C copies");

            yield return t.Key(Keys.Z, Keys.RightControl);
            t.Check(p.listLevel == 1, "Right Ctrl+Z undoes");

            TabViewControl view = new TabViewControl
            {
                horizontalAlignment = HorizontalAlignment.Stretch,
                verticalAlignment = VerticalAlignment.Stretch
            };
            view.AddChild(new TabItemControl { header = "one" });
            view.AddChild(new TabItemControl { header = "two" });
            view.AddChild(new TabItemControl { header = "three" });
            StackPanelControl host = new StackPanelControl
            {
                horizontalAlignment = HorizontalAlignment.Stretch,
                verticalAlignment = VerticalAlignment.Stretch
            };
            host.AddChild(view);
            t.Show(host);
            UIEngine.SetActiveControl(view);
            yield return 2;

            yield return t.Key(Keys.Backslash, Keys.RightControl, Keys.RightShift);
            yield return 2;
            t.Check(view.parent is SplitViewControl { orientation: StackPanelControl.Orientation.Vertical },
                "Right Ctrl+Right Shift+\\ splits down");
            t.Check(!host.children.OfType<SplitViewControl>().Any(s => s.children.OfType<SplitViewControl>().Any())
                && view.parent?.parent == host, "and the looser Ctrl+\\ bind does not also fire");
        }
        #endregion

        #region ---- styling ----
        [A_XSDActionDependency("TextInput.StyleSurvivesEnterAndBackspace", "Test")]
        private static IEnumerator<int> StyleSurvivesEnterAndBackspace(TestContext t)
        {
            DocumentEditorControl editor = ShowFixture(t, DocumentXml.Parse(new XElement("Document",
                Block("x"),
                new XElement("Block", RunX("plain "), RunX("word", ("Bold", "true"))),
                new XElement("Block", RunX("ab"), RunX("CD", ("Bold", "true"))),
                Block("tail"))));
            DocumentControl content = Content(editor);
            List<BlockControl> p = Paragraphs(editor);
            yield return 2;

            content.SetCaret(p[0], 1);
            yield return t.Key(Keys.B, Keys.LeftControl);
            yield return t.Type("ab");
            yield return t.Key(Keys.Enter);
            yield return t.Type("c");
            p = Paragraphs(editor);
            t.Check(p[1].text == "c" && p[1].StyleAt(0).IsBold, "a style picked before Enter carries into the new paragraph");

            BlockControl word = p[2];
            content.SetCaret(word, word.Length);
            for (int i = 0; i < 4; i++) yield return t.Key(Keys.Backspace);
            yield return t.Type("z");
            t.Check(word.text == "plain z" && word.StyleAt(6).IsBold, "typing after backspacing out a bold word stays bold");

            BlockControl mixed = p[3];
            content.SetCaret(mixed, 2);
            yield return t.Key(Keys.Backspace);
            yield return t.Type("y");
            t.Check(mixed.text == "ayCD" && !mixed.StyleAt(1).IsBold, "backspacing plain text in front of bold keeps typing plain");

            content.SetCaret(p[0], 0);
            yield return t.Key(Keys.B, Keys.LeftControl);
            BlockControl tail = p[4];
            yield return t.Click(tail);
            yield return t.Type("q");
            int q = tail.text.IndexOf('q');
            t.Check(q >= 0 && !tail.StyleAt(q).IsBold, "a click drops a picked style");
        }

        [A_XSDActionDependency("TextInput.UnderlineToggles", "Test")]
        private static IEnumerator<int> UnderlineToggles(TestContext t)
        {
            DocumentEditorControl editor = ShowParagraphs(t, "under line");
            DocumentControl content = Content(editor);
            BlockControl p = Paragraphs(editor)[0];
            yield return 2;

            content.SetCaret(p, 0);
            content.SetCaret(p, 5, true);
            yield return t.Key(Keys.U, Keys.LeftControl);
            t.Check(p.StyleAt(2).underline && !p.StyleAt(7).underline, "Ctrl+U underlines the selection");
            t.Check(DocumentXml.ToXml(editor.session.document).ToString().Contains("Underline=\"true\""), "an underline saves to XML");

            yield return t.Key(Keys.U, Keys.LeftControl);
            t.Check(!p.StyleAt(2).underline, "Ctrl+U again takes it off");
        }

        [A_XSDActionDependency("TextInput.HighlightApplies", "Test")]
        private static IEnumerator<int> HighlightApplies(TestContext t)
        {
            DocumentEditorControl editor = ShowParagraphs(t, "mark this");
            DocumentControl content = Content(editor);
            BlockControl p = Paragraphs(editor)[0];
            yield return 2;

            content.SetCaret(p, 5);
            content.SetCaret(p, 9, true);
            editor.ApplyStyle(new StyleDelta(highlightHex: "#C8E6A0"));
            t.Check(p.StyleAt(6).highlightHex == "#C8E6A0" && p.StyleAt(2).highlightHex == null, "a highlight lands on the selection only");

            yield return t.Key(Keys.Z, Keys.LeftControl);
            t.Check(p.StyleAt(6).highlightHex == null, "undo takes the highlight off");
        }

        [A_XSDActionDependency("TextInput.DecorationsDraw", "Test")]
        private static IEnumerator<int> DecorationsDraw(TestContext t)
        {
            DocumentEditorControl editor = ShowFixture(t, DocumentXml.Parse(new XElement("Document",
                new XElement("Block",
                    RunX("plain "),
                    RunX("under", ("Underline", "true")),
                    RunX(" "),
                    RunX("struck", ("Strikethrough", "true")),
                    RunX(" "),
                    RunX("highlighted text", ("HighlightHex", "#FFF3A3")),
                    RunX(" "),
                    RunX("red", ("ColorHex", "#E06C75"), ("Underline", "true"))))));
            DocumentControl content = Content(editor);
            BlockControl p = Paragraphs(editor)[0];
            content.SetCaret(p, 34);
            content.SetCaret(p, 38, true);
            yield return 4;

            yield return t.Golden("Decorations", p);
        }

        [A_XSDActionDependency("TextInput.ColorPickerPicks", "Test")]
        private static IEnumerator<int> ColorPickerPicks(TestContext t)
        {
            foreach (string hex in new[] { "#E06C75", "#61AFEF", "#808080", "#000000", "#FFFFFF" })
            {
                ColorPickerControl.TryParseHex(hex, out Vector3 rgb);
                (float h, float s, float v) = ColorPickerControl.RgbToHsv(rgb);
                t.Check(ColorPickerControl.ToHex(ColorPickerControl.HsvToRgb(h, s, v)) == hex, $"{hex} survives HSV and back");
            }

            string? picked = null;
            ColorPickerControl picker = new ColorPickerControl { hex = "#00FF00", padding = new Thickness(20f), onPicked = hex => picked = hex };
            t.Show(picker);
            yield return 2;
            t.Check(picker.hex == "#00FF00", "the picker shows the colour it was given");

            // past the field's top-right corner, which clamps to full saturation and brightness
            yield return t.Drag(picker, new Vector2(picker.arrangedRect.x + 20f + 175f, picker.arrangedRect.y + 12f));
            yield return 2;
            t.Check(picked == "#00FF00", $"dragging to the field's bright corner picks the pure hue, got {picked}");

            yield return t.Golden("ColorPicker", picker);
        }

        [A_XSDActionDependency("TextInput.MarkdownColourRoundTrip", "Test")]
        private static IEnumerator<int> MarkdownColourRoundTrip(TestContext t)
        {
            XElement written = new XElement("Document", new XElement("Block",
                RunX("a "),
                RunX("red", ("ColorHex", "#E06C75")),
                RunX(" "),
                RunX("marked", ("HighlightHex", MarkdownFormat.DefaultHighlightHex)),
                RunX(" "),
                RunX("pink", ("HighlightHex", "#F5C2DC"), ("Underline", "true"), ("Bold", "true"))));

            string md = MarkdownFormat.Write(written);
            t.Check(md.Contains("<span style=\"color:#E06C75\">red</span>") && md.Contains("==marked==")
                && md.Contains("<mark style=\"background:#F5C2DC\">") && md.Contains("<u>"), $"colour, highlight and underline write as Obsidian reads them: {md}");

            List<XElement> back = MarkdownFormat.Read(md, "n").Elements("Block").First().Elements("Run").ToList();
            XElement? pink = back.FirstOrDefault(r => (string?)r.Attribute("Text") == "pink");
            t.Check(back.Any(r => (string?)r.Attribute("Text") == "red" && (string?)r.Attribute("ColorHex") == "#E06C75")
                && back.Any(r => (string?)r.Attribute("Text") == "marked" && (string?)r.Attribute("HighlightHex") == MarkdownFormat.DefaultHighlightHex)
                && pink != null && (string?)pink.Attribute("HighlightHex") == "#F5C2DC" && (string?)pink.Attribute("Underline") == "true"
                && (string?)pink.Attribute("Bold") == "true", $"and read back into the same runs: {string.Join(" | ", back)}");

            List<XElement> obsidian = MarkdownFormat.Read(
                "x <span style=\"color: #61afef\">blue</span> <mark style=\"background: #FFB8EBA6;\">pinkish</mark> <b>not ours</b>", "n")
                .Elements("Block").First().Elements("Run").ToList();
            t.Check(obsidian.Any(r => (string?)r.Attribute("Text") == "blue" && (string?)r.Attribute("ColorHex") == "#61AFEF")
                && obsidian.Any(r => (string?)r.Attribute("Text") == "pinkish" && (string?)r.Attribute("HighlightHex") == "#FFB8EB")
                && obsidian.Any(r => ((string?)r.Attribute("Text") ?? "").Contains("<b>not ours</b>")),
                $"hand-written Obsidian spans read, other HTML stays text: {string.Join(" | ", obsidian)}");

            yield return 0;
        }
        #endregion

        #region ---- lists ----
        [A_XSDActionDependency("TextInput.ListMarkersPerList", "Test")]
        private static IEnumerator<int> ListMarkersPerList(TestContext t)
        {
            DocumentEditorControl editor = ShowFixture(t, DocumentXml.Parse(new XElement("Document",
                Item("a"), Item("b"), Item("c", 1), Item("d"), Block("plain"), Item("e"))));
            DocumentControl content = Content(editor);
            List<BlockControl> p = Paragraphs(editor);
            yield return 2;

            t.Check(p[0].shownMarker == ListMarker.Disc && p[2].shownMarker == ListMarker.Disc, "the default marker is a filled circle");

            ContextMenuSubmenu? markers = ContextMenus.Get("note")?.entries.OfType<ContextMenuSubmenu>().FirstOrDefault();
            t.Check(markers != null && markers.entries.OfType<ContextMenuButton>().Count(b => b.action != null) == 11,
                "the note menu offers all eleven markers, each bound");
            t.Check(p[0].listNumber == 1 && p[1].listNumber == 2 && p[2].listNumber == 1 && p[3].listNumber == 3 && p[5].listNumber == 1,
                "items count per level, a nested list restarts, a paragraph ends the list");

            content.SetCaret(p[1], 0);
            editor.SetListMarker(ListMarker.UpperRoman);
            yield return 2;
            t.Check(p[0].shownMarker == ListMarker.UpperRoman && p[1].shownMarker == ListMarker.UpperRoman
                && p[3].shownMarker == ListMarker.UpperRoman, "a marker applies to its whole level of the list");
            t.Check(p[2].shownMarker == ListMarker.Disc && p[5].shownMarker == ListMarker.Disc, "the nested level and the next list keep theirs");
            t.Check(ListMarkers.Format(p[3].listNumber, p[3].shownMarker) == "III." && ListMarkers.Format(28, ListMarker.LowerAlpha) == "ab."
                && ListMarkers.Format(14, ListMarker.LowerRoman) == "xiv.", "numbers write as the marker says");
            t.Check(DocumentXml.ToXml(editor.session.document).ToString().Contains("Marker=\"UpperRoman\""), "a chosen marker saves to XML");

            content.SetCaret(p[3], 1);
            yield return t.Key(Keys.Enter);
            yield return 2;
            p = Paragraphs(editor);
            t.Check(p[4].listKind == ListKind.Bullet && p[4].shownMarker == ListMarker.UpperRoman && p[4].listNumber == 4,
                "Enter continues the list in its marker");

            yield return t.Key(Keys.Z, Keys.LeftControl);
            yield return t.Key(Keys.Z, Keys.LeftControl);
            yield return 2;
            p = Paragraphs(editor);
            t.Check(p[0].shownMarker == ListMarker.Disc, "undo puts the marker back");

            yield return t.Key(Keys.End, Keys.LeftControl);
            yield return t.Key(Keys.Enter);
            yield return t.Key(Keys.Enter);
            yield return t.Type("1. x");
            yield return 2;
            BlockControl typed = Paragraphs(editor)[^1];
            t.Check(typed.listKind == ListKind.Bullet && typed.listMarker == ListMarker.Decimal && typed.text == "x",
                "typing 1. at a paragraph's start starts a numbered list");

            string md = MarkdownFormat.Write(DocumentXml.ToXml(editor.session.document));
            t.Check(md.Contains("- a") && md.EndsWith("1. x"), $"shapes write as - and numbers as N. in Markdown: {md}");
        }

        [A_XSDActionDependency("TextInput.EmptyItemEnterOutdents", "Test")]
        private static IEnumerator<int> EmptyItemEnterOutdents(TestContext t)
        {
            DocumentEditorControl editor = ShowFixture(t, DocumentXml.Parse(new XElement("Document",
                Item("top"), Item("middle", 1), Item("deep", 2))));
            DocumentControl content = Content(editor);
            yield return 2;

            content.SetCaret(Paragraphs(editor)[2], 4);
            yield return t.Key(Keys.Enter);
            BlockControl empty = Paragraphs(editor)[3];
            t.Check(empty.listLevel == 2, "Enter at an item's end makes an item at its level");

            yield return t.Key(Keys.Enter);
            t.Check(empty.listKind == ListKind.Bullet && empty.listLevel == 1, "Enter on an empty item outdents it");
            yield return t.Key(Keys.Enter);
            t.Check(empty.listLevel == 0, "and again");
            yield return t.Key(Keys.Enter);
            t.Check(empty.listKind == ListKind.None && Paragraphs(editor).Count == 4, "at the top level it becomes a plain paragraph");

            yield return t.Key(Keys.Z, Keys.LeftControl);
            t.Check(empty.listKind == ListKind.Bullet && empty.listLevel == 0, "each step is its own undo");
        }

        [A_XSDActionDependency("TextInput.ListMarkersDraw", "Test")]
        private static IEnumerator<int> ListMarkersDraw(TestContext t)
        {
            List<XElement> items = new List<XElement>();
            foreach (ListMarker marker in Enum.GetValues<ListMarker>())
            {
                XElement item = Item(marker.ToString());
                item.SetAttributeValue("Marker", marker.ToString());
                items.Add(item);
                items.Add(Block(""));
            }

            DocumentEditorControl editor = ShowFixture(t, DocumentXml.Parse(new XElement("Document", items)));
            yield return 4;

            yield return t.Golden("Markers", Content(editor));
        }
        #endregion

        private static XElement RunX(string text, params (string name, string value)[] attributes)
        {
            XElement run = new XElement("Run", new XAttribute("Text", text));
            foreach ((string name, string value) in attributes)
                run.SetAttributeValue(name, value);
            return run;
        }

        private static XElement Item(string text, int level = 0)
        {
            XElement item = Block(text);
            item.SetAttributeValue("List", "Bullet");
            if (level > 0) item.SetAttributeValue("Level", level);
            return item;
        }

        #region ---- helpers ----
        private static DocumentEditorControl NewEditor() => new DocumentEditorControl
        {
            horizontalAlignment = HorizontalAlignment.Stretch,
            verticalAlignment = VerticalAlignment.Stretch
        };

        // A note of plain paragraphs, opened from a file so it has undo, caret focused at the start.
        private static DocumentEditorControl ShowParagraphs(TestContext t, params string[] texts) =>
            ShowFixture(t, Paragraphs(texts));

        private static DocumentEditorControl ShowFixture(TestContext t, RichTextDocument fixture)
        {
            DocumentEditorControl editor = NewEditor();
            t.Show(editor);
            Load(editor, fixture);
            return editor;
        }

        private static void Load(DocumentEditorControl editor, RichTextDocument fixture)
        {
            string path = Path.Combine(Path.GetTempPath(), $"aurora-edit-{Guid.NewGuid():N}.xml");
            DocumentXml.Save(fixture, path);
            editor.LoadPath(path);
            File.Delete(path);
            foreach (Control entry in fixture.blocks)
                entry.Destroy();

            Content(editor).SetCaret(Paragraphs(editor)[0], 0);
            editor.FocusCaret();
        }

        private static RichTextDocument Paragraphs(params string[] texts) =>
            DocumentXml.Parse(new XElement("Document", texts.Select(Block)));

        private static List<BlockControl> Paragraphs(DocumentEditorControl editor) =>
            editor.session.document.blocks.OfType<BlockControl>().ToList();

        private static DocumentControl Content(DocumentEditorControl editor) =>
            (DocumentControl)Paragraphs(editor)[0].parent;

        // Just inside a block's right edge, which resolves to the end of its last line.
        private static Vector2 EndOf(BlockControl block) =>
            new Vector2(block.arrangedRect.x + block.arrangedRect.width - 2f, block.arrangedRect.y + block.arrangedRect.height * 0.5f);
        #endregion

        #region ---- tables ----
        [A_XSDActionDependency("TextInput.TableEnterAddsLine", "Test")]
        private static IEnumerator<int> TableEnterAddsLine(TestContext t)
        {
            DocumentControl content = ShowNote(t, TableNote(120f, 120f), out TableControl table, out RichTextDocument document);
            content.SetCaret(Cell(table, 0), Cell(table, 0).Length);
            yield return 2;

            yield return t.Key(Keys.Enter);
            yield return t.Type("x");
            t.Check(Blocks(table, 0).Count == 2, "Enter in a cell adds a second line to it");
            t.Check(Cell(table, 0, 1).text == "x", "typing lands on the cell's new line");
            t.Check(document.blocks.Count == 3, "the note's own blocks are untouched");

            yield return t.Key(Keys.Z, Keys.LeftControl);
            yield return t.Key(Keys.Z, Keys.LeftControl);
            t.Check(Blocks(table, 0).Count == 1 && Cell(table, 0).text == "r0c0", "undo joins the cell back into one line");
        }

        [A_XSDActionDependency("TextInput.TableTabStepsCells", "Test")]
        private static IEnumerator<int> TableTabStepsCells(TestContext t)
        {
            DocumentControl content = ShowNote(t, TableNote(120f, 120f), out TableControl table);
            content.SetCaret(Cell(table, 0), 2);
            yield return 2;

            yield return t.Key(Keys.Tab);
            yield return t.Type("x");
            t.Check(Cell(table, 1).text == "xr0c1", "Tab moves the caret to the start of the next cell");

            yield return t.Key(Keys.Tab, Keys.LeftShift);
            t.Check(content.caretBlock == Cell(table, 0) && content.caretOffset == 0, "Shift+Tab moves back to the previous cell");
        }

        [A_XSDActionDependency("TextInput.TableLeftCrossesCells", "Test")]
        private static IEnumerator<int> TableLeftCrossesCells(TestContext t)
        {
            DocumentControl content = ShowNote(t, TableNote(120f, 120f), out TableControl table);
            content.SetCaret(Cell(table, 1), 0);
            yield return 2;

            yield return t.Key(Keys.Left);
            t.Check(content.caretBlock == Cell(table, 0) && content.caretOffset == Cell(table, 0).Length,
                "Left from a cell's start lands at the end of the previous cell");
        }

        [A_XSDActionDependency("TextInput.TableBackspaceKeepsCells", "Test")]
        private static IEnumerator<int> TableBackspaceKeepsCells(TestContext t)
        {
            DocumentControl content = ShowNote(t, TableNote(120f, 120f), out TableControl table);
            content.SetCaret(Cell(table, 1), 0);
            yield return 2;

            yield return t.Key(Keys.Backspace);
            t.Check(Cell(table, 0).text == "r0c0" && Cell(table, 1).text == "r0c1", "Backspace at a cell's start deletes nothing");
            t.Check(!content.HasSelection && content.caretBlock == Cell(table, 1) && content.caretOffset == 0,
                "the caret stays where it was, with nothing selected");
        }

        [A_XSDActionDependency("TextInput.TableWideScrolls", "Test")]
        private static IEnumerator<int> TableWideScrolls(TestContext t)
        {
            DocumentControl content = ShowNote(t, TableNote(400f, 400f, 400f), out TableControl table);
            content.SetCaret(Cell(table, 0), 0);
            yield return 2;

            yield return t.Key(Keys.Tab);
            yield return t.Key(Keys.Tab);
            yield return 2;
            ScrollableControl viewport = (ScrollableControl)table.parent;
            t.Check(viewport.GetScrollOffset().X > 0f, "a caret in a column past the page scrolls the table sideways");
            t.Check(viewport.arrangedRect.Overlaps(Cell(table, 2).arrangedRect), "the caret's cell is in view");
        }

        [A_XSDActionDependency("TextInput.TableXmlRoundTrip", "Test")]
        private static IEnumerator<int> TableXmlRoundTrip(TestContext t)
        {
            XElement written = DocumentXml.ToXml(TableNote(120f, 160f));
            XElement cell = written.Descendants().First(e => e.Name.LocalName == "Cell");
            t.Check(written.Descendants().Count(e => e.Name.LocalName == "Column") == 2, "both column widths are written");

            XElement source = new XElement("Document",
                new XElement("Table",
                    new XElement("Column", new XAttribute("Width", 120)),
                    new XElement("Row",
                        new XElement("Cell",
                            new XElement("Block", new XElement("Run", new XAttribute("Text", "a"), new XAttribute("Bold", "true"))),
                            Block("b")))));
            XElement once = DocumentXml.ToXml(DocumentXml.Parse(source));
            XElement twice = DocumentXml.ToXml(DocumentXml.Parse(once));
            t.Check(XNode.DeepEquals(once, twice), "a table survives a save and load unchanged");
            t.Check(once.Descendants().Count(e => e.Name.LocalName == "Block") == 2, "a two-line cell keeps both blocks");
            t.Check(once.Descendants().Any(e => e.Name.LocalName == "Run" && (string?)e.Attribute("Bold") == "true"), "a bold run stays bold");
            t.Check(cell.Elements().Count() == 1, "a one-line cell writes one block");
            yield break;
        }

        [A_XSDActionDependency("TextInput.TableDraws", "Test")]
        private static IEnumerator<int> TableDraws(TestContext t)
        {
            DocumentControl content = ShowNote(t, TableNote(120f, 160f, 120f), out TableControl table);
            content.SetCaret(Cell(table, 1), Cell(table, 1).Length);
            yield return 2;

            yield return t.Type(" wraps onto a second line");
            yield return 2;
            yield return t.Golden("Table", (Control)table.parent);
        }

        [A_XSDActionDependency("TextInput.TableOverflowDraws", "Test")]
        private static IEnumerator<int> TableOverflowDraws(TestContext t)
        {
            DocumentControl content = ShowNote(t, TableNote(400f, 400f, 400f), out TableControl table);
            content.SetCaret(Cell(table, 2), Cell(table, 2).Length);
            yield return 2;

            ((ScrollableControl)table.parent).SetScrollOffset(new System.Numerics.Vector2(10000f, 0f));
            yield return 2;
            yield return t.Golden("Scrolled", (Control)table.parent);
        }

        [A_XSDActionDependency("TextInput.TableWheelAxes", "Test")]
        private static IEnumerator<int> TableWheelAxes(TestContext t)
        {
            ShowNote(t, TableNote(400f, 400f, 400f), out TableControl table);
            yield return 2;

            ScrollableControl viewport = (ScrollableControl)table.parent;
            bool took = viewport.OnPointerScroll(new PointerEvent { delta = new System.Numerics.Vector2(0f, -1f) });
            t.Check(!took && viewport.GetScrollOffset().X == 0f, "a vertical wheel over a sideways table passes it on");

            took = viewport.OnPointerScroll(new PointerEvent { delta = new System.Numerics.Vector2(-1f, 0f) });
            t.Check(took && viewport.GetScrollOffset().X > 0f, "a horizontal wheel scrolls the table");
        }

        [A_XSDActionDependency("TextInput.TableSplitsAcrossPages", "Test")]
        private static IEnumerator<int> TableSplitsAcrossPages(TestContext t)
        {
            XElement table = new XElement("Table", new XElement("Column", new XAttribute("Width", 400)));
            for (int r = 0; r < 12; r++)
                table.Add(new XElement("Row", new XElement("Cell", Enumerable.Range(0, 8).Select(i => Block($"row {r} line {i}")))));
            RichTextDocument fixture = DocumentXml.Parse(new XElement("Document", Block("before"), table, Block("after")));

            DocumentControl content = ShowNote(t, fixture, out TableControl shown);
            yield return 2;

            List<StackPanelControl> cells = shown.Cells();
            int pushed = -1;
            for (int r = 1; r < cells.Count && pushed < 0; r++)
                if (cells[r].arrangedRect.y - cells[r].margin.top > cells[r - 1].arrangedRect.Bottom + cells[r - 1].margin.bottom + 1f)
                    pushed = r;
            t.Check(pushed > 0, "a row that would cross the page break starts on the next page");
            if (pushed < 0) yield break;

            content.SetCaret(Blocks(shown, pushed)[0], 0);
            yield return t.Key(Keys.Right);
            yield return t.Key(Keys.Left);
            yield return 2;
            yield return t.Golden("PageBreak");
        }

        // A paragraph, a two-row table whose cells read rNcM, and a paragraph.
        private static RichTextDocument TableNote(params float[] widths)
        {
            XElement table = new XElement("Table", widths.Select(w => new XElement("Column", new XAttribute("Width", w))));
            for (int r = 0; r < 2; r++)
                table.Add(new XElement("Row", widths.Select((_, c) => new XElement("Cell", Block($"r{r}c{c}")))));

            return DocumentXml.Parse(new XElement("Document", Block("before"), table, Block("after")));
        }

        private static XElement Block(string text) => new XElement("Block", new XElement("Run", new XAttribute("Text", text)));

        // Opens the note from a file, so it has undo, with the caret focused; the table is its second entry.
        private static DocumentControl ShowNote(TestContext t, RichTextDocument fixture, out TableControl table) =>
            ShowNote(t, fixture, out table, out _);

        private static DocumentControl ShowNote(TestContext t, RichTextDocument fixture, out TableControl table, out RichTextDocument document)
        {
            DocumentEditorControl editor = new DocumentEditorControl
            {
                horizontalAlignment = HorizontalAlignment.Stretch,
                verticalAlignment = VerticalAlignment.Stretch
            };
            t.Show(editor);

            string path = Path.Combine(Path.GetTempPath(), $"aurora-table-{Guid.NewGuid():N}.xml");
            DocumentXml.Save(fixture, path);
            editor.LoadPath(path);
            File.Delete(path);
            foreach (Control entry in fixture.blocks)
                entry.Destroy();

            document = editor.session.document;
            BlockControl first = (BlockControl)document.blocks[0];
            DocumentControl content = (DocumentControl)first.parent;
            content.SetCaret(first, 0);
            editor.FocusCaret();

            table = (TableControl)document.blocks[1];
            return content;
        }

        private static List<BlockControl> Blocks(TableControl table, int cell) =>
            table.Cells()[cell].children.OfType<BlockControl>().ToList();

        private static BlockControl Cell(TableControl table, int cell, int block = 0) => Blocks(table, cell)[block];
        #endregion
    }
}
