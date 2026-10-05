using ArctisAurora.Core.Diagnostics;
using ArctisAurora.Core.Filing;
using ArctisAurora.Core.Filing.Serialization;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Testing;
using ArctisAurora.Core.Tex;
using ArctisAurora.Core.UI;
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
    }
}
