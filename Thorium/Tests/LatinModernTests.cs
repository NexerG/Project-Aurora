using ArctisAurora.Core.Diagnostics;
using ArctisAurora.Core.Filing;
using ArctisAurora.Core.Filing.Serialization;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Testing;
using ArctisAurora.Core.Tex;
using ArctisAurora.Core.UI;
using ArctisAurora.EngineWork;
using System.Numerics;
using System.Xml.Linq;

namespace Thorium.Tests
{
    internal static class LatinModernTests
    {
        private static readonly LogChannel Log = LogChannel.For("Test");

        private static AtlasMetaData Load(string name)
        {
            AtlasMetaData atlas = new AtlasMetaData();
            Serializer.DeserializeAttributed(Paths.Font(name, name + ".agd"), ref atlas);
            atlas.BuildCharIndex();
            return atlas;
        }

        private static GlyphMetrics Of(AtlasMetaData atlas, char c) => atlas.GetGlyphAndIndex(c).Item1.regular;

        private static (int, int, int, int, float) Key(GlyphMetrics m) => (m.xMin, m.yMin, m.xMax, m.yMax, m.advanceWidth);

        [A_XSDActionDependency("LatinModern.Baked", "Test")]
        private static IEnumerator<int> Baked(TestContext t)
        {
            AtlasMetaData roman = Load("lmroman10-regular");
            char[] distinct = { 'a', 'g', 'ﬁ', '—', 'é' };
            t.Check(distinct.Select(c => Key(Of(roman, c))).Distinct().Count() == distinct.Length,
                "a, g, fi, em dash and e-acute bake distinct glyphs");

            float a = Of(roman, 'a').advanceWidth, m = Of(roman, 'm').advanceWidth;
            t.Check(MathF.Abs(a - 0.5f) < 0.01f && MathF.Abs(m - 0.833f) < 0.01f, $"advances follow cmr10: a {a}, m {m}");

            Glyph x = roman.GetGlyphAndIndex('x').Item1;
            t.Check(roman.hasItalic && roman.hasBold && roman.hasBoldItalic && Key(x.regular) != Key(x.italic),
                "the Roman family has its italic, bold and bold-italic faces");

            AtlasMetaData mono = Load("lmmono10-regular");
            t.Check(Of(mono, 'i').advanceWidth == Of(mono, 'm').advanceWidth, "the mono face is monospaced");

            AtlasMetaData math = Load("latinmodern-math");
            MathConstants constants = MathConstants.Load(Paths.Font("latinmodern-math", "latinmodern-math.math.xml"));
            t.Check(constants.axisHeight > 0f && constants.fractionRuleThickness > 0f, "LM Math constants are read");

            // A character the font lacks bakes .notdef, so several share one box.
            List<char> missing = math.chars.GroupBy(c => Key(Of(math, c))).Where(g => g.Count() > 2).SelectMany(g => g).ToList();
            Log.Info($"LM Math characters sharing one glyph box (likely missing): {new string(missing.ToArray())}");
            t.Check(!"∑∫√α⎛⎝".Any(missing.Contains), "sum, integral, root, alpha and paren pieces are in LM Math");
            yield break;
        }

        [A_XSDActionDependency("LatinModern.Draws", "Test")]
        private static IEnumerator<int> Draws(TestContext t)
        {
            StackPanelControl panel = new StackPanelControl
            {
                preferredWidth = 640f,
                preferredHeight = 180f,
                colorHex = "#FFFFFF",
                horizontalAlignment = HorizontalAlignment.Left,
                verticalAlignment = VerticalAlignment.Top
            };
            panel.AddChild(new LabelControl { text = "Fine ffi — “LaTeX” é 0123 Wq", fontName = "latin-modern", fontSize = 28, colorHex = "#000000" });
            panel.AddChild(new LabelControl { text = "\\begin{document} 0O1lI", fontName = "latin-modern-mono", fontSize = 28, colorHex = "#000000" });
            panel.AddChild(new LabelControl { text = "∑∫√ αβγ ≤≠∞ ⎛⎝", fontName = "latin-modern-math", fontSize = 28, lineHeight = 3f, colorHex = "#000000" });
            t.Show(panel);
            yield return 2;

            yield return t.Golden("Lines", panel);
        }

        [A_XSDActionDependency("Tex.Preview", "Test")]
        private static IEnumerator<int> TexPreview(TestContext t)
        {
            int xHeight = new TexAtlasMetrics().XHeight(new TexFont(TexFamily.Roman, false, false, 10 * 65536));
            t.Check(xHeight > 4.2f * 65536 && xHeight < 4.45f * 65536, $"ex at 10pt is Latin Modern's x-height, about 4.31pt: {xHeight / 65536f}pt");

            const string source = """
                \documentclass{article}
                \title{A Sample Article}
                \author{Aurora}
                \date{5 October 2026}
                \begin{document}
                \maketitle
                \section{Introduction}
                The ``first'' paragraph---with ligatures: office, flow, and caf\'e, na\"ive.
                Inline math $e^{i\pi} + 1 = 0$ and a note.\footnote{An endnote.}
                \[ \sum_{k=1}^{n} k = \frac{n(n+1)}{2} \]
                \subsection{Lists}
                \begin{itemize}
                \item \textbf{Bold} and \emph{emphasis}
                \begin{enumerate}
                \item First
                \item Second
                \end{enumerate}
                \end{itemize}
                \begin{verbatim}
                int x = 1;
                \end{verbatim}
                \end{document}
                """;
            XElement document = TexLowering.Compile(source, out List<TexError> errors);
            t.Check(errors.Count == 0, $"the sample compiles clean: {string.Join("; ", errors.Select(e => $"{e.line}: {e.message}"))}");

            string path = Path.Combine(Path.GetTempPath(), $"aurora-tex-{Guid.NewGuid():N}.xml");
            document.Save(path);
            DocumentEditorControl editor = new DocumentEditorControl
            {
                horizontalAlignment = HorizontalAlignment.Stretch,
                verticalAlignment = VerticalAlignment.Stretch
            };
            t.Show(editor);
            editor.LoadPath(path);
            File.Delete(path);
            yield return 2;
            yield return t.Golden("Page", editor);
        }

