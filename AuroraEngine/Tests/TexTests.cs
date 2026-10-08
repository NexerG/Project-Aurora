using ArctisAurora.Core.Filing;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Registry.Assets;
using ArctisAurora.Core.Testing;
using ArctisAurora.Core.Tex;
using ArctisAurora.Core.UI;
using ArctisAurora.EngineWork.Registry;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ArctisAurora.Tests
{
    internal static class TexTests
    {
        // What the expander hands on: characters as themselves, a control sequence as "\name ".
        private static string Run(string source, out List<TexError> errors)
        {
            TexExpander expander = new TexExpander(source);
            StringBuilder text = new StringBuilder();
            while (expander.Next(out TexToken t))
                text.Append(t.IsCs ? "\\" + t.name + " " : t.ch.ToString());
            errors = expander.errors;
            return text.ToString();
        }

        private static string Run(string source) => Run(source, out _);

        private static bool Has(List<TexError> errors, string text, int line = 0) =>
            errors.Any(e => e.message.Contains(text) && (line == 0 || e.line == line));

        [A_XSDActionDependency("Tex.SourceRoundTrip", "Test")]
        private static IEnumerator<int> SourceRoundTrip(TestContext t)
        {
            foreach (string source in new[] { "\uFEFF\\section{A}\r\n% c\r\n", "\\section{A}\nx\n", "a\nb", "" })
            {
                byte[] bytes = Encoding.UTF8.GetBytes(source);
                string from = Path.Combine(Path.GetTempPath(), $"aurora-tex-{Guid.NewGuid():N}.tex");
                string to = Path.Combine(Path.GetTempPath(), $"aurora-tex-{Guid.NewGuid():N}.tex");
                File.WriteAllBytes(from, bytes);

                RichTextDocument document = RichTextDocument.Load(from);
                bool latex = document.blocks.OfType<NoteBlock>().All(b => b.stylingType == TextStyleType.Code && b.language == "latex");
                document.Save(to);
                byte[] back = File.ReadAllBytes(to);
                File.Delete(from);
                File.Delete(to);

                string shown = source.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\uFEFF", "BOM");
                t.Check(back.SequenceEqual(bytes), $"'{shown}' saves back byte for byte");
                t.Check(latex, $"'{shown}' loads as LaTeX code lines");
            }
            yield break;
        }

        [A_XSDActionDependency("Tex.Catcodes", "Test")]
        private static IEnumerator<int> Catcodes(TestContext t)
        {
            t.Check(Run(@"\def\x{X}\x  a%") == "Xa", "a control word swallows the spaces after it");
            t.Check(Run("a   b%") == "a b", "a run of spaces reads as one");
            t.Check(Run("a%comment\nb%") == "ab", "a comment takes the rest of its line and the line end");
            t.Check(Run("a\n\nb%") == @"a \par b", "a line end is a space and a blank line is \\par");
            t.Check(Run(@"\makeatletter\def\a@b{Y}\a@b\makeatother%") == "Y", "\\makeatletter makes @ a letter");

            string after = Run(@"\makeatletter\def\a@b{Y}\makeatother\a@b%", out List<TexError> errors);
            t.Check(after == "@b" && errors.Count == 1 && Has(errors, @"Undefined control sequence \a"),
                $"after \\makeatother, @ ends the name again ({after})");

            t.Check(Run(@"\catcode`\!=0 !def!x{Z}!x%") == "Z", "a character given category 0 starts control sequences");
            t.Check(Run(@"{\catcode`\!=0 }!x%") == "{}!x", "a category change ends with its group");
            yield break;
        }

        [A_XSDActionDependency("Tex.DelimitedDef", "Test")]
        private static IEnumerator<int> DelimitedDef(TestContext t)
        {
            const string def = @"\def\a#1.#2\stop{(#2,#1)}";
            t.Check(Run(def + @"\a x.{y.z}\stop%") == "(y.z,x)", "delimited arguments, and a whole-group argument loses its braces");
            t.Check(Run(def + @"\a {a.b}.c\stop%") == "(c,a.b)", "a delimiter inside braces does not end the argument");
            t.Check(Run(@"\def\b#1#2{[#2#1]}\b x {yz}%") == "[yzx]", "undelimited arguments skip spaces and take a group");
            t.Check(Run(@"\def\c.#1{<#1>}\c.q%") == "<q>", "a delimiter before the first parameter is matched");

            Run(@"\def\c.#1{<#1>}\c q%", out List<TexError> errors);
            t.Check(Has(errors, @"Use of \c doesn't match its definition"), "a call that misses the leading delimiter is an error");
            yield break;
        }

        [A_XSDActionDependency("Tex.ExpandAfter", "Test")]
        private static IEnumerator<int> ExpandAfter(TestContext t)
        {
            t.Check(Run(@"\def\a{A}\def\b#1{[#1]}\expandafter\b\a%") == "[A]", "\\expandafter expands the token after next first");
            t.Check(Run(@"\def\a{\b}\def\b{B}\def\w#1{(#1)}\expandafter\expandafter\expandafter\w\a%") == "(B)",
                "three \\expandafter reach two levels down");
            t.Check(Run(@"\expandafter\def\csname x y\endcsname{Z}\csname x y\endcsname%") == "Z", "\\csname builds a name with a space in it");
            t.Check(Run(@"\string\foo%") == @"\foo", "\\string gives the name as characters");
            yield break;
        }

        [A_XSDActionDependency("Tex.Edef", "Test")]
        private static IEnumerator<int> Edef(TestContext t)
        {
            t.Check(Run(@"\def\a{1}\edef\b{\a\noexpand\a}\def\a{2}\b%") == "12", "\\edef expands now, except after \\noexpand");
            t.Check(Run(@"\count1=7 \edef\c{\the\count1}\count1=9 \c%") == "7", "\\the inside \\edef takes the value at definition");
            yield break;
        }

        [A_XSDActionDependency("Tex.Groups", "Test")]
        private static IEnumerator<int> Groups(TestContext t)
        {
            t.Check(Run(@"\def\a{O}{\def\a{I}\a}\a%") == "{I}O", "a definition inside a group ends with it");
            t.Check(Run(@"{\gdef\a{G}}\a%") == "{}G", "\\gdef survives the group");
            t.Check(Run(@"\def\a{1}{\def\a{2}\gdef\a{3}}\a%") == "{}3", "a global change wins over the group's saved value");
            t.Check(Run(@"{\count1=5 }\the\count1%") == "{}0", "a register assignment is local");
            t.Check(Run(@"{\global\count1=5 }\the\count1%") == "{}5", "\\global makes it survive");

            string semi = Run(@"\begingroup\def\a{S}\endgroup\a%", out List<TexError> errors);
            t.Check(semi == "" && errors.Count == 1 && Has(errors, @"Undefined control sequence \a"), "\\begingroup scopes like a brace");
            yield break;
        }

        [A_XSDActionDependency("Tex.Conditionals", "Test")]
        private static IEnumerator<int> Conditionals(TestContext t)
        {
            t.Check(Run(@"\ifnum 3>2 Y\else N\fi%") == "Y", "\\ifnum compares and \\else skips its branch");
            t.Check(Run(@"\def\a{x}\def\b{x}\ifx\a\b S\else D\fi%") == "S", "\\ifx finds two macros with the same text equal");
            t.Check(Run(@"\ifdim 1in>72pt T\else F\fi%") == "T", "\\ifdim compares in sp: 1in is 72.27pt");
            t.Check(Run(@"\ifcase 2 a\or b\or c\else d\fi%") == "c", "\\ifcase takes the branch it counts to");
            t.Check(Run(@"\ifcase 5 a\or b\else d\fi%") == "d", "\\ifcase past its branches takes \\else");
            t.Check(Run(@"\iffalse \ifnum1=1 x\else y\fi\else z\fi%") == "z", "a skipped branch skips nested conditionals whole");
            t.Check(Run(@"\ifodd 3 O\fi%") == "O", "\\ifodd");
            t.Check(Run(@"\if aaE\fi\ifcat a1X\else Y\fi%") == "EY", "\\if compares characters, \\ifcat categories");
            t.Check(Run(@"\ifdefined\nothing Y\else N\fi\ifcsname relax\endcsname Y\fi%") == "NY", "\\ifdefined and \\ifcsname");
            t.Check(Run(@"\newif\ifdraft \drafttrue \ifdraft D\else F\fi%") == "D", "\\newif makes a switch and its setters");
            yield break;
        }

        [A_XSDActionDependency("Tex.Registers", "Test")]
        private static IEnumerator<int> Registers(TestContext t)
        {
            t.Check(Run(@"\count1=5 \advance\count1 by 7 \multiply\count1 2 \the\count1%") == "24", "count arithmetic");
            string inch = Run(@"\dimen0=1in \the\dimen0%");
            t.Check(inch == "72.26999pt", $"1in reads back as TeX prints it ({inch})");
            string cm = Run(@"\dimen0=2.5cm \the\dimen0%");
            t.Check(cm == "71.13188pt", $"2.5cm rounds as tex.web does ({cm})");
            t.Check(Run(@"\dimen0=-.5pt \advance\dimen0 by 2\dimen0 \the\dimen0%") == "-1.5pt", "a register as the unit of a dimension");
            t.Check(Run(@"\dimen0=1em \the\dimen0%") == "10.0pt", "em is the fixed 10pt font's");
            string skip = Run(@"\skip0=3pt plus 1fil minus 2pt \the\skip0%");
            t.Check(skip == "3.0pt plus 1.0fil minus 2.0pt", $"glue with fil stretch ({skip})");
            t.Check(Run(@"\countdef\c=3 \c=9 \the\count3%") == "9", "\\countdef names a register");
            t.Check(Run(@"\chardef\x=65 \x%") == "A", "\\chardef names a character");
            t.Check(Run(@"\toks0={ab}\the\toks0%") == "ab", "a token register");
            t.Check(Run(@"\newcounter{c}\setcounter{c}{4}\addtocounter{c}{3}\thec\ifnum\value{c}=7 Y\fi%") == "7Y", "LaTeX counters");
            yield break;
        }

        [A_XSDActionDependency("Tex.NewCommand", "Test")]
        private static IEnumerator<int> NewCommand(TestContext t)
        {
            t.Check(Run(@"\newcommand{\hi}[2][W]{#1-#2}\hi{x} \hi[y]{z}%") == "W-x y-z", "an optional first argument and its default");

            string twice = Run(@"\newcommand\hi{a}\newcommand\hi{b}\hi%", out List<TexError> errors);
            t.Check(twice == "a" && Has(errors, @"Command \hi already defined"), "\\newcommand refuses a defined name");
            t.Check(Run(@"\newcommand\hi{a}\renewcommand\hi{b}\hi%") == "b", "\\renewcommand replaces it");
            t.Check(Run(@"\providecommand\hi{a}\providecommand\hi{b}\hi%") == "a", "\\providecommand keeps the first");

            Run(@"\newcommand*\s[1]{#1}\s{a\par b}%", out errors);
            t.Check(Has(errors, @"Paragraph ended before \s was complete"), "a starred command's argument cannot hold \\par");

            t.Check(Run(@"\makeatletter\def\t{\@ifnextchar[{O}{N}}\t[ \t x%") == "O[ Nx", "\\@ifnextchar looks without taking");
            t.Check(Run(@"\makeatletter\def\s{\@ifstar{S}{P}}\s*\s.%") == "SP.", "\\@ifstar takes the star");
            yield break;
        }

        [A_XSDActionDependency("Tex.NewEnvironment", "Test")]
        private static IEnumerator<int> NewEnvironment(TestContext t)
        {
            t.Check(Run(@"\newenvironment{box}[1]{<#1|}{|>}\begin{box}{t}x\end{box}%") == "<t|x|>", "an environment's begin code takes its argument");

            string local = Run(@"\newenvironment{e}{\def\q{in}}{}\begin{e}\q\end{e}\q%", out List<TexError> errors);
            t.Check(local == "in" && errors.Count == 1 && Has(errors, @"Undefined control sequence \q"), "an environment is a group");

            Run(@"\newenvironment{a}{}{}\begin{a}\end{b}%", out errors);
            t.Check(Has(errors, @"\begin{a} on input line 1 ended by \end{b}"), "a mismatched \\end is an error");

            Run(@"\begin{zz}\end{zz}%", out errors);
            t.Check(errors.Count == 1 && Has(errors, "Environment zz undefined"), "an undefined environment is one error");
            yield break;
        }

        [A_XSDActionDependency("Tex.Errors", "Test")]
        private static IEnumerator<int> Errors(TestContext t)
        {
            Run("a\n\\nope b%", out List<TexError> errors);
            t.Check(Has(errors, @"Undefined control sequence \nope", 2), "an undefined command is reported on its line");

            Run("\\def\\a#1{#1}\n\\a{x\n\nb}%", out errors);
            t.Check(Has(errors, @"Paragraph ended before \a was complete", 3), "a runaway argument is reported where the paragraph ends");

            Run(@"\def\a{x%", out errors);
            t.Check(Has(errors, @"File ended while scanning definition of \a"), "a definition missing its brace");

            Run("a}%", out errors);
            t.Check(Has(errors, "Too many }'s"), "an extra closing brace");

            Run("{a%", out errors);
            t.Check(Has(errors, "group opened on line 1"), "a group left open at the end");

            Run(@"\def\a{\a}\a", out errors);
            t.Check(Has(errors, "Expansion limit"), "a macro calling itself stops at the expansion limit");
            yield break;
        }

        [A_XSDActionDependency("Tex.StepCounter", "Test")]
        private static IEnumerator<int> StepCounter(TestContext t)
        {
            const string counters = @"\newcounter{a}\newcounter{b}[a]\newcounter{c}[b]\setcounter{b}{5}\setcounter{c}{3}";
            t.Check(Run(counters + @"\stepcounter{a}\the\value{a},\the\value{b},\the\value{c}%") == "1,0,0",
                "\\stepcounter zeroes the counters within it, and theirs");
            t.Check(Run(counters + @"{\stepcounter{c}}\the\value{c}%") == "{}4", "\\stepcounter is global");

            Run(@"\newcounter{d}[zz]%", out List<TexError> errors);
            t.Check(Has(errors, "No counter 'zz' defined"), "[within] must name a counter");
            yield break;
        }

        [A_XSDActionDependency("Tex.EmFromFont", "Test")]
        private static IEnumerator<int> EmFromFont(TestContext t)
        {
            TexExpander expander = new TexExpander(@"\dimen0=2em \the\dimen0,\dimen1=1ex \the\dimen1%")
            {
                quad = () => 20 * 65536,
                xHeight = () => 8 * 65536
            };
            StringBuilder text = new StringBuilder();
            while (expander.Next(out TexToken token))
                text.Append(token.ch);
            t.Check(text.ToString() == "40.0pt,8.0pt", $"em and ex come from the font the caller names: {text}");

            TexTypesetter typesetter = Typeset(@"\Large\hskip1em\hskip1ex x", out _);
            List<TexGlueNode> glue = Pars(typesetter)[0].list.OfType<TexGlueNode>().ToList();
            int large = (int)Math.Round(14.4 * 65536);
            t.Check(glue[0].glue.width == large && glue[1].glue.width == (int)(large * 0.430554),
                "the typesetter's current size is the em and ex");
            yield break;
        }

        #region ---- typesetting ----
        private static TexTypesetter Typeset(string source, out List<TexError> errors)
        {
            TexTypesetter typesetter = new TexTypesetter(source);
            typesetter.Run();
            errors = typesetter.errors;
            return typesetter;
        }

        // The main list's paragraphs, a float's where it stands and after the paragraph it is in.
        private static List<TexParagraph> Pars(TexTypesetter typesetter) => Pars(typesetter.vlist).ToList();

        private static IEnumerable<TexParagraph> Pars(List<TexNode> vlist)
        {
            foreach (TexNode node in vlist)
                if (node is TexFloat f)
                    foreach (TexParagraph inner in Pars(f.vlist))
                        yield return inner;
                else if (node is TexParagraph p)
                {
                    yield return p;
                    foreach (TexFloat g in p.list.OfType<TexFloat>())
                        foreach (TexParagraph inner in Pars(g.vlist))
                            yield return inner;
                }
        }

        // A paragraph as text: glue a space, math in $ or $$, a forced break |.
        private static string Line(TexParagraph p) => string.Concat(p.list.Select(n => n switch
        {
            TexChar c => c.ch.ToString(),
            TexGlueNode => " ",
            TexMathNode { display: true } m => "$$" + m.source + "$$",
            TexMathNode m => "$" + m.source + "$",
            TexPenalty { penalty: TexPenalty.Forced } => "|",
            TexRefNode r => r.text,
            _ => ""
        }));

        private static string Lines(string source, out List<TexError> errors) =>
            string.Join(" / ", Pars(Typeset(source, out errors)).Select(Line));

        private static string Lines(string source) => Lines(source, out _);

        private static List<TexChar> Chars(string source) => Pars(Typeset(source, out _)).SelectMany(p => p.list).OfType<TexChar>().ToList();

        [A_XSDActionDependency("Tex.Typeset.Paragraphs", "Test")]
        private static IEnumerator<int> TypesetParagraphs(TestContext t)
        {
            string two = Lines("Hello   world.\n\nSecond  para\nline.", out List<TexError> errors);
            t.Check(two == "Hello world. / Second para line." && errors.Count == 0, $"a blank line ends a paragraph and spaces collapse: {two}");
            t.Check(Lines("a~b") == "a\u00A0b", "~ is a no-break space");
            t.Check(Lines(@"a \par") == "a", "a paragraph's last space is dropped");
            t.Check(Lines(@"a\\ b") == "a|b", "\\\\ is a forced break");
            t.Check(Lines(@"\begin{document}a\end{document}b") == "a", "nothing after \\end{document} is read");

            Lines("\\documentclass{article}\nx", out errors);
            t.Check(Has(errors, @"Missing \begin{document}"), "text in the preamble is an error");
            yield break;
        }

        [A_XSDActionDependency("Tex.Typeset.Fonts", "Test")]
        private static IEnumerator<int> TypesetFonts(TestContext t)
        {
            List<TexChar> abc = Chars(@"a{\bfseries b}c");
            t.Check(!abc[0].style.font.bold && abc[1].style.font.bold && !abc[2].style.font.bold, "a font change ends with its group");
            t.Check(Chars(@"\textit{x}")[0].style.font.italic && !Chars(@"\emph{\emph{y}}")[0].style.font.italic, "\\emph inside \\emph is upright");
            t.Check(Chars(@"\texttt{z}")[0].style.font.family == TexFamily.Mono, "\\texttt is the mono family");
            t.Check(Chars(@"\Large w")[0].style.font.size == (int)Math.Round(14.4 * 65536), "\\Large is 14.4pt at 10pt");
            t.Check(Chars(@"\documentclass[12pt]{article}\begin{document}n\end{document}")[0].style.font.size == 12 * 65536, "the 12pt option");
            t.Check(Chars(@"\textcolor{red}{r}")[0].style.color == "#FF0000" && Chars(@"\color{blue!50}s")[0].style.color == "#8080FF",
                "xcolor names and tints");
            t.Check(Chars(@"\underline{u}")[0].style.underline, "\\underline");
            yield break;
        }

        [A_XSDActionDependency("Tex.Typeset.Sections", "Test")]
        private static IEnumerator<int> TypesetSections(TestContext t)
        {
            List<TexParagraph> heads = Pars(Typeset(@"\section{Intro}\subsection{Sub}\section*{Star}\section{Two}\subsection{S}", out _));
            string[] texts = heads.Select(Line).ToArray();
            t.Check(texts.SequenceEqual(new[] { "1 Intro", "1.1 Sub", "Star", "2 Two", "2.1 S" }), $"numbering, starred and reset: {string.Join(" / ", texts)}");
            t.Check(heads.Select(h => h.style.level).SequenceEqual(new[] { 1, 2, 1, 1, 2 }) && heads.All(h => h.style.kind == TexParKind.Heading),
                "sections are heading levels 1 and 2");
            TexChar first = heads[0].list.OfType<TexChar>().First();
            t.Check(first.style.font.bold && first.style.font.size == (int)Math.Round(14.4 * 65536), "a section is bold \\Large");

            List<TexParagraph> report = Pars(Typeset(@"\documentclass{report}\begin{document}\chapter{C}\section{S}\end{document}", out _));
            t.Check(report.Select(Line).SequenceEqual(new[] { "Chapter 1", "C", "1.1 S" }) && report[1].style.level == 1 && report[2].style.level == 2,
                "a report's chapters are level 1 and its sections number within them");

            t.Check(Lines(@"\paragraph{P} text") == "P text", "\\paragraph runs into its text");
            yield break;
        }

        [A_XSDActionDependency("Tex.Typeset.Lists", "Test")]
        private static IEnumerator<int> TypesetLists(TestContext t)
        {
            List<TexParagraph> items = Pars(Typeset(@"\begin{itemize}\item A\item B\begin{enumerate}\item C\item D\end{enumerate}\end{itemize}", out _));
            t.Check(items.Select(Line).SequenceEqual(new[] { "A", "B", "C", "D" }), "one paragraph per item");
            t.Check(items[0].style is { list: TexListKind.Itemize, listDepth: 1, item: true }, "an itemize item");
            t.Check(items[3].style is { list: TexListKind.Enumerate, listDepth: 2, listKindDepth: 1, itemNumber: 2, item: true },
                "a nested enumerate item knows its depth and number");

            List<TexParagraph> described = Pars(Typeset(@"\begin{description}\item[x] Y\end{description}", out _));
            t.Check(Line(described[0]) == "x Y" && described[0].list.OfType<TexChar>().First().style.font.bold, "a description label is bold");

            Lines(@"\item z", out List<TexError> errors);
            t.Check(Has(errors, "Lonely \\item"), "an \\item outside a list");
            yield break;
        }

        [A_XSDActionDependency("Tex.Typeset.Math", "Test")]
        private static IEnumerator<int> TypesetMath(TestContext t)
        {
            string math = Lines(@"$x^2$ $$a+b$$ \[ \frac{a}{b} \] \(y\)", out List<TexError> errors);
            t.Check(math == @"$x^2$ $$a+b$$$$\frac {a}{b}$$$y$" && errors.Count == 0, $"inline and display forms: {math}");
            string macro = Lines(@"\newcommand\R{\mathbb{R}}$x\in\R$", out errors);
            t.Check(macro == @"$x\in \mathbb {R}$" && errors.Count == 0, $"macros expand inside math: {macro}");
            t.Check(Lines(@"\begin{equation}E=mc^2\end{equation}") == @"$$E=mc^2\tag{1}$$", "an equation is numbered display math");

            string open = Lines("$a\n\nb", out errors);
            t.Check(open == "$a$ / b" && Has(errors, "Missing $ inserted"), $"a paragraph ends unclosed math: {open}");
            yield break;
        }

        [A_XSDActionDependency("Tex.Typeset.Ligatures", "Test")]
        private static IEnumerator<int> TypesetLigatures(TestContext t)
        {
            t.Check(Lines("``Hi'' `a'") == "“Hi” ‘a’", "quotes");
            t.Check(Lines("a--b---c") == "a–b—c", "dashes");
            t.Check(Lines("office fly f{}i") == "oﬃce ﬂy fi", "f-ligatures, broken by braces");
            t.Check(Lines(@"\texttt{--``}") == "--``", "no ligatures in mono");
            yield break;
        }

        [A_XSDActionDependency("Tex.Typeset.Accents", "Test")]
        private static IEnumerator<int> TypesetAccents(TestContext t)
        {
            string accented = Lines(@"\'e\""o\^{a}\c c\v{s}\'{\i}");
            t.Check(accented == "éöâçší", $"accents compose: {accented}");
            t.Check(Lines(@"\ss\ae\o\'{}") == "ßæø´", "letters and an accent over nothing");
            t.Check(Lines(@"\%\&\$\#\_ \ldots") == "%&$#_ …", "escaped characters");
            yield break;
        }

        [A_XSDActionDependency("Tex.Typeset.Verbatim", "Test")]
        private static IEnumerator<int> TypesetVerbatim(TestContext t)
        {
            List<TexParagraph> pars = Pars(Typeset("\\begin{verbatim}\n  x = {a}%\n\\end{verbatim}\nafter \\verb|\\foo{}| end", out List<TexError> errors));
            t.Check(pars.Count == 2 && pars[0].style.kind == TexParKind.Code && Line(pars[0]) == "  x = {a}%", "verbatim lines are code, read raw");
            t.Check(Line(pars[1]) == @"after \foo{} end" && errors.Count == 0, $"\\verb reads raw: {Line(pars[1])}");
            yield break;
        }

        [A_XSDActionDependency("Tex.Typeset.Errors", "Test")]
        private static IEnumerator<int> TypesetErrors(TestContext t)
        {
            Lines(@"\section{a", out List<TexError> errors);
            t.Check(Has(errors, @"File ended while scanning use of \section"), "an unclosed argument");
            Lines("a & b", out errors);
            t.Check(Has(errors, "Misplaced alignment tab"), "& outside a table");
            Lines("x^2", out errors);
            t.Check(Has(errors, "Missing $ inserted"), "a superscript outside math");
            Lines(@"\textbf{\verb|x|}", out errors);
            t.Check(Has(errors, @"\verb illegal in command argument"), "\\verb in an argument");
            Lines(@"\color{nocolor}x", out errors);
            t.Check(Has(errors, "Undefined color"), "an unknown colour");
            Lines(@"\begin{itemize}\item a\end{enumerate}", out errors);
            t.Check(Has(errors, @"\begin{itemize} on input line 1 ended by \end{enumerate}"), "a mismatched list end");
            Lines(@"\begin{document}\usepackage{x}", out errors);
            t.Check(Has(errors, "Can be used only in preamble"), "\\usepackage in the body");
            yield break;
        }

        [A_XSDActionDependency("Tex.Lowering", "Test")]
        private static IEnumerator<int> Lowering(TestContext t)
        {
            XElement document = TexLowering.Compile(
                "\\documentclass{article}\n\\begin{document}\n\\section{Intro}\nSome \\textbf{bold} text $x^2$.\n\\begin{itemize}\n\\item One\n\\end{itemize}\n\\end{document}\n",
                out List<TexError> errors);
            t.Check(errors.Count == 0, $"compiles clean: {string.Join("; ", errors.Select(e => e.message))}");

            XElement layout = document.Element("DocumentLayout")!;
            XElement page = layout.Element("Page")!;
            t.Check((string?)page.Attribute("Size") == "Letter" && (string?)page.Attribute("Mode") == "Paged", "letter paper, paged");
            t.Check(Math.Abs((float)page.Attribute("MarginLeft")! - 47.32f) < 0.01f && Math.Abs((float)page.Attribute("MarginTop")! - 43.05f) < 0.01f,
                "article's margins");
            string Style(string type) => string.Join(",", layout.Elements("TextStyle").Where(s => (string?)s.Attribute("Type") == type)
                .Select(s => $"{s.Attribute("FontSize")?.Value} {s.Attribute("FontName")?.Value}"));
            t.Check(Style("Text") == "13 latin-modern" && Style("Heading1") == "19 latin-modern" && Style("Code") == "13 latin-modern-mono",
                "text, heading and code styles in Latin Modern");

            XElement[] expected =
            {
                new XElement("Block", new XAttribute("StylingType", "Heading1"), new XAttribute("SpaceBefore", "20.038"),
                    new XElement("Run", new XAttribute("Text", "1"), new XAttribute("Bold", "true")),
                    new XElement("Run", new XAttribute("Text", "\u00A0"), new XAttribute("Bold", "true"), new XAttribute("Space", "19.128")),
                    new XElement("Run", new XAttribute("Text", "In\u00ADtro"), new XAttribute("Bold", "true"))),
                new XElement("Block", new XAttribute("Align", "Justify"), new XAttribute("SpaceBefore", "13.168"),
                    new XElement("Run", new XAttribute("Text", "Some ")),
                    new XElement("Run", new XAttribute("Text", "bold"), new XAttribute("Bold", "true")),
                    new XElement("Run", new XAttribute("Text", " text ")),
                    new XElement("Run", new XAttribute("Math", "x^2"), new XAttribute("FontName", "latin-modern-math")),
                    new XElement("Run", new XAttribute("Text", "."))),
                new XElement("Block", new XAttribute("Align", "Justify"), new XAttribute("List", "Bullet"), new XAttribute("Level", 0),
                    new XAttribute("Marker", "Disc"), new XAttribute("SpaceBefore", "10.627"), new XElement("Run", new XAttribute("Text", "One")))
            };
            string body = string.Concat(document.Elements("Block").Select(b => b.ToString(SaveOptions.DisableFormatting)));
            string want = string.Concat(expected.Select(b => b.ToString(SaveOptions.DisableFormatting)));
            t.Check(body == want, $"blocks and runs: {body}");
            RichTextDocument parsed = DocumentXml.Parse(document);
            t.Check(parsed.blocks.Length == 3, "the tree parses as a note");
            yield break;
        }

        [A_XSDActionDependency("Tex.Lowering.Lines", "Test")]
        private static IEnumerator<int> LoweringLines(TestContext t)
        {
            List<int> lines = new List<int>();
            XElement document = TexLowering.Compile(
                "\\documentclass{article}\n\\begin{document}\n\\section{Intro}\nFirst line\\\\\nsecond half.\n\n\\hrule\nNote.\\footnote{Text.}\n\\begin{verbatim}\na\nb\n\\end{verbatim}\n\\end{document}\n",
                out List<TexError> errors, lines);
            t.Check(errors.Count == 0, $"compiles clean: {string.Join("; ", errors.Select(e => e.message))}");
            t.Check(lines.Count == document.Elements("Block").Count() + document.Elements("Footnote").Elements("Block").Count(), $"one line per block: {lines.Count}");
            t.Check(string.Join(",", lines) == "3,4,4,0,8,8,10,11", $"heading, both halves of \\\\, rule, note, its footnote, verbatim lines: {string.Join(",", lines)}");
            yield break;
        }
        #endregion

        #region ---- tables, floats, references ----
        private static string CellText(TexTableCell cell) => string.Join(" / ", cell.vlist.OfType<TexParagraph>().Select(Line));

        private static string Folder(params (string name, string text)[] files)
        {
            string folder = Path.Combine(Path.GetTempPath(), $"aurora-tex-{Guid.NewGuid():N}");
            Directory.CreateDirectory(folder);
            foreach ((string name, string text) in files)
            {
                string path = Path.Combine(folder, name);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, text);
            }
            return folder;
        }

        [A_XSDActionDependency("Tex.Typeset.Tabular", "Test")]
        private static IEnumerator<int> TypesetTabular(TestContext t)
        {
            TexTable table = Typeset(@"\begin{tabular}{|l c|r|} \hline a & {\bf b} \bf & c \\ \hline \multicolumn{2}{c}{wide} & d \\ e & f \end{tabular}",
                out List<TexError> errors).vlist.OfType<TexTable>().Single();
            t.Check(errors.Count == 0, $"typesets clean: {string.Join("; ", errors.Select(e => e.message))}");
            t.Check(table.columns.Select(c => c.align).SequenceEqual(new[] { TexAlign.Left, TexAlign.Center, TexAlign.Right }),
                "l c r");
            t.Check(string.Join(",", table.vrules) == "1,0,1,1", $"| counted per column boundary: {string.Join(",", table.vrules)}");
            t.Check(string.Join(",", table.rules.Select(r => $"{r.kind}@{r.row}:{r.from}-{r.to}")) == "Plain@0:0-2,Plain@1:0-2",
                $"\\hline above the first and second rows: {string.Join(",", table.rules.Select(r => $"{r.kind}@{r.row}:{r.from}-{r.to}"))}");
            string rows = string.Join(" | ", table.rows.Select(r => string.Join(",", r.Select(c => CellText(c) + (c.span > 1 ? "*" + c.span : "")))));
            t.Check(rows == "a,b,c | wide*2,d | e,f", $"rows and cells: {rows}");
            t.Check(table.rows[1][0].vlist.OfType<TexParagraph>().Single().style.align == TexAlign.Center, "a multicolumn takes its own alignment");
            t.Check(table.rows[2][1].vlist.OfType<TexParagraph>().Single().style.align == TexAlign.Center, "a cell takes its column's alignment");
            t.Check(table.rows[0][1].vlist.OfType<TexParagraph>().Single().list.OfType<TexChar>().Single().style.font.bold
                && !table.rows[0][2].vlist.OfType<TexParagraph>().Single().list.OfType<TexChar>().Single().style.font.bold,
                "a cell's \\bf ends at the next &");

            TexTable plain = Typeset(@"\begin{tabular}{ll}a&b\end{tabular}", out _).vlist.OfType<TexTable>().Single();
            t.Check(plain.rules.Count == 0 && plain.vrules.All(n => n == 0), "no | and no rule has no rules");
            TexTable booktabs = Typeset(@"\begin{tabular}{lll}\toprule[2pt] a&b&c\\\cmidrule(lr){1-2}\cmidrule(l){3-3} c&d&e\\\cline{2-3}\hline\hline\bottomrule\end{tabular}",
                out errors).vlist.OfType<TexTable>().Single();
            string ruleList = string.Join(",", booktabs.rules.Select(r => $"{r.kind}@{r.row}:{r.from}-{r.to}{(r.trimLeft ? "l" : "")}{(r.trimRight ? "r" : "")}"));
            t.Check(booktabs.rows.Count == 2 && errors.Count == 0
                && ruleList == "Heavy@0:0-2,Cmid@1:0-1lr,Cmid@1:2-2l,Plain@2:1-2,Plain@2:0-2,Plain@2:0-2,Heavy@2:0-2",
                $"booktabs, \\cline and trims at their boundaries: {ruleList}; {string.Join("; ", errors.Select(e => e.message))}");
            t.Check(Math.Abs(booktabs.rules[0].width - 2 * 65536) < 2 && booktabs.rules[1].width == 0, "\\toprule[2pt] carries its width");

            TexTable spec = Typeset(@"\begin{tabular}{@{}*{3}{c}p{2cm}@{}}x\end{tabular}", out errors).vlist.OfType<TexTable>().Single();
            int twoCm = (int)(2f / 2.54f * 72.27f * 65536f);
            t.Check(spec.columns.Count == 4 && spec.columns[3].align == TexAlign.Justify && Math.Abs(spec.columns[3].width - twoCm) < 100,
                $"*{{3}}{{c}} and p{{2cm}}: {spec.columns.Count} columns, {spec.columns.LastOrDefault().width}sp");

            Typeset(@"\begin{tabular}{l}a&b\end{tabular}", out errors);
            t.Check(Has(errors, "Extra alignment tab"), "more cells than columns");
            Typeset(@"a & b", out errors);
            t.Check(Has(errors, "Misplaced alignment tab"), "& outside a tabular");

            TexTable outer = Typeset(@"\begin{tabular}{l}\begin{tabular}{ll}x&y\end{tabular}\end{tabular} after", out errors).vlist.OfType<TexTable>().Single();
            t.Check(outer.rows[0][0].vlist.OfType<TexTable>().Count() == 1 && errors.Count == 0, "a tabular in a cell stays in the cell");
            yield break;
        }

        [A_XSDActionDependency("Tex.Typeset.Floats", "Test")]
        private static IEnumerator<int> TypesetFloats(TestContext t)
        {
            TexTypesetter floats = Typeset(@"\begin{figure}[h]\centering x\caption{A cat}\end{figure}\begin{table}\caption{Data}\end{table}" +
                @"\renewcommand\figurename{Fig.}\begin{figure*}\caption{Dog}\end{figure*}", out List<TexError> errors);
            string text = string.Join(" / ", Pars(floats).Select(Line));
            t.Check(text == "x / Figure 1: A cat / Table 1: Data / Fig. 2: Dog" && errors.Count == 0, $"captions numbered per kind: {text}");
            t.Check(Pars(floats).Skip(1).All(p => p.style.align == TexAlign.Center), "captions are centred");

            string report = Lines(@"\documentclass{report}\begin{document}\chapter{One}\begin{figure}\caption{A}\end{figure}\end{document}");
            t.Check(report.EndsWith("Figure 1.1: A"), $"report numbers within the chapter: {report}");

            Typeset(@"\caption{Lost}", out errors);
            t.Check(Has(errors, "\\caption outside float"), "a caption outside a float");

            List<TexFloat> set = floats.vlist.OfType<TexFloat>().ToList();
            t.Check(set.Select(f => f.kind + ":" + f.placement).SequenceEqual(new[] { "figure:ht", "table:tbp", "figure:tbp" }),
                $"each float keeps its kind and placement, a lone h becoming ht: {string.Join(", ", set.Select(f => f.kind + ":" + f.placement))}");
            t.Check(!floats.vlist.OfType<TexVGlue>().Any(), "a float leaves no glue in the text");
            t.Check(Typeset(@"\begin{figure}[!b]x\end{figure}\begin{table}[H]x\end{table}\begin{figure}[pxq]x\end{figure}", out _).vlist.OfType<TexFloat>()
                .Select(f => f.placement).SequenceEqual(new[] { "!b", "H", "p" }), "! and H are kept, unknown letters dropped");

            TexTypesetter inside = Typeset(@"Before this \begin{figure}\caption{Mid}\end{figure} and after.", out errors);
            TexParagraph whole = inside.vlist.OfType<TexParagraph>().Single();
            t.Check(Line(whole) == "Before this and after." && whole.list.OfType<TexFloat>().Count() == 1 && errors.Count == 0,
                $"a float inside a paragraph leaves the paragraph whole: {Line(whole)}");

            Typeset(@"x\footnote{n \begin{figure}y\end{figure}}", out errors);
            t.Check(Has(errors, "Not in outer par mode"), "a float inside a footnote is an error");
            yield break;
        }

        [A_XSDActionDependency("Tex.Refs", "Test")]
        private static IEnumerator<int> Refs(TestContext t)
        {
            string source = "\\section{Intro}\\label{s:intro} See \\ref{s:b} and \\ref{s:intro}.\n" +
                "\\section{Back}\\label{s:b}\n" +
                "\\begin{enumerate}\\item a\\begin{enumerate}\\item q\\item b\\label{i:b}\\end{enumerate}\\end{enumerate}\n" +
                "Item \\ref{i:b}, note\\footnote{n\\label{fn}} \\ref{fn}, figure \\ref{fig}.\n" +
                "\\begin{figure}\\caption{c\\label{fig}}\\end{figure}\n" +
                "Missing \\ref{nope}.\n" +
                "$x = \\ref{s:b}$\\begin{equation}a\\label{eq}\\end{equation}\n";
            TexTypesetter refs = Typeset(source, out List<TexError> errors);
            string text = string.Join(" / ", Pars(refs).Select(Line));
            t.Check(text.Contains("See 2 and 1."), $"a forward and a backward \\ref: {text}");
            t.Check(text.Contains("Item 1b, note") && text.Contains(" 1, ﬁgure 1."),$"an item, footnote and figure label: {text}");
            t.Check(text.Contains("Missing ??."), "an unknown label prints ??");
            t.Check(errors.Count == 1 && Has(errors, "Reference `nope' undefined", 6), $"one warning, at its line: {string.Join("; ", errors)}");
            List<TexMathNode> math = Pars(refs).SelectMany(p => p.list).OfType<TexMathNode>().ToList();
            t.Check(math.Any(m => m.source == @"x = \text{2}") && math.Any(m => m.source == @"a\tag{1}"), $"\\ref in math resolved, \\label dropped: {string.Join(" | ", math.Select(m => m.source))}");

            Typeset(@"\section{A}\label{x}\section{B}\label{x}", out errors);
            t.Check(Has(errors, "Label `x' multiply defined"), "a label defined twice");
            yield break;
        }

        [A_XSDActionDependency("Tex.Equations", "Test")]
        private static IEnumerator<int> Equations(TestContext t)
        {
            string source = "\\begin{equation}a\\label{e:a}\\end{equation}\n" +
                "\\begin{equation*}b\\end{equation*}\n" +
                "\\begin{align}x &= 1 \\label{e:x}\\\\ y &= 2 \\notag\\\\ z &= 3 \\tag{A}\\label{e:z}\\\\ w &= 4\\\\\\end{align}\n" +
                "\\begin{align*}p &= q\\end{align*}\n" +
                "\\begin{multline}m \\\\ n\\label{e:m}\\end{multline}\n" +
                "See \\eqref{e:x}, \\ref{e:z} and \\eqref{e:m}. $\\eqref{e:a}$\n";
            TexTypesetter typesetter = Typeset(source, out List<TexError> errors);
            List<TexMathNode> math = Pars(typesetter).SelectMany(p => p.list).OfType<TexMathNode>().ToList();
            string all = string.Join(" | ", math.Select(m => m.source));
            string Tags(TexMathNode m) => string.Join(",", Regex.Matches(m.source, @"\\tag\s*\{([^}]*)\}").Select(x => x.Groups[1].Value));

            t.Check(errors.Count == 0, $"compiles clean: {string.Join("; ", errors)}");
            t.Check(math.Count == 6 && math[0].source == @"a\tag{1}" && math[1].source == "b", $"equation numbered, equation* not: {all}");
            t.Check(math[2].source.StartsWith(@"\begin{align}") && Tags(math[2]) == "2,A,3" && !math[2].source.Contains("notag"),
                $"align numbers each row but \\notag and \\tag rows, and not the empty last row: {math[2].source}");
            t.Check(math[3].source.StartsWith(@"\begin{align*}") && Tags(math[3]) == "", $"align* is unnumbered: {math[3].source}");
            t.Check(Tags(math[4]) == "4" && math[4].source.EndsWith(@"\tag{4}\end{multline}"), $"multline takes one number, on its last row: {math[4].source}");
            t.Check(typesetter.labels["e:a"] == "1" && typesetter.labels["e:x"] == "2" && typesetter.labels["e:z"] == "A" && typesetter.labels["e:m"] == "4",
                "labels point at their row's number or tag");

            string text = string.Join(" / ", Pars(typesetter).Select(Line));
            t.Check(text.Contains("See (2), A and (4)."), $"\\eqref and \\ref in text: {text}");
            t.Check(math[5].source == @"\text{(1)}", $"\\eqref in math: {math[5].source}");

            string report = "\\documentclass{report}\\begin{document}\\chapter{A}\\begin{equation}x\\end{equation}\\end{document}";
            t.Check(Pars(Typeset(report, out _)).SelectMany(p => p.list).OfType<TexMathNode>().Single().source == @"x\tag{1.1}",
                "report numbers equations within the chapter");
            yield break;
        }

        [A_XSDActionDependency("Tex.MathEnvironments", "Test")]
        private static IEnumerator<int> MathEnvironments(TestContext t)
        {
            string matrix = Lines(@"\[ \begin{pmatrix} a & b \\ c & d \end{pmatrix} \]", out List<TexError> errors);
            t.Check(matrix == @"$$\begin{pmatrix} a & b \\ c & d \end{pmatrix}$$" && errors.Count == 0, $"an environment inside math is written back as source: {matrix}");

            List<TexMathNode> math = Pars(Typeset(@"\begin{equation}\begin{split}a&=b\\&=c\end{split}\end{equation}" +
                @"\begin{align}\left\{\begin{aligned}a\\b\end{aligned}\right. \\ c\end{align}", out errors))
                .SelectMany(p => p.list).OfType<TexMathNode>().ToList();
            t.Check(math[0].source == @"\begin{split}a&=b\\&=c\end{split}\tag{1}", $"split inside equation takes one number: {math[0].source}");
            t.Check(Regex.Matches(math[1].source, @"\\tag").Count == 2, $"rows split only at the top level: {math[1].source}");
            t.Check(math.All(m => MathParser.Parse(m.source) is not MathError) && errors.Count == 0, "both parse as math");

            Lines(@"\begin{pmatrix} a \end{pmatrix}", out errors);
            t.Check(Has(errors, "Missing $ inserted"), "a math environment in text");

            string ops = Lines(@"\DeclareMathOperator{\tr}{tr}\DeclareMathOperator*{\argmax}{argmax}$\tr A + \argmax_x f$", out errors);
            t.Check(ops.Contains(@"\operatorname {tr}") && ops.Contains(@"\operatorname *{argmax}") && errors.Count == 0,
                $"\\DeclareMathOperator defines \\operatorname macros: {ops}");
            yield break;
        }

        [A_XSDActionDependency("Tex.Bib", "Test")]
        private static IEnumerator<int> Bib(TestContext t)
        {
            string bib = "@string{tug = \"TUGboat\"}\n@comment{ignored}\n" +
                "@book{knuth84, author = {Donald E. Knuth}, title = {The {\\TeX}book}, publisher = \"Addison-Wesley\", year = 1984}\n" +
                "@article{lamport, author = \"Leslie Lamport and others\", title = {A Document Preparation System}, journal = tug # \" Journal\",\n" +
                "  volume = 7, number = {2}, pages = {10-20}, year = {1986}, month = jun}\n" +
                "@inproceedings(vdw, author = {Johannes Diderik van der Waals and Smith, Jr., John}, title = {On Gases}, booktitle = {Proc. Physics}, year = 1873)\n" +
                "@misc{unused, title = {Never cited}, year = 2000}\n";
            string folder = Folder(("refs.bib", bib));
            string Doc(string style) => "\\documentclass{article}\\begin{document}\n\\cite{lamport} and \\cite[p.~5]{knuth84,vdw}.\n" +
                $"\\bibliographystyle{{{style}}}\\bibliography{{refs}}\nAfter \\cite{{missing}}.\n\\end{{document}}\n";

            List<TexError> errors = new List<TexError>();
            List<string> Typeset(string style)
            {
                TexTypesetter typesetter = new TexTypesetter(Doc(style), null, folder);
                typesetter.Run();
                errors = typesetter.errors;
                return Pars(typesetter).Select(p => System.Text.RegularExpressions.Regex.Replace(Line(p), " {2,}", " ")).ToList();
            }

            List<string> plain = Typeset("plain");
            t.Check(plain[0] == "[2] and [1, 3, p.\u00A05].", $"sorted numbers, a note: {plain[0]}");
            t.Check(plain[1] == "References" && plain[^1] == "After [?].", $"the list stands where \\bibliography was: {string.Join(" / ", plain)}");
            t.Check(plain.Count == 6, $"three entries, the uncited one left out: {plain.Count}");
            t.Check(plain[2] == "[1] Donald E. Knuth. The TeXbook. Addison-Wesley, 1984.", $"a book: {plain[2]}");
            t.Check(plain[3] == "[2] Leslie Lamport et\u00A0al. A document preparation system. TUGboat Journal, 7(2):10–20, June 1986.", $"an article: {plain[3]}");
            t.Check(plain[4] == "[3] Johannes Diderik van der Waals and John Smith, Jr. On gases. In Proc. Physics, 1873.", $"von and Jr names: {plain[4]}");
            t.Check(errors.Count == 1 && Has(errors, "Citation `missing' undefined"), $"one warning: {string.Join("; ", errors)}");

            t.Check(Typeset("unsrt")[0] == "[1] and [2, 3, p.\u00A05].", "unsrt numbers in citation order");
            List<string> alpha = Typeset("alpha");
            t.Check(alpha[0] == "[L+86] and [Knu84, vdWS73, p.\u00A05]." && alpha[2].StartsWith("[Knu84]"), $"alpha labels: {alpha[0]} / {alpha[2]}");
            t.Check(Typeset("abbrv")[2] == "[1] D.\u00A0E. Knuth. The TeXbook. Addison-Wesley, 1984.", "abbrv initials");

            TexTypesetter none = new TexTypesetter(@"\cite{a}\bibliographystyle{plain}\bibliography{nope}");
            none.Run();
            t.Check(Has(none.errors, "I couldn't open database file nope.bib"), "a missing .bib");

            List<TexBibEntry> entries = new List<TexBibEntry>();
            List<string> problems = new List<string>();
            TexBibliography.Parse("@misc{x, title = \"Say {\"}hi{\"}\" # {!}, note = undefinedname, year 1}", entries, problems.Add);
            t.Check(entries.Single().fields["title"] == "Say {\"}hi{\"}!", $"quotes with braced quotes, #: {entries.Single().fields["title"]}");
            t.Check(problems.Any(p => p.Contains("undefinedname")) && problems.Any(p => p.Contains("expecting a field name")), $"parse problems: {string.Join("; ", problems)}");
            Directory.Delete(folder, true);
            yield break;
        }

        [A_XSDActionDependency("Tex.Lowering.Table", "Test")]
        private static IEnumerator<int> LoweringTable(TestContext t)
        {
            List<int> lines = new List<int>();
            XElement document = TexLowering.Compile("Before.\n\\begin{tabular}{lr}\na & bb \\\\\n\\multicolumn{2}{c}{a wide cell of text}\n\\end{tabular}\n",
                out List<TexError> errors, lines);
            t.Check(errors.Count == 0, $"compiles clean: {string.Join("; ", errors.Select(e => e.message))}");
            XElement table = document.Element("Table")!;
            t.Check((string?)table.Attribute("Borders") == "false", "an unruled tabular draws no borders");
            float[] widths = table.Elements("Column").Select(c => (float)c.Attribute("Width")!).ToArray();
            t.Check(widths.Length == 2 && widths.All(w => w >= 24f), $"two columns: {string.Join(",", widths)}");
            List<XElement> rows = table.Elements("Row").ToList();
            t.Check(rows.Count == 2 && rows[0].Elements("Cell").Count() == 2 && (int?)rows[1].Element("Cell")!.Attribute("ColumnSpan") == 2,
                "two cells, then one spanning both");
            t.Check((string?)rows[0].Elements("Cell").Last().Element("Block")!.Attribute("Align") == "Right"
                && (string?)rows[1].Element("Cell")!.Element("Block")!.Attribute("Align") == "Center", "cells carry their alignment");
            t.Check(string.Join(",", lines) == "1,2", $"the table is one block at its \\begin line: {string.Join(",", lines)}");

            RichTextDocument parsed = DocumentXml.Parse(document);
            NoteTable control = (NoteTable)parsed.blocks[1];
            t.Check(!control.showBorders && control.rows.SelectMany(r => r).ElementAt(2).span == 2, "it parses as a borderless table with a merged cell");
            t.Check(table.Attribute("Align") == null && control.alignment == TextAlignment.Left, "a tabular in a justified paragraph sits left");

            string Aligned(string body)
            {
                XElement compiled = TexLowering.Compile(body, out _);
                return (string?)compiled.Descendants("Table").Single().Attribute("Align") ?? "Left";
            }
            const string tabular = "\\begin{tabular}{l}a\\end{tabular}";
            t.Check(Aligned($"\\begin{{center}}{tabular}\\end{{center}}") == "Center", "center centres");
            t.Check(Aligned($"\\begin{{table}}\\centering{tabular}\\end{{table}}") == "Center", "\\centering in a float centres");
            t.Check(Aligned($"\\begin{{flushright}}{tabular}\\end{{flushright}}") == "Right", "flushright");
            t.Check(Aligned($"{{\\centering{tabular}}}\n\nAfter.") == "Left", "\\centering closed before the paragraph ends does not");

            XElement ruled = TexLowering.Compile(@"\begin{tabular}{|l||r|}\toprule a&b\\\cmidrule(lr){1-1}\cline{2-2} c&d\\\hline\hline\end{tabular}", out _)
                .Descendants("Table").Single();
            List<XElement> columns = ruled.Elements("Column").ToList();
            t.Check((string?)ruled.Attribute("Borders") == "false" && ((string?)ruled.Attribute("Padding"))?.EndsWith(" 0") == true,
                $"a ruled tabular draws its own rules with LaTeX's padding: {ruled.Attribute("Padding")}");
            t.Check((string?)columns[0].Attribute("RuleLeft") == "Plain" && (string?)columns[1].Attribute("RuleLeft") == "Double"
                && (string?)columns[1].Attribute("RuleRight") == "Plain" && columns[0].Attribute("RuleRight") == null, "| and || on their column edges");
            List<XElement> top = ruled.Elements("Row").First().Elements("Cell").ToList();
            List<XElement> bottom = ruled.Elements("Row").Last().Elements("Cell").ToList();
            t.Check(top.All(c => (string?)c.Attribute("RuleAbove") == "Heavy"), "\\toprule over every first-row cell");
            t.Check((string?)bottom[0].Attribute("RuleAbove") == "Cmid" && (string?)bottom[0].Attribute("TrimAbove") == "Both"
                && (string?)bottom[1].Attribute("RuleAbove") == "Plain" && bottom[1].Attribute("TrimAbove") == null, "\\cmidrule(lr) and \\cline on their columns");
            t.Check(bottom.All(c => (string?)c.Attribute("RuleBelow") == "Double"), "\\hline\\hline under the last row is a double rule");

            XElement centred = TexLowering.Compile($"\\begin{{center}}{tabular}\\end{{center}}", out _);
            TableControl table2 = new TableControl(DocumentXml.Parse(centred).blocks.OfType<NoteTable>().Single());
            t.Check(DocumentXml.WriteTable(table2.model).Attribute("Align")?.Value == "Center", "Align round-trips");
            Vector2 desired = table2.Measure(new Vector2(1000f, float.PositiveInfinity));
            table2.Arrange(new LayoutRect(0f, 0f, 1000f, desired.Y));
            StackPanelControl first = table2.Cells()[0];
            float left = first.arrangedRect.x - first.margin.left;
            t.Check(MathF.Abs(left - (1000f - desired.X) * 0.5f) < 0.5f, $"the columns sit centred: {left} of {(1000f - desired.X) * 0.5f}");
            table2.Destroy();
            yield break;
        }

        [A_XSDActionDependency("Tex.Lowering.Image", "Test")]
        private static IEnumerator<int> LoweringImage(TestContext t)
        {
            string folder = Folder(("doc.pdf", ""));
            using (SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32> png = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(40, 20))
            {
                SixLabors.ImageSharp.ImageExtensions.SaveAsPng(png, Path.Combine(folder, "pic.png"));
                Directory.CreateDirectory(Path.Combine(folder, "sub"));
                SixLabors.ImageSharp.ImageExtensions.SaveAsPng(png, Path.Combine(folder, "sub", "inner.png"));
            }

            List<XElement> Pictures(string source, out List<TexError> errors) =>
                TexLowering.Compile(source, out errors, null, folder).Descendants("Run").Where(r => r.Attribute("Image") != null).ToList();

            List<XElement> pictures = Pictures(@"\includegraphics[width=0.5\textwidth]{pic} \includegraphics[scale=2,angle=90]{pic.png}", out List<TexError> errors);
            t.Check(errors.Count == 0 && pictures.Count == 2, $"two pictures: {string.Join("; ", errors.Select(e => e.message))}");
            t.Check((string?)pictures[0].Attribute("Image") == Path.GetFullPath(Path.Combine(folder, "pic.png")), "the path is found and absolute");
            float half = 0.5f * 345f * 96f / 72.27f;
            t.Check(Math.Abs((float)pictures[0].Attribute("Width")! - half) < 0.5f && pictures[0].Attribute("Height") == null,
                $"width=0.5\\textwidth: {pictures[0].Attribute("Width")?.Value}");
            t.Check((string?)pictures[1].Attribute("Width") == "80" && (string?)pictures[1].Attribute("Height") == "40" && (string?)pictures[1].Attribute("Rotation") == "-90",
                "scale from the picture's own size, angle turned clockwise");

            t.Check(Pictures(@"\graphicspath{{sub/}}\includegraphics{inner}", out errors).Count == 1 && errors.Count == 0, "\\graphicspath");
            Pictures(@"\includegraphics{absent}", out errors);
            t.Check(Has(errors, "File `absent' not found"), "a missing picture");
            Pictures(@"\includegraphics{doc}", out errors);
            t.Check(Has(errors, "PDF and EPS pictures are not supported"), "a PDF picture");
            TexLowering.Compile(@"\includegraphics{pic}", out errors);
            t.Check(Has(errors, "File `pic' not found"), "no folder finds nothing");
            Directory.Delete(folder, true);
            yield break;
        }
        #endregion

        #region ---- line breaking ----
        // each line but the last: how far its spaces stretch (above 0) or shrink (below 0) to fill the width
        private static List<float> Ratios(BlockLayout layout, TextMeasurer.Run run, string text, float width)
        {
            List<float> ratios = new List<float>();
            for (int l = 0; l < layout.lines.Count - 1; l++)
            {
                LineSegment segment = layout.lines[l].segments[0];
                float pen = 0f, visible = 0f, spaces = 0f, inner = 0f;
                for (int k = segment.charStart; k < segment.charStart + segment.charCount; k++)
                {
                    float advance = TextMeasurer.MeasureAdvance(text[k], run);
                    pen += advance;
                    if (text[k] == ' ') spaces += advance;
                    else
                    {
                        visible = pen;
                        inner = spaces;
                    }
                }
                ratios.Add(visible <= width ? (width - visible) / (inner * TextMeasurer.GlueStretch) : (width - visible) / (inner * TextMeasurer.GlueShrink));
            }
            return ratios;
        }

        [A_XSDActionDependency("Tex.OptimalBreaks", "Test")]
        private static IEnumerator<int> OptimalBreaks(TestContext t)
        {
            IGlyphMetrics metrics = new FontAssetGlyphMetrics();
            AtlasMetaData atlas = AssetRegistries.GetRegistryByValueType<string, FontAsset>(typeof(FontAsset))["default"].atlasMetaData;
            const string text = "In olden times when wishing still helped one, there lived a king whose daughters were all beautiful, "
                + "but the youngest was so beautiful that the sun itself, which has seen so much, was astonished whenever it shone in her face. "
                + "Close by the king's castle lay a great dark forest, and under an old lime tree in the forest was a well, and when the day "
                + "was very warm, the king's child went out into the forest and sat down by the side of the cool fountain.";
            TextMeasurer.Run run = new TextMeasurer.Run(text, 0, text.Length, "default", atlas, 16, FontStyle.Regular);
            List<TextMeasurer.Run> runs = new List<TextMeasurer.Run> { run };

            int applied = 0;
            bool looser = false;
            for (float width = 260f; width <= 900f; width += 20f)
            {
                List<float> greedy = Ratios(TextMeasurer.MeasureBlock(runs, width, metrics, 1.5f), run, text, width);
                List<float> optimal = Ratios(TextMeasurer.MeasureBlock(runs, width, metrics, 1.5f, optimal: true), run, text, width);
                bool feasible = optimal.All(r => r >= -1f && r <= 1.26f);
                t.Check(feasible || optimal.SequenceEqual(greedy),
                    $"at {width} px Knuth-Plass sets every line within tolerance or leaves the block to greedy: {string.Join(", ", optimal.Select(r => r.ToString("0.00")))}");
                if (!feasible || optimal.SequenceEqual(greedy)) continue;
                applied++;
                if (greedy.Max() > optimal.Max()) looser = true;
            }
            t.Check(applied > 0 && looser, $"Knuth-Plass applies at {applied} widths and somewhere greedy leaves a looser line than it");
            yield break;
        }

        [A_XSDActionDependency("Tex.Hyphenator", "Test")]
        private static IEnumerator<int> Hyphenator(TestContext t)
        {
            static string Show(string word, bool[]? points) =>
                points == null ? word : string.Concat(word.Select((c, i) => (points[i] ? "-" : "") + c));

            TexHyphenator liang = new TexHyphenator(new[] { "hy3ph", "he2n", "hena4", "hen5at", "1na", "n2at", "1tio", "2io", "o2n", "1b" }, new[] { "as-so-ciate" });
            t.Check(Show("hyphenation", liang.Points("hyphenation")) == "hy-phen-ation", $"Liang's example: {Show("hyphenation", liang.Points("hyphenation"))}");
            t.Check(Show("associate", liang.Points("associate")) == "as-so-ciate", "an exception is used as written");
            t.Check(Show("table", liang.Points("table")) == "ta-ble" && liang.Points("tab") == null, "a break needs 2 letters before and 3 after");
            t.Check(liang.Points("abode") == null, "a break 1 letter from the start is dropped");
            Dictionary<string, bool[]> extra = new Dictionary<string, bool[]> { ["hyphenation"] = TexHyphenator.Exception("hyphe-nation")!.Value.points };
            t.Check(Show("hyphenation", liang.Points("hyphenation", extra)) == "hyphe-nation", "a document's \\hyphenation wins over patterns");

            TexHyphenator? english = TexHyphenator.English;
            t.Check(english != null, "the US English patterns load");
            if (english != null)
            {
                string Word(string w) => Show(w, english.Points(w));
                t.Check(Word("hyphenation") == "hy-phen-ation" && Word("table") == "ta-ble" && Word("present") == "present",
                    $"US English: {Word("hyphenation")} {Word("table")} {Word("present")}");
                t.Check(Word("democrat") == "de-mo-c-rat", $"the patterns' documented TeX result, bug included: {Word("democrat")}");
            }
            yield break;
        }

        [A_XSDActionDependency("Tex.SoftHyphen", "Test")]
        private static IEnumerator<int> SoftHyphen(TestContext t)
        {
            IGlyphMetrics metrics = new FontAssetGlyphMetrics();
            AtlasMetaData atlas = AssetRegistries.GetRegistryByValueType<string, FontAsset>(typeof(FontAsset))["default"].atlasMetaData;
            const string text = "aaaa bbbb\u00ADcccc dddd";
            TextMeasurer.Run run = new TextMeasurer.Run(text, 0, text.Length, "default", atlas, 16, FontStyle.Regular);
            List<TextMeasurer.Run> runs = new List<TextMeasurer.Run> { run };
            float Width(string s) => s.Sum(c => TextMeasurer.MeasureAdvance(c, run));
            float hyphen = TextMeasurer.MeasureAdvance('-', run);
            t.Check(TextMeasurer.MeasureAdvance(TextMeasurer.SoftHyphen, run) == 0f && hyphen > 0f, "a soft hyphen takes no room");

            float fits = Width("aaaa bbbb") + hyphen + 1f;
            foreach (bool optimal in new[] { false, true })
            {
                BlockLayout layout = TextMeasurer.MeasureBlock(runs, fits, metrics, 1.5f, optimal: optimal);
                TextLine first = layout.lines[0];
                LineSegment end = first.segments[^1];
                t.Check(layout.lines.Count >= 2 && text[end.charStart + end.charCount - 1] == TextMeasurer.SoftHyphen && first.hyphen == hyphen
                    && first.width <= fits, $"{(optimal ? "Knuth-Plass" : "greedy")}: the line breaks at the soft hyphen and shows a hyphen inside the width");
                t.Check(layout.lines.All(l => l != first ? l.hyphen == 0f : true), "only the hyphenated line shows a hyphen");
            }

            BlockLayout tight = TextMeasurer.MeasureBlock(runs, Width("aaaa bbbb") + hyphen * 0.5f, metrics, 1.5f);
            t.Check(tight.lines[0].hyphen == 0f && tight.lines[0].segments[^1].charStart + tight.lines[0].segments[^1].charCount == 5,
                "when the hyphen does not fit the line breaks at the space instead");
            BlockLayout whole = TextMeasurer.MeasureBlock(runs, 1000f, metrics, 1.5f);
            t.Check(whole.lines.Count == 1 && whole.lines[0].hyphen == 0f, "an unbroken soft hyphen shows nothing");

            XElement document = new XElement("Document", new XElement("Block", new XElement("Run", new XAttribute("Text", text))));
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
            editor.SelectAll();
            editor.Copy();
            t.Check(ClipboardText.Get() == "aaaa bbbbcccc dddd", $"copy drops soft hyphens: {ClipboardText.Get()}");
            t.Show(new StackPanelControl());
        }

        [A_XSDActionDependency("Tex.Lowering.Hyphens", "Test")]
        private static IEnumerator<int> LoweringHyphens(TestContext t)
        {
            XElement document = TexLowering.Compile("\\hyphenation{ta-ble}\nA big ex\\-tra table \\texttt{table}.", out List<TexError> errors);
            t.Check(errors.Count == 0, $"compiles clean: {string.Join("; ", errors.Select(e => e.message))}");
            List<string> runs = document.Descendants("Run").Select(r => (string?)r.Attribute("Text") ?? "").ToList();
            t.Check(runs.Count == 3 && runs[0] == "A big ex\u00ADtra ta\u00ADble " && runs[1] == "table" && runs[2] == ".",
                $"\\- and \\hyphenation become soft hyphens, monospace is left whole: {string.Join(" | ", runs).Replace('\u00AD', '~')}");
            yield break;
        }

        [A_XSDActionDependency("Tex.Lowering.Spacing", "Test")]
        private static IEnumerator<int> LoweringSpacing(TestContext t)
        {
            const string source = """
                \documentclass{article}
                \begin{document}
                \section{A}
                First paragraph.

                Second paragraph.

                \noindent Third.

                \bigskip
                Fourth \[ x \] after.
                \begin{itemize}\item a \item b\end{itemize}
                Fifth.

                Sixth\quad x\hspace{1cm}y.
                \end{document}
                """;
            XElement document = TexLowering.Compile(source, out List<TexError> errors);
            t.Check(errors.Count == 0, $"compiles clean: {string.Join("; ", errors.Select(e => e.message))}");
            List<XElement> blocks = document.Elements("Block").ToList();
            const float px = 96f / 72.27f;
            float At(int i, string name) => (float?)blocks[i].Attribute(name) ?? 0f;
            bool Near(float a, float b) => MathF.Abs(a - b) < 0.01f;
            t.Check(blocks.Count == 11, $"heading, 3 paragraphs, text / display / text, 2 items, 2 paragraphs: {blocks.Count}");
            if (blocks.Count != 11) yield break;

            t.Check(At(1, "Indent") == 0f && Near(At(1, "SpaceBefore"), 2.3f * At(0, "SpaceBefore") / 3.5f), "the first paragraph after a heading is not indented, 2.3ex below it");
            t.Check(Near(At(2, "Indent"), 15f * px) && At(2, "SpaceBefore") == 0f, "a following paragraph is indented 15pt, with no \\parskip");
            t.Check(At(3, "Indent") == 0f, "\\noindent");
            t.Check(Near(At(4, "SpaceBefore"), 12f * px) && Near(At(4, "Indent"), 15f * px), "\\bigskip above an indented paragraph");
            t.Check(blocks[5].Elements("Run").Single().Attribute("Display") != null && Near(At(5, "SpaceBefore"), 10f * px),
                "a display formula is a block of its own, \\abovedisplayskip above it");
            t.Check(Near(At(6, "SpaceBefore"), 10f * px) && At(6, "Indent") == 0f, "the text after a display continues unindented, \\belowdisplayskip above it");
            t.Check(Near(At(7, "SpaceBefore"), 8f * px) && Near(At(8, "SpaceBefore"), 8f * px), "\\topsep above a list, \\itemsep + \\parsep between items");
            t.Check(Near(At(9, "SpaceBefore"), 8f * px) && At(9, "Indent") == 0f, "\\topsep below a list, and the text right after it is not indented");
            List<float> spacers = blocks[10].Elements("Run").Select(r => (float?)r.Attribute("Space")).OfType<float>().ToList();
            t.Check(Near(At(10, "Indent"), 15f * px) && spacers.Count == 2 && Near(spacers[0], 10f * px) && Near(spacers[1], 72.27f / 2.54f * px),
                $"after a blank line the paragraph is indented; \\quad and \\hspace{{1cm}} are exact spacers: {string.Join(", ", spacers)}");
            t.Check((string?)document.Element("DocumentLayout")!.Attribute("BlockSpacing") == "0", "\\parskip is 0");
        }

        [A_XSDActionDependency("Tex.BlockSpacing", "Test")]
        private static IEnumerator<int> BlockSpacing(TestContext t)
        {
            IGlyphMetrics metrics = new FontAssetGlyphMetrics();
            AtlasMetaData atlas = AssetRegistries.GetRegistryByValueType<string, FontAsset>(typeof(FontAsset))["default"].atlasMetaData;
            string words = string.Join(" ", Enumerable.Repeat("lorem ipsum", 20));
            List<TextMeasurer.Run> runs = new List<TextMeasurer.Run> { new TextMeasurer.Run(words, 0, words.Length, "default", atlas, 16, FontStyle.Regular) };
            foreach (bool optimal in new[] { false, true })
            {
                BlockLayout layout = TextMeasurer.MeasureBlock(runs, 300f, metrics, 1.5f, 40f, optimal: optimal);
                TextLine first = layout.lines[0];
                t.Check(first.left == 40f && first.room == 260f && first.width <= 260f + 1f && layout.lines.Skip(1).All(l => l.left == 0f),
                    $"{(optimal ? "Knuth-Plass" : "greedy")}: the first line starts at the indent and fills the room left: {first.left} {first.room} {first.width}");
            }
            List<TextMeasurer.Run> spacer = new List<TextMeasurer.Run> { new TextMeasurer.Run("a b", 1, 1, "default", atlas, 16, FontStyle.Regular, space: 25f) };
            t.Check(TextMeasurer.MeasureAdvance(' ', spacer[0]) == 25f, "a spacer advances its own width");

            XElement source = new XElement("Document",
                new XElement("Block", new XElement("Run", new XAttribute("Text", "first"))),
                new XElement("Block", new XAttribute("Indent", "30"), new XAttribute("SpaceBefore", "40"), new XAttribute("Align", "Justify"),
                    new XElement("Run", new XAttribute("Text", words + " a")),
                    new XElement("Run", new XAttribute("Text", " "), new XAttribute("Space", "25")),
                    new XElement("Run", new XAttribute("Text", "b"))),
                new XElement("Block", new XElement("Run", new XAttribute("Text", "last"))));
            RichTextDocument parsed = DocumentXml.Parse(source);
            XElement written = DocumentXml.ToXml(parsed);
            List<XElement> blocks = written.Elements().Where(e => e.Name.LocalName == "Block").ToList();
            t.Check((string?)blocks[1].Attribute("Indent") == "30" && (string?)blocks[1].Attribute("SpaceBefore") == "40"
                && blocks[1].Elements().Any(r => (string?)r.Attribute("Space") == "25"), "Indent, SpaceBefore and Space round-trip through .xml");

            string path = Path.Combine(Path.GetTempPath(), $"aurora-tex-{Guid.NewGuid():N}.xml");
            source.Save(path);
            DocumentEditorControl editor = new DocumentEditorControl
            {
                horizontalAlignment = HorizontalAlignment.Stretch,
                verticalAlignment = VerticalAlignment.Stretch
            };
            t.Show(editor);
            editor.LoadPath(path);
            File.Delete(path);
            yield return 2;

            List<BlockControl> p = Content(editor).children.OfType<BlockControl>().ToList();
            float zoom = p[1].Lines[0].left / 30f;
            float gap = p[1].arrangedRect.y - (p[0].arrangedRect.y + p[0].arrangedRect.height);
            float plain = p[2].arrangedRect.y - (p[1].arrangedRect.y + p[1].arrangedRect.height);
            t.Check(zoom > 0f && MathF.Abs(gap - 40f * zoom) < 0.5f && MathF.Abs(plain - editor.session.document.layout.blockSpacing * zoom) < 0.5f,
                $"SpaceBefore replaces the block spacing above its block only: {gap} and {plain} at zoom {zoom}");
            t.Check(p[1].Lines.Count > 2 && p[1].Lines.Skip(1).All(l => l.left == 0f), "only the first line is indented");

            int spacerAt = p[1].Length - 2;
            p[1].InsertText(spacerAt + 1, "x");
            t.Check(p[1].spans.Count(s => s.spaceWidth != 0f) == 1 && p[1].spans.Single(s => s.spaceWidth != 0f).count == 1,
                "text typed after a spacer is text, not more spacer");
            p[1].RemoveText(spacerAt + 1, 1);

            BlockSnapshot snapshot = p[1].Snapshot();
            BlockControl tail = p[1].SplitAt(10);
            t.Check(tail.firstIndent == 30f && tail.spaceBefore == 40f && snapshot.firstIndent == 30f && snapshot.spaceBefore == 40f,
                "a split and a snapshot carry the indent and the space above");
            tail.Destroy();
            p[1].Restore(snapshot);

            editor.SelectAll();
            editor.Copy();
            t.Check(ClipboardText.Get().Contains(" a b"), $"a spacer copies as a space: {ClipboardText.Get().Substring(Math.Max(0, ClipboardText.Get().Length - 12))}");
            t.Show(new StackPanelControl());
        }

        [A_XSDActionDependency("Tex.JustifyBeforeDisplay", "Test")]
        private static IEnumerator<int> JustifyBeforeDisplay(TestContext t)
        {
            string words = string.Join(" ", Enumerable.Repeat("lorem ipsum dolor sit", 12));
            XElement document = new XElement("Document",
                new XElement("Block", new XAttribute("Align", "Justify"),
                    new XElement("Run", new XAttribute("Text", words + " ")),
                    new XElement("Run", new XAttribute("Math", "x^2"), new XAttribute("Display", "true")),
                    new XElement("Run", new XAttribute("Text", " " + words))));
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

            IReadOnlyList<TextLine> lines = ((BlockControl)View(editor, 0)).Lines;
            int display = lines.ToList().FindIndex(l => l.segments.Count > 0 && l.segments[0].runIndex == 1);
            t.Check(display > 1, $"the text wraps onto two lines or more before the display: display on line {display} of {lines.Count}");
            if (display > 1)
            {
                t.Check(lines[0].spaceExtra > 0f, "a line inside the paragraph is stretched");
                t.Check(lines[display - 1].spaceExtra == 0f, "the line before the display is not stretched");
            }
            t.Show(new StackPanelControl());
        }
        #endregion

        #region ---- pages ----
        private static List<XElement> TopBlocks(XElement document) => document.Elements().Where(e => e.Name.LocalName is "Block" or "Table").ToList();

        private static string BlockText(XElement block) => string.Concat(block.Elements("Run").Select(r => (string?)r.Attribute("Text"))).Replace("­", "");

        // A lowered or hand-built <Document> in a note editor, from a temporary file.
        private static DocumentEditorControl ShowDocument(TestContext t, XElement document)
        {
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
            return editor;
        }

        private static DocumentControl Content(DocumentEditorControl editor) => editor.children.OfType<DocumentControl>().First();

        // The control showing a note-level entry.
        private static Control View(DocumentEditorControl editor, int block) => Content(editor).ViewOf(editor.session.document.blocks[block]);

        // Each sheet's head and foot labels, page by page, in SlotPlace order.
        private static List<string[]> Margins(DocumentEditorControl editor)
        {
            DocumentControl content = Content(editor);
            return content.children.OfType<PanelControl>().Where(p => p.children.Count == 1 && p.children[0] is ContainerControl)
                .Select(p => ((Control)p.children[0]).children.OfType<LabelControl>().Select(l => l.text).ToArray()).ToList();
        }

        [A_XSDActionDependency("Tex.Lowering.Pages", "Test")]
        private static IEnumerator<int> LoweringPages(TestContext t)
        {
            const string source = """
                \documentclass{article}
                \pagestyle{headings}
                \begin{document}
                \section{Introduction}\label{sec:intro}
                Text on page \pageref{sec:intro}.
                \newpage
                \newpage
                After a break.
                \clearpage
                \thispagestyle{empty}After a clear.

                \markboth{Left}{Right}
                \subsection{Sub}
                Unknown \pageref{nowhere}.
                \end{document}
                """;
            XElement document = TexLowering.Compile(source, out List<TexError> errors);
            t.Check(errors.Count == 1 && errors[0].message.Contains("`nowhere' undefined"), $"only the missing label is reported: {string.Join("; ", errors.Select(e => e.message))}");

            List<XElement> blocks = TopBlocks(document);
            XElement heading = blocks.First(b => BlockText(b).Contains("Introduction"));
            t.Check((string?)heading.Attribute("MarkRight") == "1  INTRODUCTION" && heading.Attribute("MarkLeft") == null,
                $"a numbered section under headings sets \\rightmark to its uppercased number and title: {(string?)heading.Attribute("MarkRight")}");
            XElement after = blocks.First(b => BlockText(b).Contains("After a break"));
            XElement cleared = blocks.First(b => BlockText(b).Contains("After a clear"));
            t.Check((string?)after.Attribute("PageBreak") == "Page" && (string?)cleared.Attribute("PageBreak") == "Clear",
                "\\newpage and \\clearpage break before the next block");
            t.Check((string?)cleared.Attribute("PageStyle") == "empty", "\\thispagestyle goes on the next block");
            XElement sub = blocks.First(b => BlockText(b).Contains("Sub"));
            t.Check((string?)sub.Attribute("MarkLeft") == "Left" && (string?)sub.Attribute("MarkRight") == "Right",
                "\\markboth in vertical mode marks the next block; a subsection under headings sets none of its own");

            XElement page = document.Descendants("Page").Single();
            List<XElement> styles = page.Elements("PageStyle").ToList();
            XElement headings = styles.Single(s => (string?)s.Attribute("Name") == "headings");
            t.Check((string?)page.Attribute("Style") == "headings" && styles.Any(s => (string?)s.Attribute("Name") == "empty"),
                $"the document's style and every \\thispagestyle are written: {string.Join(", ", styles.Select(s => (string?)s.Attribute("Name")))}");
            string Slot(XElement style, string place) => (string?)style.Elements("Slot").FirstOrDefault(s => (string?)s.Attribute("Place") == place)?.Attribute("Text") ?? "";
            t.Check(Slot(headings, "HeadLeft") == "{rightmark}" && Slot(headings, "HeadRight") == "{page}"
                && (string?)headings.Elements("Slot").First().Attribute("Italic") == "true",
                $"headings puts the slanted \\rightmark left and the page right: {Slot(headings, "HeadLeft")} | {Slot(headings, "HeadRight")}");

            TexPageRefs refs = TexLowering.PageRefs(document);
            int introBlock = blocks.IndexOf(heading);
            t.Check(refs.refs.Count == 2 && refs.labels.TryGetValue("sec:intro", out (int block, int offset) site) && site.block == introBlock,
                $"each \\pageref is a run to fill and the label stands at its heading: {refs.refs.Count} refs, {string.Join(", ", refs.labels.Select(l => $"{l.Key}@{l.Value}"))}");

            const string fancy = """
                \documentclass{article}
                \usepackage{fancyhdr}
                \pagestyle{fancy}
                \fancyhf{}
                \fancyhead[LE,RO]{\thepage}
                \fancyhead[LO]{\textbf{Draft}}
                \fancyfoot[C]{\leftmark}
                \renewcommand{\headrulewidth}{0.8pt}
                \fancypagestyle{plain}{\fancyhf{}\rfoot{p. \thepage}}
                \begin{document}
                \maketitle
                \section{Methods}
                \subsection{Setup}
                Text.
                \end{document}
                """;
            document = TexLowering.Compile(fancy, out errors);
            t.Check(errors.Count == 0, $"the fancyhdr sample compiles clean: {string.Join("; ", errors.Select(e => e.message))}");
            page = document.Descendants("Page").Single();
            XElement fancyStyle = page.Elements("PageStyle").Single(s => (string?)s.Attribute("Name") == "fancy");
            XElement plain = page.Elements("PageStyle").Single(s => (string?)s.Attribute("Name") == "plain");
            t.Check(Slot(fancyStyle, "HeadRight") == "{page}" && Slot(fancyStyle, "HeadLeft") == "Draft"
                && (string?)fancyStyle.Elements("Slot").First(s => (string?)s.Attribute("Place") == "HeadLeft").Attribute("Bold") == "true"
                && Slot(fancyStyle, "FootCenter") == "{leftmark}" && fancyStyle.Elements("Slot").Count() == 3,
                $"\\fancyhf clears, odd-page entries land, E-only ones are dropped: {string.Join(" | ", fancyStyle.Elements("Slot").Select(s => $"{(string?)s.Attribute("Place")}={(string?)s.Attribute("Text")}"))}");
            t.Check(MathF.Abs(((float?)fancyStyle.Attribute("HeadRule") ?? 0f) - 0.8f * 96f / 72.27f) < 0.01f, "\\headrulewidth sets the fancy head rule");
            t.Check(Slot(plain, "FootRight") == "p. {page}" && plain.Elements("Slot").Count() == 1, "\\fancypagestyle redefines plain");
            blocks = TopBlocks(document);
            t.Check((string?)blocks[0].Attribute("PageStyle") == "plain", "\\maketitle takes plain for its page");
            XElement methods = blocks.First(b => BlockText(b).Contains("Methods"));
            XElement setup = blocks.First(b => BlockText(b).Contains("Setup"));
            t.Check((string?)methods.Attribute("MarkLeft") == "1  METHODS" && (string?)methods.Attribute("MarkRight") == ""
                && (string?)setup.Attribute("MarkRight") == "1.1  Setup" && setup.Attribute("MarkLeft") == null,
                $"fancy's section marks both, its subsection marks right: {(string?)methods.Attribute("MarkLeft")} / {(string?)setup.Attribute("MarkRight")}");
            yield break;
        }

        [A_XSDActionDependency("Tex.PageBreak", "Test")]
        private static IEnumerator<int> PageBreaks(TestContext t)
        {
            XElement document = new XElement("Document",
                new XElement("Block", new XElement("Run", new XAttribute("Text", "first"))),
                new XElement("Block", new XAttribute("PageBreak", "Page"), new XAttribute("MarkRight", "R"), new XAttribute("PageStyle", "empty"),
                    new XElement("Run", new XAttribute("Text", "second page"))),
                new XElement("Block", new XAttribute("PageBreak", "Clear"), new XElement("Run", new XAttribute("Text", "third page"))),
                new XElement("Table", new XAttribute("PageBreak", "Page"), new XElement("Column", new XAttribute("Width", "100")),
                    new XElement("Row", new XElement("Cell", new XElement("Block", new XElement("Run", new XAttribute("Text", "cell")))))));
            RichTextDocument parsed = DocumentXml.Parse(document);
            List<XElement> written = TopBlocks(DocumentXml.ToXml(parsed));
            t.Check((string?)written[1].Attribute("PageBreak") == "Page" && (string?)written[1].Attribute("MarkRight") == "R"
                && (string?)written[1].Attribute("PageStyle") == "empty" && (string?)written[2].Attribute("PageBreak") == "Clear"
                && (string?)written[3].Attribute("PageBreak") == "Page" && written[0].Attribute("PageBreak") == null,
                "PageBreak, PageStyle and the marks round-trip through .xml, tables' breaks too");

            DocumentEditorControl editor = ShowDocument(t, document);
            yield return 2;
            t.Check(editor.PageAt(0, 0) == 1 && editor.PageAt(1, 0) == 2 && editor.PageAt(2, 0) == 3 && editor.PageAt(3, 0) == 4,
                $"each break starts a new page: {editor.PageAt(0, 0)} {editor.PageAt(1, 0)} {editor.PageAt(2, 0)} {editor.PageAt(3, 0)}");
            List<BlockControl> blocks = Content(editor).children.OfType<BlockControl>().ToList();
            BlockSnapshot snapshot = blocks[1].Snapshot();
            BlockControl tail = blocks[1].SplitAt(6);
            t.Check(tail.pageBreak == PageBreak.None && tail.markRight == null && tail.pageStyle == null
                && blocks[1].pageBreak == PageBreak.Page && snapshot.pageBreak == PageBreak.Page && snapshot.markRight == "R" && snapshot.pageStyle == "empty",
                "a split leaves the break, style and marks with the head; a snapshot carries them");
            tail.Destroy();
            blocks[1].Restore(snapshot);
            t.Show(new StackPanelControl());
        }

        [A_XSDActionDependency("Tex.Lowering.Footnote", "Test")]
        private static IEnumerator<int> LoweringFootnote(TestContext t)
        {
            const string source = """
                \documentclass{article}
                \begin{document}
                First\footnote{One note.} and second.\footnote{Two notes.}

                After.
                \end{document}
                """;
            XElement document = TexLowering.Compile(source, out List<TexError> errors);
            t.Check(errors.Count == 0, $"compiles clean: {string.Join("; ", errors.Select(e => e.message))}");
            List<XElement> top = document.Elements().Where(e => e.Name.LocalName is "Block" or "Footnote").ToList();
            t.Check(top.Count == 4 && top[0].Name.LocalName == "Block" && (string?)top[1].Attribute("Id") == "1" && (string?)top[2].Attribute("Id") == "2"
                && BlockText(top[3]).Contains("After"), $"each footnote follows the paragraph its mark is in: {string.Join(", ", top.Select(e => e.Name.LocalName))}");
            List<string?> anchors = top[0].Elements("Run").Select(r => (string?)r.Attribute("Note")).Where(n => n != null).ToList();
            t.Check(anchors.SequenceEqual(new[] { "1", "2" }), $"the marks in the text name their footnotes: {string.Join(",", anchors)}");
            XElement note = top[1].Elements("Block").Single();
            int small = (int)MathF.Round(8f * 96f / 72.27f);
            t.Check(BlockText(note).Contains("One note.") && note.Elements("Run").Any(r => (int?)r.Attribute("FontSize") == small)
                && note.Elements("Run").All(r => r.Attribute("Note") == null), $"a footnote is set in \\footnotesize and its own mark anchors nothing: {note}");
            t.Check(MathF.Abs(((float?)document.Descendants("Page").Single().Attribute("FootnoteSkip") ?? 0f) - 9f * 25.4f / 72.27f) < 0.01f, "\\skip\\footins is the page's footnote skip");

            RichTextDocument parsed = DocumentXml.Parse(document);
            List<NoteBlock> blocks = parsed.blocks.OfType<NoteBlock>().ToList();
            t.Check(blocks.Count == 4 && blocks[0].insert == null && blocks[1].insert?.footnote == "1" && blocks[2].insert?.footnote == "2" && blocks[3].insert == null,
                "a <Footnote> reads as its blocks with one PageInsert");
            XElement written = DocumentXml.ToXml(parsed);
            t.Check(written.Elements().Count(e => e.Name.LocalName == "Footnote") == 2
                && written.Elements().First(e => e.Name.LocalName == "Block").Elements().Count(r => (string?)r.Attribute("Note") != null) == 2,
                "footnotes and their anchors round-trip through .xml");

            BlockSnapshot snapshot = blocks[1].Snapshot();
            NoteBlock tail = blocks[1].SplitAt(3);
            t.Check(tail.insert == blocks[1].insert && snapshot.insert == blocks[1].insert, "Enter in a footnote makes another block of it; a snapshot keeps it");
            yield break;
        }

        [A_XSDActionDependency("Tex.Footnotes", "Test")]
        private static IEnumerator<int> Footnotes(TestContext t)
        {
            string filler = string.Join(" ", Enumerable.Repeat("lorem ipsum dolor sit", 13));
            string tall = string.Join(" ", Enumerable.Repeat("note text here", 12));
            XElement Para(string text, string? anchor = null, string? pageBreak = null)
            {
                XElement block = new XElement("Block", new XElement("Run", new XAttribute("Text", text)));
                if (anchor != null) block.Add(new XElement("Run", new XAttribute("Text", "*"), new XAttribute("Note", anchor)));
                if (pageBreak != null) block.SetAttributeValue("PageBreak", pageBreak);
                return block;
            }
            XElement Note(string id, string text) => new XElement("Footnote", new XAttribute("Id", id), Para(text));

            XElement document = new XElement("Document",
                new XElement("DocumentLayout", new XAttribute("BlockSpacing", "0"),
                    new XElement("TextStyle", new XAttribute("Type", "Text"), new XAttribute("FontSize", "18")),
                    new XElement("Page", new XAttribute("Mode", "Paged"), new XAttribute("Size", "A6"))),
                Para("intro", "a"), Note("a", "note a"),
                Para("more", "b"), Note("b", "note b"),
                Para(filler, "c", "Page"), Note("c", tall),
                Note("z", "orphan"));
            DocumentEditorControl editor = ShowDocument(t, document);
            yield return 2;

            List<Control> blocks = Enumerable.Range(0, editor.session.document.blocks.Length).Select(i => View(editor, i)).ToList();
            BlockControl a = (BlockControl)blocks[1], b = (BlockControl)blocks[3], filled = (BlockControl)blocks[4], c = (BlockControl)blocks[5], z = (BlockControl)blocks[6];
            t.Check(editor.PageAt(1, 0) == 1 && editor.PageAt(3, 0) == 1, $"footnotes a and b sit on their anchors' page: {editor.PageAt(1, 0)} {editor.PageAt(3, 0)}");
            t.Check(b.arrangedRect.y >= a.arrangedRect.Bottom - 0.5f, "two footnotes on a page stack in anchor order");

            DocumentControl content = (DocumentControl)a.parent;
            PanelControl sheet = content.children.OfType<PanelControl>().First(p => p.children.Count == 1 && p.children[0] is ContainerControl);
            float textBottom = sheet.arrangedRect.Bottom - 25.4f * PageLayout.PxPerMm * content.zoom;
            t.Check(MathF.Abs(b.arrangedRect.Bottom - textBottom) < 1f && a.arrangedRect.y > ((BlockControl)blocks[2]).arrangedRect.Bottom + 10f,
                $"footnotes end at the foot of the text area, under a gap: b ends {b.arrangedRect.Bottom}, text area ends {textBottom}");
            Control rule = (Control)((Control)sheet.children[0]).children[8];
            t.Check(rule.arrangedRect.width > 0f && rule.arrangedRect.Bottom <= a.arrangedRect.y, $"a short rule sits above the footnotes: {rule.arrangedRect.width}");

            t.Check(filled.Lines.Count == 13 && editor.PageAt(4, 0) == 2 && editor.PageAt(4, filler.Length) == 3 && editor.PageAt(5, 0) == 3,
                $"a line whose footnote does not fit goes to the next page with it: {filled.Lines.Count} lines, first on {editor.PageAt(4, 0)}, anchor line {editor.PageAt(4, filler.Length)}, note {editor.PageAt(5, 0)}");
            t.Check(editor.PageAt(6, 0) == 3 && z.arrangedRect.y >= c.arrangedRect.Bottom - 0.5f, $"a footnote no anchor names goes at the foot of the last page: {editor.PageAt(6, 0)}");
            t.Check(filled.Lines.All(l => l.top + l.height + filled.arrangedRect.y <= c.arrangedRect.y || l.top + filled.arrangedRect.y >= c.arrangedRect.Bottom),
                "no line of text runs into a footnote");

            XElement huge = new XElement("Document",
                new XElement("DocumentLayout", new XAttribute("BlockSpacing", "0"),
                    new XElement("Page", new XAttribute("Mode", "Paged"), new XAttribute("Size", "A6"))),
                Para("before"), Para("anchor", "h"), Note("h", string.Join(" ", Enumerable.Repeat("note text here", 40))));
            editor = ShowDocument(t, huge);
            yield return 2;
            BlockControl anchor = (BlockControl)View(editor, 1);
            BlockControl over = (BlockControl)View(editor, 2);
            t.Check(over.arrangedRect.y >= anchor.arrangedRect.Bottom, $"a footnote taller than the page starts below its anchor's line: {over.arrangedRect.y} vs {anchor.arrangedRect.Bottom}");
            t.Show(new StackPanelControl());
        }

        [A_XSDActionDependency("Tex.Lowering.Float", "Test")]
        private static IEnumerator<int> LoweringFloat(TestContext t)
        {
            const string source = """
                \documentclass[12pt]{article}
                \begin{document}
                Before.

                \vspace{10pt}
                \begin{figure}[!t]\centering x\caption{A}\end{figure}
                After, with \begin{table}[b]\caption{B}\end{table} a table inside.
                \end{document}
                """;
            XElement document = TexLowering.Compile(source, out List<TexError> errors);
            t.Check(errors.Count == 0, $"compiles clean: {string.Join("; ", errors.Select(e => e.message))}");
            List<XElement> top = document.Elements().Where(e => e.Name.LocalName is "Block" or "Float").ToList();
            t.Check(top.Select(e => e.Name.LocalName).SequenceEqual(new[] { "Block", "Float", "Block", "Float" }),
                $"a float stands where it was set, or after the paragraph it is in: {string.Join(", ", top.Select(e => e.Name.LocalName))}");
            t.Check((string?)top[1].Attribute("Kind") == "figure" && (string?)top[1].Attribute("Placement") == "!t"
                && (string?)top[3].Attribute("Kind") == "table" && (string?)top[3].Attribute("Placement") == "b", "<Float> carries the kind and placement");
            t.Check(top[1].Elements("Block").Count() == 2 && BlockText(top[1].Elements("Block").Last()).Contains("Figure 1: A"), "the float holds its body and caption");
            t.Check(top[2].Attribute("SpaceBefore") != null && top[1].Elements("Block").First().Attribute("SpaceBefore") == null,
                "the space above a float stays with the next block in the text");
            XElement page = document.Descendants("Page").Single();
            float Mm(string name) => (float?)page.Attribute(name) ?? 0f;
            const float mmPerPt = 25.4f / 72.27f;
            t.Check(MathF.Abs(Mm("FloatSep") - 14f * mmPerPt) < 0.01f && MathF.Abs(Mm("TextFloatSep") - 20f * mmPerPt) < 0.01f && MathF.Abs(Mm("InTextSep") - 14f * mmPerPt) < 0.01f,
                $"the class size sets the float separations: {Mm("FloatSep")} {Mm("TextFloatSep")} {Mm("InTextSep")}");

            RichTextDocument parsed = DocumentXml.Parse(document);
            NoteNode[] blocks = parsed.blocks;
            PageInsert? figure = (blocks[1] as NoteBlock)?.insert;
            t.Check(figure is { floatKind: "figure", placement: "!t" } && (blocks[2] as NoteBlock)?.insert == figure && (blocks[3] as NoteBlock)?.insert == null,
                "a <Float> reads as its blocks with one PageInsert");
            XElement written = DocumentXml.ToXml(parsed);
            t.Check(written.Elements().Count(e => e.Name.LocalName == "Float") == 2, "floats round-trip through .xml");
            yield break;
        }

        [A_XSDActionDependency("Tex.FloatPlacement", "Test")]
        private static IEnumerator<int> FloatPlacement(TestContext t)
        {
            string filler = string.Join(" ", Enumerable.Repeat("lorem ipsum dolor sit", 40));
            XElement Para(string text, string? pageBreak = null, string? anchor = null)
            {
                XElement block = new XElement("Block", new XElement("Run", new XAttribute("Text", text)));
                if (anchor != null) block.Add(new XElement("Run", new XAttribute("Text", "*"), new XAttribute("Note", anchor)));
                if (pageBreak != null) block.SetAttributeValue("PageBreak", pageBreak);
                return block;
            }
            XElement Float(string placement, float tall = 0f, string kind = "figure") => new XElement("Float", new XAttribute("Kind", kind), new XAttribute("Placement", placement),
                Para("float " + placement), new XElement("Block", new XAttribute("SpaceBefore", tall), new XElement("Run", new XAttribute("Text", "end"))));
            XElement Document(string mode, params XElement[] content) => new XElement("Document",
                new XElement("DocumentLayout", new XAttribute("BlockSpacing", "0"),
                    new XElement("Page", new XAttribute("Mode", mode), new XAttribute("Size", "A6"))), content);
            LayoutRect Rect(DocumentEditorControl e, int block) => View(e, block).arrangedRect;
            string Span(DocumentEditorControl e, int block) => $"{Rect(e, block).y:0.#}..{Rect(e, block).Bottom:0.#}";

            DocumentEditorControl editor = ShowDocument(t, Document("Paged", Para("a"), Float("ht"), Para("b")));
            yield return 2;
            t.Check(Rect(editor, 1).y > Rect(editor, 0).Bottom && Rect(editor, 3).y > Rect(editor, 2).Bottom && editor.PageAt(1, 0) == 1,
                "h: a float that fits is set in the text where it stands");

            editor = ShowDocument(t, Document("Paged", Para("a"), Para("b"), Float("tbp"), Para("c")));
            yield return 2;
            t.Check(editor.PageAt(2, 0) == 1 && Rect(editor, 3).Bottom < Rect(editor, 0).y && Rect(editor, 4).y >= Rect(editor, 1).Bottom - 0.5f,
                $"t: a float met mid-page goes to that page's top and the text above it moves down: float {Span(editor, 2)} {Span(editor, 3)}, a {Span(editor, 0)}, b {Span(editor, 1)}, c {Span(editor, 4)}");
            Control? header = Content(editor).header;
            t.Check(header != null && Rect(editor, 2).y >= header.arrangedRect.Bottom - 0.5f,
                $"a top float on the first page sits under the note's header: float {Rect(editor, 2).y}, header ends {header?.arrangedRect.Bottom}");

            editor = ShowDocument(t, Document("Paged", Para("a", anchor: "n"), new XElement("Footnote", new XAttribute("Id", "n"), Para("note")), Float("b"), Para("c")));
            yield return 2;
            DocumentControl content = Content(editor);
            PanelControl sheet = content.children.OfType<PanelControl>().First(p => p.children.Count == 1 && p.children[0] is ContainerControl);
            float textBottom = sheet.arrangedRect.Bottom - 25.4f * PageLayout.PxPerMm * content.zoom;
            t.Check(editor.PageAt(2, 0) == 1 && MathF.Abs(Rect(editor, 3).Bottom - textBottom) < 1f && Rect(editor, 1).Bottom <= Rect(editor, 2).y && Rect(editor, 4).Bottom < Rect(editor, 1).y,
                $"b: a float goes to the foot of the page, under the footnotes: float ends {Rect(editor, 3).Bottom}, text area {textBottom}, note ends {Rect(editor, 1).Bottom}");

            float height = sheet.arrangedRect.height / content.zoom - 2f * 25.4f * PageLayout.PxPerMm;
            editor = ShowDocument(t, Document("Paged", Para("a"), Float("tbp", 0.8f * height), Float("t", 10f), Float("t", 10f, "table"), Para(filler), Para(filler)));
            yield return 2;
            t.Check(editor.PageAt(1, 0) == 2 && editor.PageAt(3, 0) == 3, $"a float too tall for a text page waits for a page of floats; the next figure may not pass it: {editor.PageAt(1, 0)} {editor.PageAt(3, 0)}");
            t.Check(editor.PageAt(5, 0) == 1 && Rect(editor, 6).Bottom < Rect(editor, 0).y, $"a table is not held back by a waiting figure: page {editor.PageAt(5, 0)}");
            BlockControl text = (BlockControl)View(editor, 7);
            t.Check(editor.PageAt(8, 0) > 2 && Enumerable.Range(0, text.Length).All(i => editor.PageAt(7, i) != 2), "no text goes on a page of floats");

            editor = ShowDocument(t, Document("Paged", Para("a"), Float("p", 10f), Para("b", "Clear"), Para("c")));
            yield return 2;
            t.Check(editor.PageAt(1, 0) == 2 && editor.PageAt(3, 0) == 3, $"\\clearpage puts out the waiting floats before it breaks: float {editor.PageAt(1, 0)}, b {editor.PageAt(3, 0)}");

            editor = ShowDocument(t, Document("Paged", Para("a"), Float("p", 10f), Para("b")));
            yield return 2;
            t.Check(editor.PageAt(1, 0) == 2 && editor.PageAt(3, 0) == 1, $"the document's end puts out what still waits: float {editor.PageAt(1, 0)}");

            editor = ShowDocument(t, Document("Paged", Para("a"), Float("H", 10f), Para("b")));
            yield return 2;
            t.Check(Rect(editor, 1).y > Rect(editor, 0).Bottom && Rect(editor, 3).y > Rect(editor, 2).Bottom, "H: set here, always");

            editor = ShowDocument(t, Document("Paged", Para("a"), Float("!t", 0.6f * height), Para("b")));
            yield return 2;
            t.Check(editor.PageAt(1, 0) == 1 && Rect(editor, 2).Bottom < Rect(editor, 0).y, $"!: a float past \\topfraction still goes at the top: page {editor.PageAt(1, 0)}, float {Span(editor, 1)} {Span(editor, 2)}, a {Span(editor, 0)}, height {height}");
            editor = ShowDocument(t, Document("Paged", Para("a"), Float("t", 0.6f * height), Para("b")));
            yield return 2;
            t.Check(editor.PageAt(1, 0) == 2, $"without !, \\topfraction holds it back: page {editor.PageAt(1, 0)}");

            editor = ShowDocument(t, Document("Pageless", Para("a"), Float("tp", 10f), Para("b")));
            yield return 2;
            t.Check(Rect(editor, 1).y > Rect(editor, 0).Bottom && Rect(editor, 3).y > Rect(editor, 2).Bottom, "pageless: every float is set where it stands");
            t.Show(new StackPanelControl());
        }

        [A_XSDActionDependency("Tex.RunningHeads", "Test")]
        private static IEnumerator<int> RunningHeads(TestContext t)
        {
            string words = string.Join(" ", Enumerable.Repeat("lorem ipsum dolor", 4));
            XElement Para(string text) => new XElement("Block", new XElement("Run", new XAttribute("Text", text)));
            XElement Heading(string text, string? left, string? right)
            {
                XElement block = Para(text);
                if (left != null) block.SetAttributeValue("MarkLeft", left);
                if (right != null) block.SetAttributeValue("MarkRight", right);
                return block;
            }
            XElement Slot(string place, string text, bool italic = false) => new XElement("Slot", new XAttribute("Place", place), new XAttribute("Text", text),
                new XAttribute("FontSize", "12"), new XAttribute("Italic", italic ? "true" : "false"));

            XElement document = new XElement("Document",
                new XElement("DocumentLayout",
                    new XElement("Page", new XAttribute("Mode", "Paged"), new XAttribute("Size", "A6"), new XAttribute("Style", "heads"),
                        new XElement("PageStyle", new XAttribute("Name", "heads"), new XAttribute("HeadRule", "1"),
                            Slot("HeadLeft", "{leftmark} / {rightmark}", true), Slot("HeadRight", "{page}")),
                        new XElement("PageStyle", new XAttribute("Name", "plain"), Slot("FootCenter", "- {page} -")))),
                new XElement("Block", new XAttribute("PageStyle", "plain"), new XElement("Run", new XAttribute("Text", "Title"))),
                Heading("One", "ONE", ""),
                Para(words),
                Heading("Sub A", null, "A"),
                Para(words),
                new XElement("Block", new XAttribute("PageBreak", "Page"), new XElement("Run", new XAttribute("Text", "no marks here"))),
                new XElement("Block", new XAttribute("PageBreak", "Page"), new XElement("Run", new XAttribute("Text", "two marks"))),
                Heading("Sub B", null, "B"),
                Heading("Two", "TWO", ""));
            RichTextDocument parsed = DocumentXml.Parse(document);
            XElement page = DocumentXml.ToXml(parsed).Descendants().Single(e => e.Name.LocalName == "Page");
            t.Check(page.Elements().Count(e => e.Name.LocalName == "PageStyle") == 2 && (string?)page.Attribute("Style") == "heads"
                && page.Elements().First().Elements().Count() == 2, "page styles and their slots round-trip through .xml");

            DocumentEditorControl editor = ShowDocument(t, document);
            yield return 2;
            List<string[]> margins = Margins(editor);
            t.Check(margins.Count >= 3, $"three pages: {margins.Count}");
            if (margins.Count < 3)
            {
                t.Show(new StackPanelControl());
                yield break;
            }
            t.Check(margins[0][0] == "" && margins[0][2] == "" && margins[0][4] == "- 1 -", $"page 1 takes its block's plain: {string.Join("|", margins[0])}");
            t.Check(margins[1][0] == "ONE / A" && margins[1][2] == "2", $"page 2 carries the last marks over: {string.Join("|", margins[1])}");
            t.Check(margins[2][0] == "TWO / B" && margins[2][2] == "3",
                $"page 3: \\leftmark is the last mark's left, \\rightmark the first mark's right: {string.Join("|", margins[2])}");
            t.Show(new StackPanelControl());
        }
        #endregion
    }
}
