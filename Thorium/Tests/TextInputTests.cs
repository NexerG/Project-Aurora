using ArctisAurora.Core.Filing;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Testing;
using ArctisAurora.Core.UI;
using ArctisAurora.EngineWork;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
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

        #region ---- pictures ----
        [A_XSDActionDependency("TextInput.PictureInline", "Test")]
        private static IEnumerator<int> PictureInline(TestContext t)
        {
            string dir = PictureFolder();
            string small = PicturePng(Path.Combine(dir, "small.png"), 50, 40);
            string wide = PicturePng(Path.Combine(dir, "wide.png"), 4000, 100);
            RichTextDocument fixture = DocumentXml.Parse(new XElement("Document",
                new XElement("Block", RunX("abc"), PictureRun(small, ("Width", "100"), ("Height", "80")), RunX("def")),
                new XElement("Block", RunX("abc"), PictureRun(wide))));
            DocumentEditorControl editor = ShowFixture(t, fixture);
            yield return 2;

            List<BlockControl> p = Paragraphs(editor);
            float advance = p[0].CaretAt(4).x - p[0].CaretAt(3).x;
            float zoom = advance / 100f;
            t.Check(advance > 0f && MathF.Abs(p[0].Lines[0].ascent - 80f * zoom) < 0.01f,
                $"an inline picture advances its width and its height stands on the baseline (advance {advance}, ascent {p[0].Lines[0].ascent})");
            t.Check(p[0].Lines.Count == 1, "a picture that fits stays on the line");

            IReadOnlyList<TextLine> lines = p[1].Lines;
            t.Check(lines.Count == 2 && lines[0].segments.Sum(s => s.charCount) == 3 && lines[1].segments[0].charStart == 3,
                "a picture that does not fit breaks before itself, leaving the word in front of it");
            t.Check(lines.Count == 2 && lines[1].width <= p[1].arrangedRect.width,
                "an unsized picture is fitted to the column");

            Directory.Delete(dir, true);
        }

        [A_XSDActionDependency("TextInput.PictureEditUndo", "Test")]
        private static IEnumerator<int> PictureEditUndo(TestContext t)
        {
            DocumentEditorControl editor = ShowParagraphs(t, "ab");
            DocumentControl content = Content(editor);
            content.SetCaret(Paragraphs(editor)[0], 2);
            yield return 2;

            string attachments = Path.Combine(Path.GetDirectoryName(editor.session.path)!, "attachments");
            using (Image<Rgba32> image = new Image<Rgba32>(30, 20, new Rgba32(200, 80, 40)))
                editor.PasteImage(image);
            yield return 2;

            BlockControl p = Paragraphs(editor)[0];
            string? saved = p.spans.FirstOrDefault(s => s.IsPicture).imageSource;
            t.Check(p.text == "ab" + BlockControl.PictureChar && PictureCount(p) == 1, "a pasted picture is one character with a picture span");
            t.Check(saved != null && File.Exists(saved) && Path.GetDirectoryName(saved) == attachments, "the pasted picture is saved under attachments");

            yield return t.Type("x");
            t.Check(p.text == "ab" + BlockControl.PictureChar + "x" && PictureCount(p) == 1, "typing after a picture at the block's end starts a text span");
            content.SetCaret(p, 2);
            yield return t.Type("y");
            t.Check(p.text == "aby" + BlockControl.PictureChar + "x" && PictureCount(p) == 1, "typing before a picture extends the text in front of it");

            string before = DocumentXml.ToXml(editor.session.document).ToString();
            content.SetCaret(p, 1);
            content.SetCaret(p, 5, true);
            yield return t.Key(Keys.Delete);
            p = Paragraphs(editor)[0];
            t.Check(p.text == "a" && PictureCount(p) == 0, "a range delete takes the picture with it");

            yield return t.Key(Keys.Z, Keys.LeftControl);
            p = Paragraphs(editor)[0];
            t.Check(DocumentXml.ToXml(editor.session.document).ToString() == before && PictureCount(p) == 1, "undo puts the picture back exactly");

            if (saved != null) File.Delete(saved);
            if (Directory.Exists(attachments) && !Directory.EnumerateFileSystemEntries(attachments).Any()) Directory.Delete(attachments);
        }

        [A_XSDActionDependency("TextInput.PictureRoundTrip", "Test")]
        private static IEnumerator<int> PictureRoundTrip(TestContext t)
        {
            string dir = PictureFolder();
            string picture = PicturePng(Path.Combine(dir, "attachments", "my pic.png"), 30, 20);

            foreach (string extension in new[] { ".xml", ".md" })
            {
                RichTextDocument fixture = DocumentXml.Parse(new XElement("Document",
                    new XElement("Block", RunX("a"), PictureRun(picture, ("Width", "120")))));
                string path = Path.Combine(dir, "note" + extension);
                fixture.Save(path);
                DestroyBlocks(fixture);

                string written = File.ReadAllText(path);
                t.Check(written.Contains(extension == ".md" ? "![|120](attachments/my%20pic.png)" : "Image=\"attachments/my pic.png\""),
                    $"{extension} writes the picture relative to the note: {written}");

                RichTextDocument back = RichTextDocument.Load(path);
                StyleSpan? span = back.blocks.OfType<BlockControl>().First().spans.Where(s => s.IsPicture).Cast<StyleSpan?>().FirstOrDefault();
                t.Check(span is { count: 1, imageWidth: 120f } && string.Equals(span.Value.imageSource, picture, StringComparison.OrdinalIgnoreCase),
                    $"{extension} reads it back as the same picture span");
                DestroyBlocks(back);
            }

            RichTextDocument plain = DocumentXml.Parse(new XElement("Document",
                new XElement("Block", RunX("a"), PictureRun(picture))));
            string txt = Path.Combine(dir, "note.txt");
            plain.Save(txt);
            DestroyBlocks(plain);
            t.Check(File.ReadAllText(txt) == "a", "plain text drops the picture");

            Directory.Delete(dir, true);
            yield break;
        }

        [A_XSDActionDependency("TextInput.PictureResize", "Test")]
        private static IEnumerator<int> PictureResize(TestContext t)
        {
            string dir = PictureFolder();
            string small = PicturePng(Path.Combine(dir, "small.png"), 50, 40);
            string wide = PicturePng(Path.Combine(dir, "wide.png"), 4000, 100);
            RichTextDocument fixture = DocumentXml.Parse(new XElement("Document",
                new XElement("Block", PictureRun(wide)),
                new XElement("Block", RunX("ab"), PictureRun(small, ("Width", "100")))));
            DocumentEditorControl editor = ShowFixture(t, fixture);
            DocumentControl content = Content(editor);
            yield return 2;

            List<BlockControl> p = Paragraphs(editor);
            yield return t.Click(p[0]);
            t.Check(content.HasSelection && content.caretBlock == p[0] && content.caretOffset == 1, "a click on a picture selects it");
            t.Check(Handles(content).Count(h => h.arrangedRect.width > 0f) == 8, "a selected picture shows eight handles");

            content.SetCaret(p[1], 2);
            content.SetCaret(p[1], 3, true);
            yield return 2;
            p[1].PictureBox(2, out LayoutRect box);
            float zoom = box.width / 100f;

            DocumentControl.PictureHandle corner = Handles(content).First(h => h.right && h.bottom);
            yield return t.Drag(corner, Centre(corner) + new Vector2(50f * zoom, 0f));
            t.Check(Stored(p[1]) == new Vector2(150f, 0f), $"a corner keeps the aspect and writes the width only: {Stored(p[1])}");
            p[1].PictureBox(2, out box);
            t.Check(MathF.Abs(box.height - 120f * zoom) < 0.5f, $"and the height follows it: {box.height}");

            DocumentControl.PictureHandle side = Handles(content).First(h => h.right && !h.top && !h.bottom);
            yield return t.Drag(side, Centre(side) + new Vector2(20f * zoom, 0f));
            t.Check(Stored(p[1]) == new Vector2(170f, 120f), $"a side stretches one axis and writes both: {Stored(p[1])}");

            yield return t.Key(Keys.Z, Keys.LeftControl);
            t.Check(Stored(Paragraphs(editor)[1]) == new Vector2(150f, 0f), "undo takes back the side drag");
            yield return t.Key(Keys.Z, Keys.LeftControl);
            t.Check(Stored(Paragraphs(editor)[1]) == new Vector2(100f, 0f), "and then the corner drag");
            t.Check(content.HasSelection && content.caretOffset == 3, "undo leaves the picture selected");

            Directory.Delete(dir, true);
        }

        [A_XSDActionDependency("TextInput.PictureHandlesDraw", "Test")]
        private static IEnumerator<int> PictureHandlesDraw(TestContext t)
        {
            string dir = PictureFolder();
            string picture = PicturePng(Path.Combine(dir, "picture.png"), 60, 40);
            RichTextDocument fixture = DocumentXml.Parse(new XElement("Document",
                new XElement("Block", RunX("ab "), PictureRun(picture), RunX(" cd"))));
            DocumentEditorControl editor = ShowFixture(t, fixture);
            DocumentControl content = Content(editor);
            BlockControl p = Paragraphs(editor)[0];
            content.SetCaret(p, 3);
            content.SetCaret(p, 4, true);
            yield return 2;

            yield return t.Golden("Selected", editor);
            Directory.Delete(dir, true);
        }

        [A_XSDActionDependency("TextInput.PictureWrapLayout", "Test")]
        private static IEnumerator<int> PictureWrapLayout(TestContext t)
        {
            string dir = PictureFolder();
            string picture = PicturePng(Path.Combine(dir, "square.png"), 100, 100);
            string words = string.Join(" ", Enumerable.Repeat("lorem ipsum dolor", 60));

            foreach ((string mode, bool right) in new[] { ("Square", false), ("Square", true), ("TopAndBottom", false), ("Behind", false), ("InFront", false) })
            {
                DocumentEditorControl editor = ShowFixture(t, DocumentXml.Parse(new XElement("Document",
                    new XElement("Block", PictureRun(picture, ("Width", "100"), ("Height", "100"), ("Wrap", mode)), RunX(words)))));
                yield return 2;

                DocumentControl content = Content(editor);
                BlockControl p = Paragraphs(editor)[0];
                float z = content.zoom;
                float column = p.arrangedRect.width;
                if (right)
                {
                    p.SetPicture(0, new StyleSpan { imageWidth = 100f, imageHeight = 100f, wrap = PictureWrap.Square, imageX = column / z - 100f });
                    yield return 2;
                }

                IReadOnlyList<TextLine> lines = p.Lines;
                float edge = 108f * z;
                List<TextLine> beside = lines.Where(l => l.top < edge).ToList();
                List<TextLine> under = lines.Where(l => l.top >= edge).ToList();

                if (mode == "Square" && !right)
                    t.Check(beside.Count > 0 && beside.All(l => l.left >= edge - 0.5f) && under.Count > 0 && under.All(l => l.left == 0f),
                        "Square on the left indents the lines beside it and not the ones below");
                else if (mode == "Square")
                    t.Check(beside.Count > 0 && beside.All(l => l.left == 0f && l.width <= column - edge + p.fontSize * 0.35f * z),
                        $"Square on the right narrows the lines beside it, a trailing space allowed to hang: {string.Join(" ", lines.Select(l => $"{l.top}:{l.width}"))}");
                else if (mode == "TopAndBottom")
                    t.Check(lines[0].top >= edge - 0.5f, $"Top and bottom moves the first line below the picture: {lines[0].top}");
                else
                    t.Check(lines[0].top == 0f && lines.All(l => l.left == 0f), $"{mode} leaves the lines where they were");
            }

            Directory.Delete(dir, true);
        }

        [A_XSDActionDependency("TextInput.PictureFloatEdit", "Test")]
        private static IEnumerator<int> PictureFloatEdit(TestContext t)
        {
            string dir = PictureFolder();
            string picture = PicturePng(Path.Combine(dir, "pic.png"), 50, 40);
            DocumentEditorControl editor = ShowFixture(t, DocumentXml.Parse(new XElement("Document",
                new XElement("Block", RunX("ab"), PictureRun(picture, ("Width", "100"))),
                Block(string.Join(" ", Enumerable.Repeat("lorem ipsum", 40))))));
            DocumentControl content = Content(editor);
            yield return 2;

            BlockControl p = Paragraphs(editor)[0];
            content.SetCaret(p, 0);
            content.PicturePressed(p, 2, PointerEvent.rightButton);
            t.Check(content.HasSelection && content.caretOffset == 3, "a right press on a picture selects it");

            editor.SetPictureWrap(PictureWrap.Square);
            yield return 2;
            StyleSpan span = Paragraphs(editor)[0].spans.First(s => s.IsPicture);
            t.Check(span.wrap == PictureWrap.Square && span.imageX > 0f && span.imageY >= 0f,
                $"Square floats the picture where it stood inline: {span.imageX}, {span.imageY}");
            t.Check(Handles(content).Count(h => h.arrangedRect.width > 0f) == 8, "the floating picture keeps its handles");

            yield return t.Key(Keys.Z, Keys.LeftControl);
            span = Paragraphs(editor)[0].spans.First(s => s.IsPicture);
            t.Check(span.wrap == PictureWrap.Inline && span.imageX == 0f, "undo puts it back in line");

            foreach (string extension in new[] { ".xml", ".md" })
            {
                RichTextDocument fixture = DocumentXml.Parse(new XElement("Document", new XElement("Block",
                    RunX("a"), PictureRun(picture, ("Width", "100"), ("Wrap", "Square"), ("X", "10"), ("Y", "20")))));
                string path = Path.Combine(dir, "note" + extension);
                fixture.Save(path);
                DestroyBlocks(fixture);

                string written = File.ReadAllText(path);
                t.Check(extension == ".xml" || written.Contains("data-wrap=\"square\" data-x=\"10\" data-y=\"20\""),
                    $"Markdown writes a floating picture as <img>: {written}");

                RichTextDocument back = RichTextDocument.Load(path);
                StyleSpan read = back.blocks.OfType<BlockControl>().First().spans.First(s => s.IsPicture);
                t.Check(read.wrap == PictureWrap.Square && read.imageX == 10f && read.imageY == 20f && read.imageWidth == 100f,
                    $"{extension} reads the wrap and offset back");
                DestroyBlocks(back);
            }

            Directory.Delete(dir, true);
        }

        [A_XSDActionDependency("TextInput.PictureWrapDraws", "Test")]
        private static IEnumerator<int> PictureWrapDraws(TestContext t)
        {
            string dir = PictureFolder();
            string words = string.Join(" ", Enumerable.Repeat("lorem ipsum dolor sit", 24));
            (string mode, Rgba32 colour, string x)[] modes =
            {
                ("Square", new Rgba32(200, 80, 40), "0"), ("TopAndBottom", new Rgba32(40, 140, 200), "120"),
                ("Behind", new Rgba32(240, 200, 60), "60"), ("InFront", new Rgba32(60, 160, 80), "200")
            };

            foreach (string mode in new[] { "Pageless", "Paged" })
            {
                XElement document = new XElement("Document",
                    new XElement("DocumentLayout", new XElement("Page", new XAttribute("Mode", mode))));
                foreach ((string wrap, Rgba32 colour, string x) in modes)
                {
                    string path = Path.Combine(dir, wrap + ".png");
                    using (Image<Rgba32> image = new Image<Rgba32>(80, 60, colour)) image.SaveAsPng(path);
                    document.Add(new XElement("Block", PictureRun(path, ("Wrap", wrap), ("X", x)), RunX(words)));
                }
                document.Add(new XElement("Block", RunX("inline "), PictureRun(Path.Combine(dir, "Square.png"), ("Width", "40")), RunX(" after")));

                DocumentEditorControl editor = ShowFixture(t, DocumentXml.Parse(document));
                yield return 2;
                t.Check(Content(editor).page.mode.ToString() == mode, $"the note is {mode}");
                if (mode == "Paged")
                {
                    BlockControl last = Paragraphs(editor)[^1];
                    Content(editor).SetCaret(last, last.Length);
                    yield return t.Key(Keys.Left);
                    yield return t.Key(Keys.Right);
                    yield return 2;
                }
                yield return t.Golden(mode);
            }

            Directory.Delete(dir, true);
        }

        [A_XSDActionDependency("TextInput.PictureTightLayout", "Test")]
        private static IEnumerator<int> PictureTightLayout(TestContext t)
        {
            string dir = PictureFolder();
            string path = Path.Combine(dir, "half.png");
            using (Image<Rgba32> image = new Image<Rgba32>(100, 100))
            {
                image.ProcessPixelRows(rows =>
                {
                    for (int y = 0; y < rows.Height; y++)
                    {
                        Span<Rgba32> row = rows.GetRowSpan(y);
                        for (int x = 0; x < 50; x++) row[x] = new Rgba32(40, 140, 200);
                    }
                });
                image.SaveAsPng(path);
            }

            DocumentEditorControl editor = ShowFixture(t, DocumentXml.Parse(new XElement("Document", new XElement("Block",
                PictureRun(path, ("Width", "100"), ("Height", "100"), ("Wrap", "Tight")), RunX(string.Join(" ", Enumerable.Repeat("lorem ipsum dolor", 60)))))));
            yield return 2;

            float z = Content(editor).zoom;
            List<TextLine> beside = Paragraphs(editor)[0].Lines.Where(l => l.top < 100f * z).ToList();
            t.Check(beside.Count > 0 && beside.All(l => l.left >= 58f * z - 0.5f && l.left < 108f * z - 0.5f),
                $"Tight wraps the opaque half, not the box: {string.Join(" ", beside.Select(l => l.left))}");

            Directory.Delete(dir, true);
        }

        [A_XSDActionDependency("TextInput.PictureFloatMove", "Test")]
        private static IEnumerator<int> PictureFloatMove(TestContext t)
        {
            string dir = PictureFolder();
            string picture = PicturePng(Path.Combine(dir, "pic.png"), 100, 100);
            string words = string.Join(" ", Enumerable.Repeat("lorem ipsum dolor", 40));
            DocumentEditorControl editor = ShowFixture(t, DocumentXml.Parse(new XElement("Document",
                Block(words),
                new XElement("Block", PictureRun(picture, ("Width", "100"), ("Height", "100"), ("Wrap", "Square")), RunX(words)))));
            DocumentControl content = Content(editor);
            yield return 2;

            float z = content.zoom;
            DocumentControl.FloatingPicture view = content.children.OfType<DocumentControl.FloatingPicture>().First(v => v.arrangedRect.width > 0f);
            yield return t.Click(view);
            t.Check(content.HasSelection && content.caretBlock == Paragraphs(editor)[1], "a click selects the floating picture");

            yield return t.Drag(view, Centre(view) + new Vector2(40f * z, 30f * z));
            StyleSpan span = Paragraphs(editor)[1].spans.First(s => s.IsPicture);
            t.Check(span.imageX == 40f && span.imageY == 30f, $"a drag moves it within its paragraph: {span.imageX}, {span.imageY}");

            List<BlockControl> p = Paragraphs(editor);
            float rise = view.arrangedRect.y - p[1].arrangedRect.y + 60f * z;
            yield return t.Drag(view, Centre(view) - new Vector2(0f, rise));
            p = Paragraphs(editor);
            t.Check(p[0].spans.Count(s => s.IsPicture) == 1 && p[0].text.StartsWith(BlockControl.PictureChar) && !p[1].spans.Any(s => s.IsPicture),
                "dropped beside the paragraph above, the picture is re-anchored to its start");
            StyleSpan moved = p[0].spans.FirstOrDefault(s => s.IsPicture);
            t.Check(moved.imageY >= 0f && moved.imageX == 40f, $"with its offset taken from the new paragraph: {moved.imageX}, {moved.imageY}");

            yield return t.Key(Keys.Z, Keys.LeftControl);
            p = Paragraphs(editor);
            span = p[1].spans.FirstOrDefault(s => s.IsPicture);
            t.Check(!p[0].spans.Any(s => s.IsPicture) && span.imageX == 40f && span.imageY == 30f, "undo puts it back in its paragraph as one step");
            yield return t.Key(Keys.Z, Keys.LeftControl);
            span = Paragraphs(editor)[1].spans.First(s => s.IsPicture);
            t.Check(span.imageX == 0f && span.imageY == 0f, "and the first move back too");

            Directory.Delete(dir, true);
        }

        [A_XSDActionDependency("TextInput.PictureTightDraws", "Test")]
        private static IEnumerator<int> PictureTightDraws(TestContext t)
        {
            string dir = PictureFolder();
            string path = Path.Combine(dir, "circle.png");
            using (Image<Rgba32> image = new Image<Rgba32>(120, 120))
            {
                image.ProcessPixelRows(rows =>
                {
                    for (int y = 0; y < rows.Height; y++)
                    {
                        Span<Rgba32> row = rows.GetRowSpan(y);
                        for (int x = 0; x < row.Length; x++)
                            if ((x - 59.5f) * (x - 59.5f) + (y - 59.5f) * (y - 59.5f) <= 60f * 60f) row[x] = new Rgba32(200, 80, 40);
                    }
                });
                image.SaveAsPng(path);
            }

            ShowFixture(t, DocumentXml.Parse(new XElement("Document", new XElement("Block",
                PictureRun(path, ("Wrap", "Tight"), ("X", "200")), RunX(string.Join(" ", Enumerable.Repeat("lorem ipsum dolor sit", 30)))))));
            yield return 2;
            yield return t.Golden("Circle");

            Directory.Delete(dir, true);
        }

        [A_XSDActionDependency("TextInput.PictureRotateRoundTrip", "Test")]
        private static IEnumerator<int> PictureRotateRoundTrip(TestContext t)
        {
            string dir = PictureFolder();
            string picture = PicturePng(Path.Combine(dir, "pic.png"), 30, 20);

            foreach (string extension in new[] { ".xml", ".md" })
            {
                RichTextDocument fixture = DocumentXml.Parse(new XElement("Document",
                    new XElement("Block", RunX("a"), PictureRun(picture, ("Width", "120"), ("Rotation", "30"))),
                    new XElement("Block", PictureRun(picture, ("Width", "60"), ("Wrap", "Square"), ("X", "10"), ("Y", "5"),
                        ("Rotation", "45"), ("Collision", "Shape")), RunX("b"))));
                string path = Path.Combine(dir, "note" + extension);
                fixture.Save(path);
                DestroyBlocks(fixture);

                string written = File.ReadAllText(path);
                if (extension == ".md")
                    t.Check(written.Contains("<img src=\"pic.png\" width=\"120\" data-rotate=\"30\">")
                            && written.Contains("data-collision=\"shape\" data-rotate=\"45\""),
                        $".md writes a turned picture as <img> with its turn and collision: {written}");

                RichTextDocument back = RichTextDocument.Load(path);
                List<StyleSpan> spans = back.blocks.OfType<BlockControl>().SelectMany(b => b.spans.Where(s => s.IsPicture)).ToList();
                t.Check(spans.Count == 2 && spans[0] is { wrap: PictureWrap.Inline, imageRotation: 30f, imageWidth: 120f }
                        && spans[1] is { wrap: PictureWrap.Square, imageRotation: 45f, collision: PictureCollision.Shape, imageX: 10f, imageY: 5f },
                    $"{extension} reads back the turn, the wrap and the collision");
                DestroyBlocks(back);
            }

            Directory.Delete(dir, true);
            yield break;
        }

        [A_XSDActionDependency("TextInput.PictureRotateInline", "Test")]
        private static IEnumerator<int> PictureRotateInline(TestContext t)
        {
            string dir = PictureFolder();
            string picture = PicturePng(Path.Combine(dir, "pic.png"), 100, 40);
            DocumentEditorControl editor = ShowFixture(t, DocumentXml.Parse(new XElement("Document",
                new XElement("Block", RunX("ab "), PictureRun(picture, ("Width", "100"), ("Height", "40"), ("Rotation", "90")), RunX(" cd")))));
            yield return 2;

            BlockControl p = Paragraphs(editor)[0];
            float z = Content(editor).zoom;
            p.PictureBox(3, out LayoutRect box);
            t.Check(MathF.Abs(box.width - 40f * z) < 0.5f && MathF.Abs(box.height - 100f * z) < 0.5f,
                $"a quarter-turned inline picture takes its turned box in the line: {box.width} x {box.height}");
            p.PictureFrame(3, out LayoutRect rect, out _);
            t.Check(MathF.Abs(rect.width - 100f * z) < 0.5f && MathF.Abs(rect.height - 40f * z) < 0.5f,
                $"and keeps its own size for the frame: {rect.width} x {rect.height}");

            Content(editor).SetCaret(p, 3);
            Content(editor).SetCaret(p, 4, true);
            yield return 2;
            yield return t.Golden("Selected", editor);

            Directory.Delete(dir, true);
        }

        [A_XSDActionDependency("TextInput.PictureRotateLayout", "Test")]
        private static IEnumerator<int> PictureRotateLayout(TestContext t)
        {
            string dir = PictureFolder();
            string picture = PicturePng(Path.Combine(dir, "square.png"), 100, 100);
            string words = string.Join(" ", Enumerable.Repeat("lorem ipsum dolor", 60));
            float[] firstLeft = new float[2];

            for (int i = 0; i < 2; i++)
            {
                string collision = i == 0 ? "Box" : "Shape";
                DocumentEditorControl editor = ShowFixture(t, DocumentXml.Parse(new XElement("Document", new XElement("Block",
                    PictureRun(picture, ("Width", "100"), ("Height", "100"), ("Wrap", "Square"), ("Rotation", "45"), ("Collision", collision)),
                    RunX(words)))));
                yield return 2;

                float z = Content(editor).zoom;
                IReadOnlyList<TextLine> lines = Paragraphs(editor)[0].Lines;
                firstLeft[i] = lines[0].left / z;
                if (i == 0)
                    t.Check(lines.Where(l => l.top < 100f * z).All(l => l.left >= 128.5f * z - 0.5f),
                        $"Box wraps the 45-degree picture's bounding box: {string.Join(" ", lines.Select(l => l.left))}");
            }
            t.Check(firstLeft[1] < firstLeft[0] - 10f && firstLeft[1] > 70f,
                $"Shape lets the first line in closer, beside the diamond's tip: {firstLeft[1]} vs {firstLeft[0]}");

            string path = Path.Combine(dir, "half.png");
            using (Image<Rgba32> image = new Image<Rgba32>(100, 100))
            {
                image.ProcessPixelRows(rows =>
                {
                    for (int y = 0; y < rows.Height; y++)
                    {
                        Span<Rgba32> row = rows.GetRowSpan(y);
                        for (int x = 0; x < 50; x++) row[x] = new Rgba32(40, 140, 200);
                    }
                });
                image.SaveAsPng(path);
            }

            DocumentEditorControl tight = ShowFixture(t, DocumentXml.Parse(new XElement("Document", new XElement("Block",
                PictureRun(path, ("Width", "100"), ("Height", "100"), ("Wrap", "Tight"), ("Rotation", "90")), RunX(words)))));
            yield return 2;

            float tz = Content(tight).zoom;
            IReadOnlyList<TextLine> tl = Paragraphs(tight)[0].Lines;
            List<TextLine> top = tl.Where(l => l.top + l.height < 50f * tz).ToList();
            List<TextLine> lower = tl.Where(l => l.top > 52f * tz && l.top < 100f * tz).ToList();
            t.Check(top.Count > 0 && top.All(l => l.left >= 108f * tz - 0.5f) && lower.Count > 0 && lower.All(l => l.left == 0f),
                $"Tight turned a quarter wraps the opaque half now on top: {string.Join(" ", tl.Select(l => $"{l.top}:{l.left}"))}");

            yield return t.Golden("Tight");
            Directory.Delete(dir, true);
        }

        [A_XSDActionDependency("TextInput.PictureRotate", "Test")]
        private static IEnumerator<int> PictureRotate(TestContext t)
        {
            string dir = PictureFolder();
            string picture = PicturePng(Path.Combine(dir, "pic.png"), 100, 100);
            string words = string.Join(" ", Enumerable.Repeat("lorem ipsum dolor", 40));
            DocumentEditorControl editor = ShowFixture(t, DocumentXml.Parse(new XElement("Document",
                Block(words),
                new XElement("Block", PictureRun(picture, ("Width", "100"), ("Height", "100"), ("Wrap", "Square"), ("X", "100")), RunX(words)))));
            DocumentControl content = Content(editor);
            yield return 2;

            DocumentControl.FloatingPicture view = content.children.OfType<DocumentControl.FloatingPicture>().First(v => v.arrangedRect.width > 0f);
            yield return t.Click(view);
            DocumentControl.PictureRotator ring = content.children.OfType<DocumentControl.PictureRotator>().First();
            t.Check(ring.arrangedRect.width > 0f, "a selected picture shows the rotate ring");

            Vector2 c = Centre(ring);
            float r = ring.arrangedRect.width * 0.5f - 1f;
            yield return t.Drag(ring, c - new Vector2(0f, r), c + new Vector2(r, 0f));
            t.Check(PictureSpan(editor).imageRotation == 90f, $"dragging the ring a quarter clockwise turns it 90: {PictureSpan(editor).imageRotation}");

            c = Centre(ring);
            r = ring.arrangedRect.width * 0.5f - 1f;
            float a = 50f * MathF.PI / 180f;
            yield return t.Drag(ring, c - new Vector2(0f, r), c + new Vector2(MathF.Sin(a), -MathF.Cos(a)) * r, 8, Keys.LeftShift);
            t.Check(PictureSpan(editor).imageRotation == 135f, $"Shift snaps the turn to 15 degrees: {PictureSpan(editor).imageRotation}");
            t.Check(Paragraphs(editor)[0].spans.Any(s => s.IsPicture),
                "turned so its box rises above its paragraph, the picture is re-anchored to the one above");

            editor.SetPictureWrap(PictureWrap.TopAndBottom);
            yield return 2;
            t.Check(PictureSpan(editor) is { wrap: PictureWrap.TopAndBottom, imageRotation: 135f }, "a wrap change keeps the turn");

            yield return t.Key(Keys.Z, Keys.LeftControl);
            yield return t.Key(Keys.Z, Keys.LeftControl);
            t.Check(PictureSpan(editor).imageRotation == 90f && Paragraphs(editor)[1].spans.Any(s => s.IsPicture),
                "undo takes back the snapped turn and its re-anchoring as one step");
            yield return t.Key(Keys.Z, Keys.LeftControl);
            t.Check(PictureSpan(editor).imageRotation == 0f, "and then the first turn");

            Directory.Delete(dir, true);
        }

        private static StyleSpan PictureSpan(DocumentEditorControl editor) =>
            Paragraphs(editor).SelectMany(p => p.spans).First(s => s.IsPicture && s.count > 0);

        [A_XSDActionDependency("TextInput.PictureRotateDraws", "Test")]
        private static IEnumerator<int> PictureRotateDraws(TestContext t)
        {
            string dir = PictureFolder();
            string picture = PicturePng(Path.Combine(dir, "pic.png"), 120, 60);
            string words = string.Join(" ", Enumerable.Repeat("lorem ipsum dolor sit", 30));

            foreach (string collision in new[] { "Box", "Shape" })
            {
                ShowFixture(t, DocumentXml.Parse(new XElement("Document", new XElement("Block",
                    PictureRun(picture, ("Wrap", "Square"), ("X", "200"), ("Y", "20"), ("Rotation", "30"), ("Collision", collision)), RunX(words)))));
                yield return 2;
                yield return t.Golden(collision);
            }

            Directory.Delete(dir, true);
        }

        private static List<DocumentControl.PictureHandle> Handles(DocumentControl content) =>
            content.children.OfType<DocumentControl.PictureHandle>().ToList();

        private static Vector2 Centre(Control control) =>
            new Vector2(control.arrangedRect.x + control.arrangedRect.width * 0.5f, control.arrangedRect.y + control.arrangedRect.height * 0.5f);

        private static Vector2 Stored(BlockControl block)
        {
            StyleSpan span = block.spans.First(s => s.IsPicture);
            return new Vector2(span.imageWidth, span.imageHeight);
        }

        private static XElement PictureRun(string source, params (string name, string value)[] attributes)
        {
            XElement run = new XElement("Run", new XAttribute("Image", source));
            foreach ((string name, string value) in attributes)
                run.SetAttributeValue(name, value);
            return run;
        }

        private static string PictureFolder()
        {
            string dir = Path.Combine(Path.GetTempPath(), $"aurora-pictures-{Guid.NewGuid():N}");
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static string PicturePng(string path, int width, int height)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using Image<Rgba32> image = new Image<Rgba32>(width, height, new Rgba32(200, 80, 40));
            image.SaveAsPng(path);
            return path;
        }

        private static int PictureCount(BlockControl block) => block.spans.Count(s => s.IsPicture && s.count > 0);

        private static void DestroyBlocks(RichTextDocument document)
        {
            foreach (Control entry in document.blocks)
                entry.Destroy();
        }
        #endregion
    }
}