        [A_XSDActionDependency("Tex.SplitView.Recompile", "Test")]
        private static IEnumerator<int> SplitViewRecompile(TestContext t)
        {
            string body = string.Concat(Enumerable.Range(1, 60).Select(i => $"Paragraph {i} of the sample.\n\n"));
            TexEditorControl editor = ShowSplit(t, "\\documentclass{article}\n\\begin{document}\n" + body + "\\end{document}\n");
            yield return 2;

            RichTextDocument first = editor.preview.activeDocument;
            t.Check(first != null && first.blocks.Count == 60, $"the preview is typeset on open: {first?.blocks.Count}");
            editor.preview.SetScrollOffset(new Vector2(0f, 200f));
            yield return 1;
            float scrolled = editor.preview.GetScrollOffset().Y;

            editor.source.GoTo(2, "Paragraph 1".Length);
            editor.source.FocusCaret();
            yield return t.Type("Z");
            t.Check(ReferenceEquals(editor.preview.activeDocument, first), "no recompile while typing");

            double until = Engine.totalTime + TexEditorControl.recompileDelay + 0.1;
            for (int i = 0; i < 2000 && Engine.totalTime < until; i++) yield return 1;
            yield return 1;

            RichTextDocument second = editor.preview.activeDocument;
            string text = ((BlockControl)second.blocks[0]).text;
            t.Check(!ReferenceEquals(second, first) && text.StartsWith("Paragraph 1Z"), $"recompiled after the pause: {text}");
            t.Check(scrolled > 0f && Math.Abs(editor.preview.GetScrollOffset().Y - scrolled) < 0.5f,
                $"the preview keeps its scroll: {scrolled} → {editor.preview.GetScrollOffset().Y}");

            t.Show(new StackPanelControl());
        }

        [A_XSDActionDependency("Tex.SplitView.Errors", "Test")]
        private static IEnumerator<int> SplitViewErrors(TestContext t)
        {
            TexEditorControl editor = ShowSplit(t,
                "\\documentclass{article}\n\\begin{document}\n\\section{Errors}\nText with \\nosuch{} in it.\n\\end{document}\n");
            yield return 2;

            t.Check(!editor.errors.hidden, "the error list shows");
            List<ButtonControl> rows = editor.errors.children.OfType<StackPanelControl>().SelectMany(p => p.children.OfType<ButtonControl>()).ToList();
            t.Check(rows.Count == 1, $"one error row: {rows.Count}");
            yield return t.Golden("Split", editor);

            if (rows.Count > 0) yield return t.Click(rows[0]);
            yield return 1;
            DocumentControl content = Content(editor.source);
            int index = editor.source.session.document.blocks.IndexOf(content.caretBlock);
            t.Check(index == 3 && content.caretOffset == "Text with ".Length,
                $"the row puts the source caret at the error: block {index}, offset {content.caretOffset}");

            t.Show(new StackPanelControl());
        }

        [A_XSDActionDependency("Tex.SplitView.ClickToSource", "Test")]
        private static IEnumerator<int> SplitViewClickToSource(TestContext t)
        {
            TexEditorControl editor = ShowSplit(t,
                "\\documentclass{article}\n\\begin{document}\nFirst paragraph.\n\nSecond\nparagraph.\n\\end{document}\n");
            yield return 30;

            BlockControl second = (BlockControl)editor.preview.activeDocument.blocks[1];
            LayoutRect r = second.arrangedRect;
            Vector2 at = new Vector2(r.x + 4f, r.y + r.height * 0.5f);
            yield return t.Click(second, at);
            yield return 1;
            DocumentControl content = Content(editor.source);
            int index = editor.source.session.document.blocks.IndexOf(content.caretBlock);
            t.Check(index != 4, $"a single click leaves the source caret: block {index}");

            yield return t.Click(second, at);
            yield return t.Click(second, at);
            yield return 1;
            index = editor.source.session.document.blocks.IndexOf(content.caretBlock);
            t.Check(index == 4 && content.caretOffset == 0, $"a double click on the second paragraph goes to its source line: block {index}, offset {content.caretOffset}");

            t.Show(new StackPanelControl());
        }

        // A .tex opened in the split view from a temporary file.
        private static TexEditorControl ShowSplit(TestContext t, string source)
        {
            string path = Path.Combine(Path.GetTempPath(), $"aurora-split-{Guid.NewGuid():N}.tex");
            File.WriteAllText(path, source);
            TexEditorControl editor = new TexEditorControl();
            t.Show(editor);
            editor.LoadPath(path);
            File.Delete(path);
            return editor;
        }

        private static DocumentControl Content(DocumentEditorControl editor) =>
            (DocumentControl)((BlockControl)editor.session.document.blocks[0]).parent;
    }
}
