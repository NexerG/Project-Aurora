using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Testing;
using ArctisAurora.Core.Tex;
using ArctisAurora.Core.UI;
using System.Text;
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
                bool latex = document.blocks.OfType<BlockControl>().All(b => b.stylingType == TextStyleType.Code && b.language == "latex");
                document.Save(to);
                byte[] back = File.ReadAllBytes(to);
                foreach (Control block in document.blocks)
                    block.Destroy();
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

        private static List<TexParagraph> Pars(TexTypesetter typesetter) => typesetter.vlist.OfType<TexParagraph>().ToList();

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
            t.Check(Lines(@"\begin{equation}E=mc^2\end{equation}") == "$$E=mc^2$$", "an equation is display math");

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
                new XElement("Block", new XAttribute("StylingType", "Heading1"),
                    new XElement("Run", new XAttribute("Text", "1\u00A0\u00A0\u00A0Intro"), new XAttribute("Bold", "true"))),
                new XElement("Block", new XAttribute("Align", "Justify"),
                    new XElement("Run", new XAttribute("Text", "Some ")),
                    new XElement("Run", new XAttribute("Text", "bold"), new XAttribute("Bold", "true")),
                    new XElement("Run", new XAttribute("Text", " text ")),
                    new XElement("Run", new XAttribute("Math", "x^2"), new XAttribute("FontName", "latin-modern-math")),
                    new XElement("Run", new XAttribute("Text", "."))),
                new XElement("Block", new XAttribute("Align", "Justify"), new XAttribute("List", "Bullet"), new XAttribute("Level", 0),
                    new XAttribute("Marker", "Disc"), new XElement("Run", new XAttribute("Text", "One")))
            };
            string body = string.Concat(document.Elements("Block").Select(b => b.ToString(SaveOptions.DisableFormatting)));
            string want = string.Concat(expected.Select(b => b.ToString(SaveOptions.DisableFormatting)));
            t.Check(body == want, $"blocks and runs: {body}");
            RichTextDocument parsed = DocumentXml.Parse(document);
            t.Check(parsed.blocks.Count == 3, "the tree parses as a note");
            foreach (Control block in parsed.blocks)
                block.Destroy();
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
            t.Check(lines.Count == document.Elements("Block").Count(), $"one line per block: {lines.Count}");
            t.Check(string.Join(",", lines) == "3,4,4,0,8,10,11,0,8", $"heading, both halves of \\\\, rule, note, verbatim lines, Notes heading, endnote: {string.Join(",", lines)}");
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
            t.Check(table.columns.Select(c => c.align).SequenceEqual(new[] { TexAlign.Left, TexAlign.Center, TexAlign.Right }) && table.ruled,
                "l c r with rules");
            string rows = string.Join(" | ", table.rows.Select(r => string.Join(",", r.Select(c => CellText(c) + (c.span > 1 ? "*" + c.span : "")))));
            t.Check(rows == "a,b,c | wide*2,d | e,f", $"rows and cells: {rows}");
            t.Check(table.rows[1][0].vlist.OfType<TexParagraph>().Single().style.align == TexAlign.Center, "a multicolumn takes its own alignment");
            t.Check(table.rows[2][1].vlist.OfType<TexParagraph>().Single().style.align == TexAlign.Center, "a cell takes its column's alignment");
            t.Check(table.rows[0][1].vlist.OfType<TexParagraph>().Single().list.OfType<TexChar>().Single().style.font.bold
                && !table.rows[0][2].vlist.OfType<TexParagraph>().Single().list.OfType<TexChar>().Single().style.font.bold,
                "a cell's \\bf ends at the next &");

            t.Check(!Typeset(@"\begin{tabular}{ll}a&b\end{tabular}", out _).vlist.OfType<TexTable>().Single().ruled, "no | and no rule is unruled");
            t.Check(Typeset(@"\begin{tabular}{ll}\toprule a&b\\\cmidrule(lr){1-2} c&d\\\bottomrule\end{tabular}", out errors).vlist.OfType<TexTable>().Single() is { ruled: true, rows.Count: 2 }
                && errors.Count == 0, "booktabs rules mark it ruled");

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
            t.Check(math.Any(m => m.source == @"x = \text{2}") && math.Any(m => m.source == "a"), $"\\ref in math resolved, \\label dropped: {string.Join(" | ", math.Select(m => m.source))}");

            Typeset(@"\section{A}\label{x}\section{B}\label{x}", out errors);
            t.Check(Has(errors, "Label `x' multiply defined"), "a label defined twice");
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
            TableControl control = (TableControl)parsed.blocks[1];
            t.Check(!control.showBorders && control.ColumnSpan(control.Cells()[2]) == 2, "it parses as a borderless table with a merged cell");
            foreach (Control block in parsed.blocks)
                block.Destroy();
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
    }
}
