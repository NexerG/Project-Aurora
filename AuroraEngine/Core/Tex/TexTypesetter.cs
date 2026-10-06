using ArctisAurora.Core.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ArctisAurora.Core.Tex
{
    // TeX's stomach for a LaTeX document: turns the expander's tokens into paragraphs of characters, glue and math.
    public sealed class TexTypesetter
    {
        private static readonly LogChannel Log = LogChannel.For("Tex");

        private const int Unity = 65536;

        private enum Mode { Vertical, Horizontal }

        private sealed class ListFrame
        {
            public readonly TexListKind kind;
            public readonly ListFrame? parent;
            public readonly int depth;
            public readonly int kindDepth;
            public int count;
            public bool started;

            public ListFrame(TexListKind kind, ListFrame? parent)
            {
                this.kind = kind;
                this.parent = parent;
                depth = (parent?.depth ?? 0) + 1;
                kindDepth = 1;
                for (ListFrame? p = parent; p != null; p = p.parent)
                    if (p.kind == kind) kindDepth++;
            }
        }

        // what a heading or footnote sets aside while its text is typeset
        private readonly record struct Frame(Mode mode, TexParagraph? par, List<TexNode> vtarget, TexStyle style,
            ListFrame? list, TexAlign align, bool quote);

        // a tabular being read: the open row and cell, and the style each cell starts from
        private sealed class TableBuild
        {
            public readonly TexTable table;
            public readonly TexStyle style;
            public List<TexTableCell>? row;
            public TexTableCell? cell;
            public int column;

            public TableBuild(TexTable table, TexStyle style)
            {
                this.table = table;
                this.style = style;
            }
        }

        private sealed class FixedMetrics : ITexFontMetrics
        {
            public int Quad(TexFont font) => font.size;

            public int XHeight(TexFont font) => (int)(font.size * 0.430554);
        }

        // LaTeX's size commands at the 10pt, 11pt and 12pt class options
        private static readonly string[] sizeNames = { "tiny", "scriptsize", "footnotesize", "small", "normalsize", "large", "Large", "LARGE", "huge", "Huge" };
        private static readonly float[][] sizePoints =
        {
            new[] { 5f, 7f, 8f, 9f, 10f, 12f, 14.4f, 17.28f, 20.74f, 24.88f },
            new[] { 6f, 8f, 9f, 10f, 10.95f, 12f, 14.4f, 17.28f, 20.74f, 24.88f },
            new[] { 6f, 8f, 10f, 10.95f, 12f, 14.4f, 17.28f, 20.74f, 24.88f, 24.88f }
        };
        private const int NormalSize = 4;

        private static readonly string[] sectionNames = { "chapter", "section", "subsection", "subsubsection", "paragraph", "subparagraph" };

        // packages whose commands are here, or that need none
        private static readonly HashSet<string> knownPackages = new HashSet<string>
        {
            "amsmath", "amssymb", "amsfonts", "geometry", "xcolor", "color", "hyperref", "url",
            "inputenc", "fontenc", "lmodern", "textcomp", "babel", "microtype", "graphicx", "graphics", "booktabs", "fancyhdr"
        };

        // article's \textwidth at 10pt, 11pt, 12pt and its \textheight, pt
        public static readonly float[] textWidths = { 345f, 360f, 390f };
        public const float TextHeight = 550f;
        private const float MmPerPt = 25.4f / 72.27f;

        // the standard classes' spacing at 10pt, 11pt, 12pt, pt: \parindent, \topsep, \itemsep (= \parsep), \intextsep (= \floatsep), \abovedisplayskip
        private static readonly float[] parIndents = { 15f, 17f, 18f };
        private static readonly float[] topSeps = { 8f, 9f, 10f };
        private static readonly float[] itemSeps = { 4f, 4.5f, 5f };
        public static readonly float[] floatSeps = { 12f, 12f, 14f };
        public const float TextFloatSep = 20f;
        public static readonly float[] displaySkips = { 10f, 11f, 12f };
        public static readonly float[] footnoteSkips = { 9f, 10f, 10.8f };

        private static readonly string[] rasterExtensions = { ".png", ".jpg", ".jpeg" };
        private static readonly string[] vectorExtensions = { ".pdf", ".eps", ".ps" };

        private static readonly Regex mathLabel = new Regex(@"\\label\s*\{([^}]*)\}");
        private static readonly Regex mathRef = new Regex(@"\\ref\s*\{([^}]*)\}");
        private static readonly Regex mathEqref = new Regex(@"\\eqref\s*\{([^}]*)\}");
        private static readonly Regex mathTag = new Regex(@"\\tag\s*(\*?)\s*\{([^}]*)\}");
        private static readonly Regex mathNotag = new Regex(@"\\(?:notag|nonumber)(?![a-zA-Z])\s*");

        // amsmath environments that live inside math, written back into the formula's source
        private static readonly HashSet<string> mathEnvironments = new HashSet<string>
        {
            "matrix", "pmatrix", "bmatrix", "Bmatrix", "vmatrix", "Vmatrix", "smallmatrix", "cases", "array",
            "aligned", "alignedat", "gathered", "split"
        };

        public readonly TexExpander expander;
        public List<TexError> errors => expander.errors;

        // the main vertical list, and every \footnote in order
        public readonly List<TexNode> vlist = new List<TexNode>();
        public readonly List<TexFootnote> footnotes = new List<TexFootnote>();

        // \hyphenation words
        public readonly Dictionary<string, bool[]> hyphenation = new Dictionary<string, bool[]>();

        // page styles: the document's, and every style a page can take, typeset once the document is read
        public string pageStyle = "plain";
        public readonly Dictionary<string, TexPageStyle> pageStyles = new Dictionary<string, TexPageStyle>();

        // what \thepage, \leftmark and \rightmark leave in a page style's slot
        public const char PageField = '';
        public const char LeftMarkField = '';
        public const char RightMarkField = '';

        // document class and page
        public string documentClass = "article";
        public int sizeOption;
        public string paper = "letter";
        public float? marginTop, marginBottom, marginLeft, marginRight;

        // \label key -> its text, cited keys in citation order, \bibitem key -> its label
        public readonly Dictionary<string, string> labels = new Dictionary<string, string>();
        public readonly List<string> citations = new List<string>();
        public readonly Dictionary<string, string> bibLabels = new Dictionary<string, string>();

        private readonly ITexFontMetrics metrics;
        private readonly string? folder;
        private readonly Dictionary<string, Action<TexToken>> commands = new Dictionary<string, Action<TexToken>>();
        private readonly Stack<Frame> frames = new Stack<Frame>();

        // the state the save stack restores
        private TexStyle style;
        private ListFrame? list;
        private TexAlign align;
        private bool quote;
        private TexHBox? box;

        private Mode mode;
        private TexParagraph? par;
        private List<TexNode> vtarget;

        // what stops the next paragraph's indent: \noindent, a heading until text, an environment's end until a blank line
        private bool noIndent, afterHeading, afterEnvironment;

        // the skip under the heading being set, sp
        private float headingAfter;
        private bool inDocument = true;
        private bool done;
        private bool itemPending;
        private bool boundary;
        private bool suppressSpace;
        private bool warnedSmallCaps;

        // last source line a dispatched token came from
        private int sourceLine;

        // references resolved once the document has been read
        private readonly List<TexRefNode> refs = new List<TexRefNode>();
        private readonly List<(TexMathNode node, int line)> mathRefs = new List<(TexMathNode, int)>();
        private string currentLabel = "";

        // floats, pictures and tabulars, innermost tabular last
        private string? floatKind;
        private TexFloat? openFloat;
        private List<string> graphicsPath = new List<string> { "" };
        private readonly List<TableBuild> tables = new List<TableBuild>();
        private readonly List<(List<TexNode> target, TexTable table)> unaligned = new List<(List<TexNode>, TexTable)>();

        // bibliography: \bibliographystyle, \bibliography's files and where it stood
        private string bibStyle = "";
        private string[] bibFiles = Array.Empty<string>();
        private (List<TexNode> list, TexBibliographyMark mark)? bibMark;
        private bool allCited;
        private int bibCount;

        // page style slot sources by style and place, the styles fancyhdr defined, the one \fancyhead writes to, the styles used,
        // what the next paragraph takes, the numbered heading whose marks are pending, and whether a slot is being typeset
        private readonly Dictionary<string, Dictionary<string, TexToken[]>> styleSlots = new Dictionary<string, Dictionary<string, TexToken[]>>();
        private readonly HashSet<string> fancyStyles = new HashSet<string> { "fancy" };
        private string fancyTarget = "fancy";
        private readonly HashSet<string> usedStyles = new HashSet<string>();
        private string? pendingStyle, pendingMarkLeft, pendingMarkRight;
        private (int depth, string number)? headingMark;
        private bool inSlot;

        private List<TexNode> HList => box?.list ?? par!.list;

        // \normalsize at the class option, sp
        public int normalSize => Size(NormalSize);
        public float parIndent => parIndents[sizeOption];

        private bool Chapters => documentClass is "report" or "book";

        // paper, mm
        public float paperWidth => paper == "a4" ? 210f : 215.9f;
        public float paperHeight => paper == "a4" ? 297f : 279.4f;

        // \textwidth and \textheight, sp: the class's, or the paper less geometry's margins
        public int textWidth => Extent(paperWidth, marginLeft, marginRight, textWidths[sizeOption]);
        public int textHeight => Extent(paperHeight, marginTop, marginBottom, TextHeight);

        // folder is where pictures and .bib files are looked for; null finds none.
        public TexTypesetter(string source, ITexFontMetrics? metrics = null, string? folder = null)
        {
            this.metrics = metrics ?? new FixedMetrics();
            this.folder = folder;
            vtarget = vlist;
            style = new TexStyle(new TexFont(TexFamily.Roman, false, false, Size(NormalSize)), null, false);
            expander = new TexExpander(source, TexFormat.Prelude);
            expander.quad = () => this.metrics.Quad(style.font);
            expander.xHeight = () => this.metrics.XHeight(style.font);
            Register();
            DefaultPageStyles();
        }

        public void Run()
        {
            while (!done && expander.Next(out TexToken t)) Dispatch(t);
            EndParagraph();
            TypesetBibliography();
            TypesetPageStyles();
            Resolve();
        }

        private void Dispatch(TexToken t)
        {
            if (t.line > 0) sourceLine = t.line;
            if (!t.IsCs) Character(t);
            else if (commands.TryGetValue(t.name!, out Action<TexToken>? command)) command(t);
        }

        #region ---- commands ----
        private void Register()
        {
            void On(string name, Action<TexToken> action)
            {
                expander.DefinePrimitive(name);
                commands[name] = action;
            }

            On("documentclass", DocumentClass);
            On("usepackage", UsePackage);
            On("geometry", t => Geometry(expander.Argument(t, true)));
            On("document", _ =>
            {
                inDocument = true;
                PageDimens();
            });
            On("@pagedimens", _ => PageDimens());
            On("enddocument", _ =>
            {
                EndParagraph();
                done = true;
            });
            On("today", _ => AddText(Today()));

            for (int i = 0; i < sectionNames.Length; i++)
            {
                int depth = i;
                On(sectionNames[i], t => Section(t, depth));
            }
            On("@endhead", _ =>
            {
                Resume();
                if (headingMark is (int depth, string number) && vtarget.Count > 0 && vtarget[^1] is TexParagraph heading)
                    HeadingMarks(heading, depth, number);
                headingMark = null;
                VSkip(headingAfter);
                afterHeading = true;
            });
            On("@runin", _ =>
            {
                Space(1f);
                suppressSpace = true;
            });

            On("bfseries", _ => SetFont(style.font with { bold = true }));
            On("mdseries", _ => SetFont(style.font with { bold = false }));
            On("itshape", _ => SetFont(style.font with { italic = true }));
            On("slshape", _ => SetFont(style.font with { italic = true }));
            On("upshape", _ => SetFont(style.font with { italic = false }));
            On("scshape", _ =>
            {
                if (!warnedSmallCaps) Log.Info($"small caps are set upright: Latin Modern has no small-caps face here");
                warnedSmallCaps = true;
                SetFont(style.font with { italic = false });
            });
            On("rmfamily", _ => SetFont(style.font with { family = TexFamily.Roman }));
            On("sffamily", _ => SetFont(style.font with { family = TexFamily.Sans }));
            On("ttfamily", _ => SetFont(style.font with { family = TexFamily.Mono }));
            On("normalfont", _ => SetFont(style.font with { family = TexFamily.Roman, bold = false, italic = false }));
            On("em", _ => SetFont(style.font with { italic = !style.font.italic }));
            for (int i = 0; i < sizeNames.Length; i++)
            {
                int size = i;
                On(sizeNames[i], _ => SetFont(style.font with { size = Size(size) }));
            }
            On("@underline", _ => SetStyle(style with { underline = true }));
            On("color", Color);

            On("itemize", _ => BeginList(TexListKind.Itemize));
            On("enumerate", _ => BeginList(TexListKind.Enumerate));
            On("description", _ => BeginList(TexListKind.Description));
            foreach (string name in new[] { "enditemize", "endenumerate", "enddescription" })
                On(name, _ => EndEnvironment(list?.depth ?? 1));
            foreach (string name in new[] { "endquote", "endquotation", "endcenter", "endflushleft", "endflushright" })
                On(name, _ => EndEnvironment((list?.depth ?? 0) + 1));
            On("item", Item);
            On("@label", _ =>
            {
                Interword();
                suppressSpace = true;
            });
            On("quote", _ => BeginQuote());
            On("quotation", _ => BeginQuote());
            On("center", _ => BeginAligned(TexAlign.Center));
            On("flushleft", _ => BeginAligned(TexAlign.Left));
            On("flushright", _ => BeginAligned(TexAlign.Right));
            On("centering", _ => SetAlign(TexAlign.Center));
            On("raggedright", _ => SetAlign(TexAlign.Left));
            On("raggedleft", _ => SetAlign(TexAlign.Right));
            On("verbatim", Verbatim);
            On("verb", Verb);

            On("(", _ => MathUntil(TexToken.Cs(")"), false));
            On("[", _ => MathUntil(TexToken.Cs("]"), true));
            On(")", _ => expander.Error("LaTeX Error: Bad math environment delimiter"));
            On("]", _ => expander.Error("LaTeX Error: Bad math environment delimiter"));
            foreach ((string env, bool display) in new[] { ("displaymath", true), ("math", false) })
            {
                On(env, _ => MathUntil(TexToken.Cs("end" + env), display));
                On("end" + env, _ => { });
            }
            foreach (string env in new[] { "equation", "equation*", "align", "align*", "gather", "gather*", "flalign", "flalign*",
                         "alignat", "alignat*", "multline", "multline*" })
            {
                On(env, _ => MathUntil(TexToken.Cs("end" + env), true, env));
                On("end" + env, _ => { });
            }
            foreach (string env in mathEnvironments)
            {
                On(env, _ => expander.Error("Missing $ inserted"));
                On("end" + env, _ => { });
            }
            On("eqref", t =>
            {
                AddText("(");
                Reference(t, false);
                AddText(")");
            });
            On("footnote", Footnote);
            On("@endnote", _ => Resume());

            On("label", Label);
            On("ref", t => Reference(t, false));
            On("pageref", t => Reference(t, false, true));

            On("newpage", _ => PageBreak(false));
            On("clearpage", _ => PageBreak(true));
            On("pagestyle", t => SetPageStyle(t, false));
            On("thispagestyle", t => SetPageStyle(t, true));
            On("markboth", t => Mark(t, true));
            On("markright", t => Mark(t, false));
            On("thepage", _ => Field(PageField));
            On("leftmark", _ => Field(LeftMarkField));
            On("rightmark", _ => Field(RightMarkField));
            On("@endtypeset", _ => Resume());
            On("fancyhf", t => FancySlots(t, "HF"));
            On("fancyhead", t => FancySlots(t, "H"));
            On("fancyfoot", t => FancySlots(t, "F"));
            foreach ((string name, string place) in new[] { ("lhead", "HeadLeft"), ("chead", "HeadCenter"), ("rhead", "HeadRight"),
                         ("lfoot", "FootLeft"), ("cfoot", "FootCenter"), ("rfoot", "FootRight") })
                On(name, t =>
                {
                    TexToken[]? body = expander.Argument(t, true);
                    if (body != null) SetSlot(place, body);
                });
            On("fancypagestyle", FancyPageStyle);
            On("@endfancystyle", _ => fancyTarget = "fancy");
            On("cite", t => Reference(t, true));
            On("nocite", NoCite);
            On("bibliographystyle", t => bibStyle = expander.NameArgument(t)?.Trim() ?? bibStyle);
            On("bibliography", Bibliography);
            On("thebibliography", BeginBibliography);
            On("endthebibliography", _ => EndParagraph());
            On("bibitem", BibItem);
            On("@endbib", _ => done = true);

            foreach (string kind in new[] { "figure", "table" })
                foreach (string name in new[] { kind, kind + "*" })
                {
                    On(name, t => BeginFloat(t, kind));
                    On("end" + name, _ => EndFloat());
                }
            On("caption", Caption);
            On("@endcaption", _ => Resume());
            On("includegraphics", IncludeGraphics);
            On("graphicspath", GraphicsPath);

            On("tabular", BeginTabular);
            On("endtabular", _ => EndTabular());
            On("multicolumn", MultiColumn);
            On("hline", _ => Rule(TexRuleKind.Plain));
            On("cline", t =>
            {
                TexToken[]? range = expander.Argument(t, false);
                if (range == null) return;
                (int from, int to) = Columns(range);
                Rule(TexRuleKind.Plain, from, to);
            });
            foreach ((string name, TexRuleKind kind) in new[] { ("toprule", TexRuleKind.Heavy), ("midrule", TexRuleKind.Light), ("bottomrule", TexRuleKind.Heavy) })
                On(name, t => Rule(kind, width: OptionalWidth(expander.Optional(t))));
            On("cmidrule", t =>
            {
                int width = OptionalWidth(expander.Optional(t));
                bool left = false, right = false;
                if (expander.ScanKeyword("("))
                    while (!expander.ScanKeyword(")") && expander.Next(out TexToken trim))
                    {
                        if (trim.IsCs) continue;
                        left |= trim.ch == 'l';
                        right |= trim.ch == 'r';
                    }
                TexToken[]? range = expander.Argument(t, false);
                if (range == null) return;
                (int from, int to) = Columns(range);
                Rule(TexRuleKind.Cmid, from, to, width, left, right);
            });

            On("\\", t =>
            {
                expander.TakeStar();
                expander.Optional(t);
                if (box != null) return;
                if (tables.Count > 0) EndRow();
                else if (mode == Mode.Vertical) expander.Error("LaTeX Error: There's no line here to end");
                else HList.Add(new TexPenalty(TexPenalty.Forced));
            });
            On("hskip", _ =>
            {
                TexGlue glue = expander.ScanGlue();
                StartParagraph();
                HList.Add(new TexGlueNode(glue, false, style));
            });
            On("vskip", _ =>
            {
                TexGlue glue = expander.ScanGlue();
                EndParagraph();
                vtarget.Add(new TexVGlue(glue));
            });
            On("kern", _ =>
            {
                int width = expander.ScanDimen();
                if (mode == Mode.Horizontal) HList.Add(new TexKern(width, style));
                else vtarget.Add(new TexVGlue(new TexGlue { width = width }));
            });
            On("penalty", _ =>
            {
                int penalty = expander.ScanInt();
                if (mode == Mode.Horizontal) HList.Add(new TexPenalty(penalty));
                else vtarget.Add(new TexVPenalty(penalty));
            });
            On("hrule", _ =>
            {
                while (expander.ScanKeyword("width") || expander.ScanKeyword("height") || expander.ScanKeyword("depth"))
                    expander.ScanDimen();
                EndParagraph();
                vtarget.Add(new TexRuleNode());
            });
            On("quad", _ => Space(1f));
            On("qquad", _ => Space(2f));
            On(",", _ => Kern(3f / 18f));
            On(":", _ => Kern(4f / 18f));
            On(";", _ => Kern(5f / 18f));
            On("!", _ => Kern(-3f / 18f));
            On(" ", _ => Interword());
            On("nobreakspace", _ => AddText(" "));
            On("-", _ => AddText("­"));
            On("hyphenation", t =>
            {
                TexToken[]? words = expander.Argument(t, true);
                if (words == null) return;
                foreach (string word in Text(words).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                    if (TexHyphenator.Exception(word) is (string plain, bool[] points)) hyphenation[plain] = points;
            });
            On("noindent", _ =>
            {
                if (mode == Mode.Horizontal) return;
                noIndent = true;
                StartParagraph();
            });
            On("indent", _ => StartParagraph());
            On("mbox", Box);
            On("hbox", Box);
            On("url", t =>
            {
                TexToken[]? arg = expander.Argument(t, true);
                if (arg == null) return;
                TexStyle outer = style;
                style = style with { font = style.font with { family = TexFamily.Mono } };
                AddText(Text(arg));
                style = outer;
            });
            On("href", t =>
            {
                if (expander.Argument(t, true) == null) return;
                TexToken[]? text = expander.Argument(t, true);
                if (text == null) return;
                expander.Insert(Grouped(new[] { TexToken.Cs("@underline") }.Concat(text)));
            });

            foreach (KeyValuePair<string, string> symbol in symbols)
            {
                string text = symbol.Value;
                On(symbol.Key, _ => AddText(text));
            }
            foreach (KeyValuePair<string, char> accent in accents)
            {
                char mark = accent.Value;
                On(accent.Key, t => Accent(t, mark));
            }

            commands["par"] = _ =>
            {
                if (mode == Mode.Vertical) afterEnvironment = false;
                EndParagraph();
            };
            commands["relax"] = _ => { };
        }

        private void DocumentClass(TexToken t)
        {
            TexToken[]? options = expander.Optional(t);
            string? name = expander.NameArgument(t);
            if (name == null) return;
            inDocument = false;

            foreach (string option in Options(options))
                switch (option)
                {
                    case "10pt": sizeOption = 0; break;
                    case "11pt": sizeOption = 1; break;
                    case "12pt": sizeOption = 2; break;
                    case "a4paper": paper = "a4"; break;
                    case "letterpaper": paper = "letter"; break;
                }
            if (name is "article" or "report" or "book") documentClass = name;
            else Log.Info($"document class {name} is set as article");
            pageStyle = documentClass == "book" ? "headings" : "plain";
            style = style with { font = style.font with { size = Size(NormalSize) } };
        }

        private void UsePackage(TexToken t)
        {
            TexToken[]? options = expander.Optional(t);
            string? names = expander.NameArgument(t);
            if (names == null) return;
            if (inDocument) expander.Error("LaTeX Error: Can be used only in preamble");

            foreach (string name in names.Split(',').Select(n => n.Trim()).Where(n => n.Length > 0))
            {
                if (name == "geometry" && options != null) Geometry(options);
                if (!knownPackages.Contains(name)) Log.Info($"package {name} is not supported and is ignored");
            }
        }

        // geometry's paper and margin keys; other keys are ignored.
        private void Geometry(TexToken[]? options)
        {
            if (options == null) return;
            foreach (TexToken[] option in Split(options))
            {
                int equals = Array.FindIndex(option, x => x.IsChar('=', TexCatcode.Other));
                string key = Text(equals < 0 ? option : option[..equals]).Trim();
                if (equals < 0)
                {
                    if (key is "a4paper" or "letterpaper") paper = key[..^5];
                    continue;
                }
                if (key == "paper")
                {
                    string value = Text(option[(equals + 1)..]).Trim();
                    if (value is "a4paper" or "letterpaper") paper = value[..^5];
                    continue;
                }

                float mm = Millimetres(option[(equals + 1)..]);
                switch (key)
                {
                    case "margin": marginTop = marginBottom = marginLeft = marginRight = mm; break;
                    case "hmargin": marginLeft = marginRight = mm; break;
                    case "vmargin": marginTop = marginBottom = mm; break;
                    case "left" or "lmargin" or "inner": marginLeft = mm; break;
                    case "right" or "rmargin" or "outer": marginRight = mm; break;
                    case "top" or "tmargin": marginTop = mm; break;
                    case "bottom" or "bmargin": marginBottom = mm; break;
                }
            }
        }

        private float Millimetres(TexToken[] value) => Dimen(value) / (float)Unity * 25.4f / 72.27f;

        // A dimension written in tokens, sp.
        private int Dimen(TexToken[] value)
        {
            expander.Insert(value.Append(TexToken.Cs("relax")).ToArray());
            int sp = expander.ScanDimen();
            while (expander.Next(out TexToken rest) && !(rest.IsCs && rest.name == "relax")) { }
            return sp;
        }

        // \textwidth and its kin from the class, paper and geometry.
        private void PageDimens()
        {
            List<TexToken> tokens = new List<TexToken>();
            void Set(string name, int sp)
            {
                tokens.Add(TexToken.Cs("global"));
                tokens.Add(TexToken.Cs(name));
                tokens.AddRange(Chars("=" + sp.ToString(CultureInfo.InvariantCulture) + "sp"));
                tokens.Add(TexToken.Cs("relax"));
            }

            foreach (string name in new[] { "textwidth", "linewidth", "columnwidth" })
                Set(name, textWidth);
            Set("textheight", textHeight);
            Set("paperwidth", (int)(paperWidth / MmPerPt * Unity));
            Set("paperheight", (int)(paperHeight / MmPerPt * Unity));
            expander.Insert(tokens.ToArray());
        }

        private static int Extent(float paper, float? before, float? after, float defaultPt)
        {
            if (before == null && after == null) return (int)(defaultPt * Unity);
            float side = (paper - defaultPt * MmPerPt) / 2f;
            return (int)((paper - (before ?? side) - (after ?? side)) / MmPerPt * Unity);
        }

        // \chapter (0) to \subparagraph (5).
        private void Section(TexToken t, int depth)
        {
            if (depth == 0 && !Chapters)
            {
                expander.Error(@"Undefined control sequence \chapter");
                depth = 1;
            }
            bool star = expander.TakeStar();
            expander.Optional(t);
            TexToken[]? title = expander.Argument(t, true);
            if (title == null) return;
            EndParagraph();
            int ex = metrics.XHeight(new TexFont(TexFamily.Roman, false, false, Size(NormalSize)));
            VSkip(depth switch { 0 => 50f * Unity, 1 => 3.5f * ex, _ => 3.25f * ex }, true);
            headingAfter = depth switch { 0 => 40f * Unity, 1 => 2.3f * ex, _ => 1.5f * ex };

            int top = Chapters ? 0 : 1;
            bool numbered = !star && depth <= (Chapters ? 2 : 3);
            if (numbered) expander.StepCounter(sectionNames[depth]);
            string number = numbered ? string.Join(".", Enumerable.Range(top, depth - top + 1).Select(d => expander.Counter(sectionNames[d]))) : "";
            if (numbered) SetLabel(number);
            headingMark = !star && depth <= 2 ? (depth, number) : null;

            if (depth >= 4)
            {
                noIndent = true;
                StartParagraph();
                expander.Insert(Grouped(new[] { TexToken.Cs("bfseries") }.Concat(title)).Append(TexToken.Cs("@runin")).ToArray());
                return;
            }

            TexFont normal = new TexFont(TexFamily.Roman, true, false, Size(NormalSize));
            int heading = depth - top + 1;
            if (depth == 0 && numbered)
            {
                Suspend(new TexParStyle { kind = TexParKind.Text, align = TexAlign.Left }, normal with { size = Size(8) });
                AddText("Chapter " + number);
                Resume();
                number = "";
            }

            int size = depth switch { 0 => 9, 1 => 6, 2 => 5, _ => NormalSize };
            Suspend(new TexParStyle { kind = TexParKind.Heading, level = heading, align = TexAlign.Left }, normal with { size = Size(size) });
            if (number.Length > 0)
            {
                AddText(number);
                Space(1f);
            }
            expander.Insert(title.Append(TexToken.Cs("@endhead")).ToArray());
        }

        private void Color(TexToken t)
        {
            TexToken[]? model = expander.Optional(t);
            string? spec = expander.NameArgument(t);
            if (spec == null) return;
            string? hex = ResolveColor(model == null ? "" : Text(model).Trim(), spec.Trim());
            if (hex == null) expander.Error($"Package xcolor Error: Undefined color `{spec}'");
            else SetStyle(style with { color = hex });
        }

        // \vskip of sp; merge takes the larger of it and what is pending, as \addvspace does
        private void VSkip(float sp, bool merge = false) => vtarget.Add(new TexVGlue(new TexGlue { width = (int)sp }, merge));

        // a list-level skip in sp: full at the outer level, half at the second, a quarter deeper
        private float ListSkip(float[] points, int depth) => points[sizeOption] * Unity / (depth <= 1 ? 1f : depth == 2 ? 2f : 4f);

        // A list or a trivlist environment ends: \topsep below it, and no indent until a blank line.
        private void EndEnvironment(int depth)
        {
            EndParagraph();
            VSkip(ListSkip(topSeps, depth), true);
            afterEnvironment = true;
        }

        private void BeginList(TexListKind kind)
        {
            EndParagraph();
            VSkip(ListSkip(topSeps, (list?.depth ?? 0) + 1), true);
            ListFrame frame = new ListFrame(kind, list);
            if (frame.depth > 4) expander.Error("LaTeX Error: Too deeply nested");
            ListFrame? outer = list;
            expander.Save(() => list = outer);
            list = frame;
        }

        private void Item(TexToken t)
        {
            TexToken[]? label = expander.Optional(t);
            EndParagraph();
            if (list == null) expander.Error("LaTeX Error: Lonely \\item--perhaps a missing list environment");
            else if (label == null)
            {
                list.count++;
                if (list.kind == TexListKind.Enumerate) SetLabel(EnumerateLabel(list));
            }

            if (list != null)
            {
                if (list.started) VSkip(2f * ListSkip(itemSeps, list.depth));
                list.started = true;
            }
            itemPending = true;
            StartParagraph();
            suppressSpace = true;
            if (label != null)
                expander.Insert(Grouped(list?.kind == TexListKind.Description ? new[] { TexToken.Cs("bfseries") }.Concat(label) : label)
                    .Append(TexToken.Cs("@label")).ToArray());
        }

        private void BeginQuote()
        {
            EndParagraph();
            VSkip(ListSkip(topSeps, (list?.depth ?? 0) + 1), true);
            bool outer = quote;
            expander.Save(() => quote = outer);
            quote = true;
        }

        private void BeginAligned(TexAlign to)
        {
            EndParagraph();
            VSkip(ListSkip(topSeps, (list?.depth ?? 0) + 1), true);
            SetAlign(to);
        }

        private void SetAlign(TexAlign to)
        {
            TexAlign outer = align;
            expander.Save(() => align = outer);
            align = to;
        }

        private void Verbatim(TexToken t)
        {
            EndParagraph();
            string? text = expander.CanReadRaw ? expander.ReadRaw("\\end{verbatim}") : null;
            if (text == null)
            {
                expander.Error(expander.CanReadRaw ? "File ended while scanning use of \\@xverbatim" : "LaTeX Error: verbatim illegal in command argument");
                return;
            }

            List<string> lines = text.Split('\n').ToList();
            int sourceAt = t.line;
            if (lines.Count > 1 && string.IsNullOrWhiteSpace(lines[0])) { lines.RemoveAt(0); sourceAt++; }
            if (lines.Count > 1 && string.IsNullOrWhiteSpace(lines[^1])) lines.RemoveAt(lines.Count - 1);

            TexStyle code = style with { font = style.font with { family = TexFamily.Mono, bold = false, italic = false } };
            int depth = (list?.depth ?? 0) + 1;
            VSkip(ListSkip(topSeps, depth), true);
            foreach (string line in lines)
            {
                TexParagraph paragraph = new TexParagraph(new TexParStyle { kind = TexParKind.Code, align = TexAlign.Left }) { line = sourceAt > 0 ? sourceAt++ : 0 };
                foreach (char c in line)
                    paragraph.list.Add(new TexChar(c, code));
                Add(paragraph);
            }
            VSkip(ListSkip(topSeps, depth), true);
            afterEnvironment = true;

            expander.Insert(new[] { TexToken.Cs("end"), TexToken.Char('{', TexCatcode.BeginGroup) }
                .Concat("verbatim".Select(c => TexToken.Char(c, TexCatcode.Letter)))
                .Append(TexToken.Char('}', TexCatcode.EndGroup)).ToArray());
        }

        private void Verb(TexToken t)
        {
            if (!expander.CanReadRaw)
            {
                expander.Error("LaTeX Error: \\verb illegal in command argument");
                return;
            }
            if (!expander.ReadRawChar(out char delimiter) || delimiter == '*' && !expander.ReadRawChar(out delimiter))
            {
                expander.Error("LaTeX Error: \\verb ended by end of line");
                return;
            }
            string? text = expander.ReadRaw(delimiter.ToString());
            if (text == null || text.Contains('\n'))
            {
                expander.Error("LaTeX Error: \\verb ended by end of line");
                if (text == null) return;
            }

            TexStyle outer = style;
            style = style with { font = style.font with { family = TexFamily.Mono } };
            AddText(text);
            style = outer;
        }

        private void Footnote(TexToken t)
        {
            expander.Optional(t);
            TexToken[]? text = expander.Argument(t, true);
            if (text == null) return;

            expander.StepCounter("footnote");
            SetLabel(expander.Counter("footnote").ToString(CultureInfo.InvariantCulture));
            string mark = "{}^{" + expander.Counter("footnote").ToString(CultureInfo.InvariantCulture) + "}";
            TexFootnote note = new TexFootnote((footnotes.Count + 1).ToString(CultureInfo.InvariantCulture));
            footnotes.Add(note);
            StartParagraph();
            HList.Add(new TexMathNode(mark, false, style) { note = note });

            TexFont small = new TexFont(TexFamily.Roman, false, false, Size(2));
            Suspend(new TexParStyle { kind = TexParKind.Text }, small, note.vlist);
            HList.Add(new TexMathNode(mark, false, style));
            Interword();
            expander.Insert(text.Append(TexToken.Cs("@endnote")).ToArray());
        }

        private void Box(TexToken t)
        {
            if (expander.ScanKeyword("to") || expander.ScanKeyword("spread")) expander.ScanDimen();
            StartParagraph();
            if (!expander.Next(out TexToken next)) return;
            if (!next.IsChar(next.ch, TexCatcode.BeginGroup))
            {
                expander.Insert(new[] { next });
                return;
            }

            TexHBox made = new TexHBox();
            HList.Add(made);
            TexHBox? outer = box;
            expander.Save(() => box = outer);
            box = made;
        }

        private void Accent(TexToken t, char mark)
        {
            TexToken[]? arg = expander.Argument(t, false);
            if (arg == null) return;
            TexToken[] body = arg.Where(x => !(x.IsChar(x.ch, TexCatcode.Space))).ToArray();
            if (body.Length == 0)
            {
                AddText(spacingAccents.GetValueOrDefault(mark, mark).ToString());
                return;
            }

            char letter = body[0].IsCs ? body[0].name switch { "i" => 'i', "j" => 'j', _ => '\0' } : body[0].ch;
            if (letter == '\0' || body.Length > 1)
            {
                expander.Error($"LaTeX Error: {t} takes one letter");
                return;
            }

            string composed = (letter.ToString() + mark).Normalize(NormalizationForm.FormC);
            if (composed.Length == 1) AddText(composed);
            else
            {
                Log.Warn($"{t}{letter} has no precomposed character; set without its accent");
                AddText(letter.ToString());
            }
        }
        #endregion

        #region ---- references ----
        private void SetLabel(string text)
        {
            string outer = currentLabel;
            expander.Save(() => currentLabel = outer);
            currentLabel = text;
        }

        // An enumerate item as \ref prints it: 1, 1a, 1(a)i, 1(a)iA.
        private static string EnumerateLabel(ListFrame list)
        {
            List<int> counts = new List<int>();
            for (ListFrame? f = list; f != null; f = f.parent)
                if (f.kind == TexListKind.Enumerate) counts.Insert(0, f.count);

            string[] parts = counts.Select((n, i) => (i % 4) switch
            {
                0 => n.ToString(CultureInfo.InvariantCulture),
                1 => Alpha(n, 'a'),
                2 => Roman(n),
                _ => Alpha(n, 'A')
            }).ToArray();
            string text = parts[0];
            if (parts.Length == 2) text += parts[1];
            if (parts.Length >= 3) text += "(" + parts[1] + ")" + parts[2];
            if (parts.Length >= 4) text += parts[3];
            return text;
        }

        private static string Alpha(int n, char first) => n is >= 1 and <= 26 ? ((char)(first + n - 1)).ToString() : n.ToString(CultureInfo.InvariantCulture);

        private static string Roman(int n)
        {
            StringBuilder roman = new StringBuilder();
            foreach ((int value, string numeral) in new[] { (1000, "m"), (900, "cm"), (500, "d"), (400, "cd"), (100, "c"), (90, "xc"),
                         (50, "l"), (40, "xl"), (10, "x"), (9, "ix"), (5, "v"), (4, "iv"), (1, "i") })
                for (; n >= value; n -= value) roman.Append(numeral);
            return roman.ToString();
        }

        private void Label(TexToken t)
        {
            string? key = expander.NameArgument(t)?.Trim();
            if (key == null) return;
            if (labels.ContainsKey(key)) expander.Error($"LaTeX Warning: Label `{key}' multiply defined");
            labels[key] = currentLabel;
            if (mode == Mode.Horizontal) HList.Add(new TexLabelMark(key));
            else if (vtarget.LastOrDefault(n => n is not TexVGlue) is TexParagraph last) last.list.Add(new TexLabelMark(key));
        }

        // \ref{key}, \pageref{key}, or \cite[note]{keys} as [labels, note].
        private void Reference(TexToken t, bool cite, bool page = false)
        {
            TexToken[]? note = cite ? expander.Optional(t) : null;
            string? names = expander.NameArgument(t);
            if (names == null) return;
            string[] keys = cite ? Keys(names) : new[] { names.Trim() };

            StartParagraph();
            if (cite)
            {
                foreach (string key in keys)
                    if (!citations.Contains(key)) citations.Add(key);
                AddText("[");
            }
            suppressSpace = false;
            boundary = true;
            TexRefNode node = new TexRefNode(keys, cite, style, t.line, t.column) { page = page };
            refs.Add(node);
            HList.Add(node);
            if (!cite) return;

            if (note == null || note.All(x => x.IsChar(x.ch, TexCatcode.Space))) AddText("]");
            else expander.Insert(new[] { TexToken.Char(',', TexCatcode.Other), TexToken.Char(' ', TexCatcode.Space) }
                .Concat(note).Append(TexToken.Char(']', TexCatcode.Other)).ToArray());
        }

        private void NoCite(TexToken t)
        {
            string? names = expander.NameArgument(t);
            if (names == null) return;
            foreach (string key in Keys(names))
                if (key == "*") allCited = true;
                else if (!citations.Contains(key)) citations.Add(key);
        }

        private static string[] Keys(string names) => names.Split(',').Select(k => k.Trim()).Where(k => k.Length > 0).ToArray();

        // Each \ref and \cite gets its text, now every label and \bibitem is known.
        private void Resolve()
        {
            foreach (TexRefNode r in refs)
                if (r.page) Look(labels, r.keys[0], "Reference", r.line, r.column, "??");
                else r.text = r.cite
                    ? string.Join(", ", r.keys.Select(k => Look(bibLabels, k, "Citation", r.line, r.column, "?")))
                    : Look(labels, r.keys[0], "Reference", r.line, r.column, "??");
            foreach ((TexMathNode node, int line) in mathRefs)
            {
                node.source = mathRef.Replace(node.source, m => @"\text{" + Look(labels, m.Groups[1].Value.Trim(), "Reference", line, 0, "??") + "}");
                node.source = mathEqref.Replace(node.source, m => @"\text{(" + Look(labels, m.Groups[1].Value.Trim(), "Reference", line, 0, "??") + ")}");
            }
        }

        private string Look(Dictionary<string, string> table, string key, string what, int line, int column, string missing)
        {
            if (table.TryGetValue(key, out string? text)) return text;
            errors.Add(new TexError(line, column, $"LaTeX Warning: {what} `{key}' undefined"));
            return missing;
        }
        #endregion

        #region ---- pages ----
        private void PageBreak(bool clear)
        {
            EndParagraph();
            if (mode == Mode.Vertical) vtarget.Add(new TexPageBreak(clear));
        }

        // the four standard page styles and fancyhdr's, one-sided, as source
        private void DefaultPageStyles()
        {
            TexToken[] page = { TexToken.Cs("thepage") };
            TexToken[] Slanted(string mark) => new[] { TexToken.Cs("slshape"), TexToken.Cs(mark) };
            styleSlots["empty"] = new Dictionary<string, TexToken[]>();
            styleSlots["plain"] = new Dictionary<string, TexToken[]> { ["FootCenter"] = page };
            styleSlots["headings"] = new Dictionary<string, TexToken[]> { ["HeadLeft"] = Slanted("rightmark"), ["HeadRight"] = page };
            styleSlots["myheadings"] = new Dictionary<string, TexToken[]>(styleSlots["headings"]);
            styleSlots["fancy"] = new Dictionary<string, TexToken[]>
            {
                ["HeadLeft"] = Slanted("leftmark"),
                ["HeadRight"] = Slanted("rightmark"),
                ["FootCenter"] = page
            };
        }

        // \pagestyle sets every page's; \thispagestyle the page the paragraph being set, or the next one, lands on.
        private void SetPageStyle(TexToken t, bool thisPage)
        {
            string? name = expander.NameArgument(t)?.Trim();
            if (name == null) return;
            if (!styleSlots.ContainsKey(name))
            {
                expander.Error($@"Undefined control sequence \ps@{name}");
                return;
            }

            if (!thisPage) pageStyle = name;
            else
            {
                usedStyles.Add(name);
                if (mode == Mode.Horizontal) par!.pageStyle = name;
                else pendingStyle = name;
            }
        }

        private void TakePending(TexParagraph p)
        {
            p.pageStyle ??= pendingStyle;
            p.markLeft ??= pendingMarkLeft;
            p.markRight ??= pendingMarkRight;
            pendingStyle = pendingMarkLeft = pendingMarkRight = null;
        }

        // \markboth{left}{right} or \markright{right}.
        private void Mark(TexToken t, bool both)
        {
            TexToken[]? left = both ? expander.Argument(t, true) : null;
            if (both && left == null) return;
            TexToken[]? right = expander.Argument(t, true);
            if (right == null) return;

            string? leftText = left == null ? null : MarkText(Typeset(left));
            string rightText = MarkText(Typeset(right));
            if (mode == Mode.Horizontal)
            {
                par!.markLeft = leftText ?? par.markLeft;
                par.markRight = rightText;
            }
            else
            {
                pendingMarkLeft = leftText ?? pendingMarkLeft;
                pendingMarkRight = rightText;
            }
        }

        // \sectionmark and \chaptermark under headings and fancyhdr, one-sided.
        private void HeadingMarks(TexParagraph heading, int depth, string number)
        {
            bool fancy = fancyStyles.Contains(pageStyle);
            if (!fancy && pageStyle != "headings") return;

            string text = MarkText(heading);
            string title = depth > 0 && number.Length > 0 && text.StartsWith(number, StringComparison.Ordinal) ? text[number.Length..].TrimStart() : text;
            string Numbered(string after) => number.Length > 0 ? number + after : "";
            if (Chapters)
            {
                string chapter = (number.Length > 0 ? "CHAPTER " + number + ". " : "") + title.ToUpperInvariant();
                if (depth == 0 && fancy) (heading.markLeft, heading.markRight) = (chapter, "");
                else if (depth == 0) heading.markRight = chapter;
                else if (depth == 1 && fancy) heading.markRight = (Numbered(". ") + title).ToUpperInvariant();
            }
            else
            {
                string section = (Numbered("  ") + title).ToUpperInvariant();
                if (depth == 1 && fancy) (heading.markLeft, heading.markRight) = (section, "");
                else if (depth == 1) heading.markRight = section;
                else if (depth == 2 && fancy) heading.markRight = Numbered("  ") + title;
            }
        }

        // A paragraph as one line of plain text, ligatures taken apart.
        private static string MarkText(TexParagraph p)
        {
            StringBuilder text = new StringBuilder();
            void Walk(List<TexNode> nodes)
            {
                foreach (TexNode node in nodes)
                    switch (node)
                    {
                        case TexChar c:
                            text.Append(c.ch switch { 'ﬀ' => "ff", 'ﬁ' => "fi", 'ﬂ' => "fl", 'ﬃ' => "ffi", 'ﬄ' => "ffl", '­' => "", _ => c.ch.ToString() });
                            break;
                        case TexGlueNode or TexKern:
                            if (text.Length > 0 && text[^1] != ' ') text.Append(' ');
                            break;
                        case TexHBox b:
                            Walk(b.list);
                            break;
                        case TexRefNode r:
                            text.Append(r.text);
                            break;
                    }
            }
            Walk(p.list);
            return text.ToString().Trim();
        }

        // \thepage, \leftmark, \rightmark: a field in a page style's slot; outside one there is no page to know.
        private void Field(char field)
        {
            if (!inSlot)
            {
                AddText("??");
                return;
            }
            StartParagraph();
            HList.Add(new TexChar(field, style));
        }

        // \fancyhf, \fancyhead, \fancyfoot: [L,C,R with E,O] into the style being defined; one-sided, so E-only entries are dropped.
        private void FancySlots(TexToken t, string parts)
        {
            TexToken[]? spec = expander.Optional(t);
            TexToken[]? body = expander.Argument(t, true);
            if (body == null) return;

            IEnumerable<string> items = spec == null ? new[] { "" } : Text(spec).ToUpperInvariant().Split(',').Select(s => s.Trim());
            foreach (string item in items)
            {
                if (item.Contains('E') && !item.Contains('O')) continue;
                string sides = item.Any(c => c is 'L' or 'C' or 'R') ? item : "LCR";
                string bands = item.Any(c => c is 'H' or 'F') && parts.Length > 1 ? item : parts;
                foreach (char band in bands.Where(c => c is 'H' or 'F'))
                    foreach (char side in sides.Where(c => c is 'L' or 'C' or 'R'))
                        SetSlot((band == 'H' ? "Head" : "Foot") + (side == 'L' ? "Left" : side == 'C' ? "Center" : "Right"), body);
            }
        }

        private void SetSlot(string place, TexToken[] body)
        {
            Dictionary<string, TexToken[]> slots = styleSlots[fancyTarget];
            if (body.Length == 0) slots.Remove(place);
            else slots[place] = body;
        }

        // \fancypagestyle{name}{definitions}: a style that starts from fancy's slots.
        private void FancyPageStyle(TexToken t)
        {
            string? name = expander.NameArgument(t)?.Trim();
            TexToken[]? body = expander.Argument(t, true);
            if (name == null || body == null) return;
            styleSlots[name] = new Dictionary<string, TexToken[]>(styleSlots["fancy"]);
            fancyStyles.Add(name);
            fancyTarget = name;
            expander.Insert(body.Append(TexToken.Cs("@endfancystyle")).ToArray());
        }

        // Every style a page can take, each slot typeset in the normal font.
        private void TypesetPageStyles()
        {
            usedStyles.Add(pageStyle);
            float headRule = RuleWidth("headrulewidth");
            float footRule = RuleWidth("footrulewidth");
            inSlot = true;
            foreach (string name in usedStyles)
            {
                if (!styleSlots.TryGetValue(name, out Dictionary<string, TexToken[]>? slots)) continue;
                TexPageStyle typeset = new TexPageStyle();
                if (fancyStyles.Contains(name))
                {
                    typeset.headRule = headRule;
                    typeset.footRule = footRule;
                }
                foreach (KeyValuePair<string, TexToken[]> slot in slots)
                    typeset.slots[slot.Key] = Typeset(slot.Value);
                pageStyles[name] = typeset;
            }
            inSlot = false;
        }

        private float RuleWidth(string macro)
        {
            expander.Insert(new[] { TexToken.Cs(macro), TexToken.Cs("relax") });
            return expander.ScanDimen() / (float)Unity;
        }

        // Typesets tokens on their own, in a group, into one paragraph the document never sees.
        private TexParagraph Typeset(TexToken[] tokens)
        {
            int depth = frames.Count;
            Suspend(new TexParStyle { kind = TexParKind.Text, align = TexAlign.Left }, new TexFont(TexFamily.Roman, false, false, Size(NormalSize)), new List<TexNode>());
            TexParagraph result = par!;
            expander.Insert(Grouped(tokens).Append(TexToken.Cs("@endtypeset")).ToArray());
            while (frames.Count > depth && expander.Next(out TexToken t)) Dispatch(t);
            return result;
        }
        #endregion

        #region ---- floats and pictures ----
        // Sets the body aside in a TexFloat where the environment stands; inside a box, note, cell or float it stays inline.
        private void BeginFloat(TexToken t, string kind)
        {
            TexToken[]? option = expander.Optional(t);
            string? outer = floatKind;
            TexFloat? outerFloat = openFloat;
            expander.Save(() =>
            {
                floatKind = outer;
                openFloat = outerFloat;
            });
            floatKind = kind;

            if (box != null || frames.Count > 0 || tables.Count > 0 || outer != null)
            {
                expander.Error("LaTeX Error: Not in outer par mode");
                openFloat = null;
                EndParagraph();
                VSkip(floatSeps[sizeOption] * Unity, true);
                return;
            }

            EnsureDocument();
            openFloat = new TexFloat(kind, Placement(option == null ? "" : Text(option)));
            if (mode == Mode.Horizontal) HList.Add(openFloat);
            else vtarget.Add(openFloat);
            frames.Push(new Frame(mode, par, vtarget, style, list, align, quote));
            vtarget = openFloat.vlist;
            mode = Mode.Vertical;
            par = null;
            list = null;
            quote = false;
            align = TexAlign.Justify;
            style = new TexStyle(new TexFont(TexFamily.Roman, false, false, Size(NormalSize)), null, false);
            noIndent = true;
        }

        private void EndFloat()
        {
            if (openFloat == null)
            {
                EndParagraph();
                VSkip(floatSeps[sizeOption] * Unity, true);
                afterEnvironment = true;
                return;
            }
            Resume();
            if (mode == Mode.Vertical) afterEnvironment = true;
            else if (HList.Count > 1 && HList[^2] is TexGlueNode { interword: true }) suppressSpace = true;
        }

        // The letters of [htbp!H] that mean something, tbp when none; a lone h becomes ht, as LaTeX does.
        private static string Placement(string option)
        {
            string letters = new string(option.Where(c => "htbpH!".Contains(c)).Distinct().ToArray());
            if (letters.Replace("!", "").Length == 0) return "tbp";
            return letters.Replace("!", "") == "h" ? letters + "t" : letters;
        }

        // "Figure n: text", centred, numbered within the chapter in report and book.
        private void Caption(TexToken t)
        {
            expander.Optional(t);
            TexToken[]? text = expander.Argument(t, true);
            if (text == null) return;
            if (floatKind == null)
            {
                expander.Error("LaTeX Error: \\caption outside float");
                return;
            }

            expander.StepCounter(floatKind);
            string number = (Chapters ? expander.Counter("chapter").ToString(CultureInfo.InvariantCulture) + "." : "")
                + expander.Counter(floatKind).ToString(CultureInfo.InvariantCulture);
            SetLabel(number);
            EndParagraph();
            Suspend(new TexParStyle { kind = TexParKind.Text, align = TexAlign.Center }, style.font);
            expander.Insert(new[] { TexToken.Cs(floatKind + "name"), TexToken.Char(' ', TexCatcode.Space) }
                .Concat(Chars(number + ":")).Append(TexToken.Char(' ', TexCatcode.Space))
                .Concat(text).Append(TexToken.Cs("@endcaption")).ToArray());
        }

        // graphicx's width, height, scale, angle and keepaspectratio; other keys are ignored.
        private void IncludeGraphics(TexToken t)
        {
            expander.TakeStar();
            TexToken[]? options = expander.Optional(t);
            TexToken[]? arg = expander.Argument(t, false);
            if (arg == null) return;
            string? path = FindGraphic(Text(arg).Trim());
            if (path == null) return;

            TexImage image = new TexImage(path);
            foreach (TexToken[] option in options == null ? new List<TexToken[]>() : Split(options))
            {
                int equals = Array.FindIndex(option, x => x.IsChar('=', TexCatcode.Other));
                string key = Text(equals < 0 ? option : option[..equals]).Trim();
                TexToken[] value = equals < 0 ? Array.Empty<TexToken>() : option[(equals + 1)..];
                switch (key)
                {
                    case "width": image.width = Dimen(value); break;
                    case "height" or "totalheight": image.height = Dimen(value); break;
                    case "scale": image.scale = Number(value); break;
                    case "angle": image.angle = Number(value); break;
                    case "keepaspectratio": image.keepAspect = equals < 0 || Text(value).Trim() == "true"; break;
                }
            }

            StartParagraph();
            suppressSpace = false;
            boundary = true;
            HList.Add(image);
        }

        private float Number(TexToken[] value)
        {
            if (float.TryParse(Text(value).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float n)) return n;
            expander.Error("Missing number, treated as zero");
            return 0f;
        }

        // The file under the source's folder and each \graphicspath entry; no extension tries PNG and JPEG.
        private string? FindGraphic(string name)
        {
            bool named = Path.GetExtension(name).Length > 0;
            if (folder != null)
                foreach (string prefix in graphicsPath)
                {
                    string stem = Path.Combine(folder, prefix, name);
                    IEnumerable<string> candidates = named ? new[] { stem } : rasterExtensions.Concat(vectorExtensions).Select(e => stem + e);
                    foreach (string candidate in candidates)
                    {
                        if (!File.Exists(candidate)) continue;
                        if (vectorExtensions.Contains(Path.GetExtension(candidate).ToLowerInvariant()))
                        {
                            expander.Error($"LaTeX Error: {Path.GetFileName(candidate)}: PDF and EPS pictures are not supported");
                            return null;
                        }
                        return Path.GetFullPath(candidate);
                    }
                }
            expander.Error($"LaTeX Error: File `{name}' not found");
            return null;
        }

        // \graphicspath{{a/}{b/}}
        private void GraphicsPath(TexToken t)
        {
            TexToken[]? arg = expander.Argument(t, false);
            if (arg == null) return;
            graphicsPath = new List<string> { "" };
            for (int i = 0; i < arg.Length; i++)
                if (arg[i].IsChar(arg[i].ch, TexCatcode.BeginGroup))
                {
                    i--;
                    graphicsPath.Add(Text(Group(arg, ref i)));
                }
        }
        #endregion

        #region ---- tabular ----
        private void BeginTabular(TexToken t)
        {
            expander.Optional(t);
            TexToken[]? spec = expander.Argument(t, false);
            if (spec == null) return;
            if (tables.Count > 0 && tables[^1].cell == null) StartCell();
            EndParagraph();
            EnsureDocument();

            TexTable table = new TexTable { line = sourceLine };
            ColumnSpec(spec, table, 0);
            if (table.columns.Count == 0) table.columns.Add(new TexColumn { align = TexAlign.Left });
            vtarget.Add(table);
            unaligned.Add((vtarget, table));
            tables.Add(new TableBuild(table, style));
        }

        // l c r p{} m{} b{} | @{} !{} >{} <{} *{n}{spec}
        private void ColumnSpec(TexToken[] spec, TexTable table, int depth)
        {
            for (int i = 0; i < spec.Length; i++)
            {
                TexToken s = spec[i];
                if (!s.IsCs && s.cat == TexCatcode.Space) continue;
                switch (s.IsCs ? '\0' : s.ch)
                {
                    case 'l': table.columns.Add(new TexColumn { align = TexAlign.Left }); break;
                    case 'c': table.columns.Add(new TexColumn { align = TexAlign.Center }); break;
                    case 'r': table.columns.Add(new TexColumn { align = TexAlign.Right }); break;
                    case 'p' or 'm' or 'b': table.columns.Add(new TexColumn { align = TexAlign.Justify, width = Dimen(Group(spec, ref i)) }); break;
                    case '|':
                        while (table.vrules.Count <= table.columns.Count) table.vrules.Add(0);
                        table.vrules[table.columns.Count]++;
                        break;
                    case '@' or '!' or '>' or '<': Group(spec, ref i); break;
                    case '*':
                        int.TryParse(Text(Group(spec, ref i)).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int count);
                        TexToken[] repeated = Group(spec, ref i);
                        for (int n = 0; n < Math.Min(count, 100) && depth < 8; n++)
                            ColumnSpec(repeated, table, depth + 1);
                        break;
                    default:
                        expander.Error("LaTeX Error: Illegal character in array arg");
                        break;
                }
            }
        }

        // The braced group or single token after i; i is left on its last token.
        private static TexToken[] Group(TexToken[] tokens, ref int i)
        {
            i++;
            while (i < tokens.Length && !tokens[i].IsCs && tokens[i].cat == TexCatcode.Space) i++;
            if (i >= tokens.Length) return Array.Empty<TexToken>();
            if (!tokens[i].IsChar(tokens[i].ch, TexCatcode.BeginGroup)) return new[] { tokens[i] };

            int start = i + 1, depth = 0;
            for (; i < tokens.Length; i++)
            {
                if (tokens[i].IsChar(tokens[i].ch, TexCatcode.BeginGroup)) depth++;
                else if (tokens[i].IsChar(tokens[i].ch, TexCatcode.EndGroup) && --depth == 0) return tokens[start..i];
            }
            return tokens[start..];
        }

        // A cell is set like a heading: the outer state suspended, the cell's own list the target.
        private void StartCell()
        {
            TableBuild b = tables[^1];
            if (b.row == null)
            {
                b.row = new List<TexTableCell>();
                b.table.rows.Add(b.row);
                b.column = 0;
            }
            TexTableCell cell = new TexTableCell();
            b.row.Add(cell);
            b.cell = cell;

            TexAlign to = b.column < b.table.columns.Count ? b.table.columns[b.column].align : TexAlign.Left;
            Suspend(new TexParStyle { kind = TexParKind.Text, align = to }, b.style.font, cell.vlist);
            style = b.style;
            suppressSpace = true;
        }

        private void EndCell()
        {
            TableBuild b = tables[^1];
            if (b.cell == null) return;
            Resume();
            b.column += b.cell.span;
            b.cell = null;
        }

        // &
        private void NextCell()
        {
            TableBuild b = tables[^1];
            if (b.cell == null) StartCell();
            EndCell();
            if (b.column >= b.table.columns.Count)
            {
                expander.Error("Extra alignment tab has been changed to \\cr");
                b.row = null;
            }
            StartCell();
        }

        // \\ in a tabular
        private void EndRow()
        {
            TableBuild b = tables[^1];
            if (b.cell == null) StartCell();
            EndCell();
            b.row = null;
        }

        private void EndTabular()
        {
            if (tables.Count == 0) return;
            EndCell();
            tables.RemoveAt(tables.Count - 1);
        }

        private void MultiColumn(TexToken t)
        {
            TexToken[]? count = expander.Argument(t, false);
            TexToken[]? spec = count == null ? null : expander.Argument(t, false);
            TexToken[]? text = spec == null ? null : expander.Argument(t, true);
            if (text == null) return;
            if (tables.Count == 0)
            {
                expander.Error("Misplaced \\omit");
                return;
            }

            TableBuild b = tables[^1];
            if (b.cell == null) StartCell();
            int.TryParse(Text(count!).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int span);
            b.cell!.span = Math.Max(1, span);
            TexTable own = new TexTable();
            ColumnSpec(spec!, own, 0);
            align = own.columns.Count > 0 ? own.columns[0].align : TexAlign.Center;
            expander.Insert(Grouped(text));
        }

        // \hline, \cline and the booktabs rules, at the row boundary they stand on; columns 1-based, 0 = all.
        private void Rule(TexRuleKind kind, int from = 0, int to = 0, int width = 0, bool trimLeft = false, bool trimRight = false)
        {
            if (tables.Count == 0)
            {
                expander.Error("Misplaced \\noalign");
                return;
            }
            TableBuild b = tables[^1];
            int row = b.row == null ? b.table.rows.Count : b.table.rows.Count - 1;
            int last = b.table.columns.Count - 1;
            b.table.rules.Add(new TexTableRule
            {
                row = row,
                kind = kind,
                from = from > 0 ? Math.Min(from - 1, last) : 0,
                to = to > 0 ? Math.Min(to - 1, last) : last,
                width = width,
                trimLeft = trimLeft,
                trimRight = trimRight
            });
        }

        // a-b, or a lone column
        private (int from, int to) Columns(TexToken[] range)
        {
            string[] ends = Text(range).Split('-');
            int.TryParse(ends[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int from);
            int to = from;
            if (ends.Length > 1) int.TryParse(ends[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out to);
            return (Math.Max(1, from), Math.Max(Math.Max(1, from), to));
        }

        private int OptionalWidth(TexToken[]? option) => option == null ? 0 : Dimen(option);
        #endregion

        #region ---- bibliography ----
        private void Bibliography(TexToken t)
        {
            string? names = expander.NameArgument(t);
            if (names == null) return;
            bibFiles = Keys(names);
            EndParagraph();
            EnsureDocument();
            TexBibliographyMark mark = new TexBibliographyMark();
            vtarget.Add(mark);
            bibMark = (vtarget, mark);
        }

        private void BeginBibliography(TexToken t)
        {
            expander.Argument(t, false);
            EndParagraph();
            bibCount = 0;
            expander.Insert(new[]
            {
                TexToken.Cs(Chapters ? "chapter" : "section"), TexToken.Char('*', TexCatcode.Other), TexToken.Char('{', TexCatcode.BeginGroup),
                TexToken.Cs(Chapters ? "bibname" : "refname"), TexToken.Char('}', TexCatcode.EndGroup)
            });
        }

        // \bibitem[label]{key}: a paragraph starting [label], or [n] counted.
        private void BibItem(TexToken t)
        {
            TexToken[]? label = expander.Optional(t);
            string? key = expander.NameArgument(t)?.Trim();
            if (key == null) return;
            EndParagraph();

            TexToken[] shown = label ?? Chars((++bibCount).ToString(CultureInfo.InvariantCulture));
            bibLabels[key] = Text(shown.Where(x => x.IsCs || x.cat is not (TexCatcode.BeginGroup or TexCatcode.EndGroup)));
            noIndent = true;
            StartParagraph();
            suppressSpace = true;
            expander.Insert(Grouped(new[] { TexToken.Char('[', TexCatcode.Other) }.Concat(shown).Append(TexToken.Char(']', TexCatcode.Other)))
                .Append(TexToken.Cs("@label")).ToArray());
        }

        // BibTeX's job, once every \cite is known: the cited entries as a thebibliography, typeset where \bibliography stood.
        private void TypesetBibliography()
        {
            if (bibMark is not (List<TexNode> list, TexBibliographyMark mark)) return;

            List<TexBibEntry> entries = new List<TexBibEntry>();
            foreach (string file in bibFiles)
            {
                string name = file.EndsWith(".bib", StringComparison.OrdinalIgnoreCase) ? file : file + ".bib";
                string? path = folder == null ? null : Path.Combine(folder, name);
                if (path == null || !File.Exists(path))
                {
                    expander.Error($"I couldn't open database file {name}");
                    continue;
                }
                TexBibliography.Parse(File.ReadAllText(path), entries, message => expander.Error($"{message} in {name}"));
            }
            if (!TexBibliography.styles.Contains(bibStyle))
            {
                expander.Error(bibStyle.Length == 0 ? "I found no \\bibliographystyle command" : $"I couldn't open style file {bibStyle}.bst");
                bibStyle = "plain";
            }

            Dictionary<string, TexBibEntry> byKey = new Dictionary<string, TexBibEntry>();
            foreach (TexBibEntry entry in entries)
                byKey.TryAdd(entry.key, entry);
            IEnumerable<string> keys = allCited ? citations.Concat(entries.Select(e => e.key)).Distinct() : citations;
            List<TexBibEntry> cited = keys.Where(byKey.ContainsKey).Select(k => byKey[k]).ToList();

            List<TexNode> made = new List<TexNode>();
            vtarget = made;
            mode = Mode.Vertical;
            par = null;
            done = false;
            expander.Insert(new[] { TexToken.Cs("@endbib") });
            expander.InsertSource(TexBibliography.Format(cited, bibStyle));
            while (!done && expander.Next(out TexToken t)) Dispatch(t);
            EndParagraph();

            int at = list.IndexOf(mark);
            list.RemoveAt(at);
            list.InsertRange(at, made);
        }
        #endregion

        #region ---- building ----
        private void Character(TexToken t)
        {
            switch (t.cat)
            {
                case TexCatcode.BeginGroup or TexCatcode.EndGroup:
                    boundary = true;
                    return;
                case TexCatcode.MathShift:
                    MathShift();
                    return;
                case TexCatcode.Space:
                    if (suppressSpace) suppressSpace = false;
                    else if (mode == Mode.Horizontal) Interword();
                    return;
                case TexCatcode.AlignTab:
                    if (tables.Count > 0) NextCell();
                    else expander.Error("Misplaced alignment tab character &");
                    return;
                case TexCatcode.Superscript or TexCatcode.Subscript:
                    expander.Error("Missing $ inserted");
                    return;
                default:
                    AddChar(t.ch);
                    return;
            }
        }

        private void AddChar(char c)
        {
            StartParagraph();
            suppressSpace = false;
            List<TexNode> hlist = HList;
            bool joins = !boundary;
            boundary = false;
            if (style.font.family != TexFamily.Mono)
            {
                if (c == '`') c = '‘';
                else if (c == '\'') c = '’';
                if (joins && hlist.Count > 0 && hlist[^1] is TexChar last && last.style == style && Ligature(last.ch, c) is char merged and not '\0')
                {
                    hlist[^1] = new TexChar(merged, style);
                    return;
                }
            }
            hlist.Add(new TexChar(c, style));
        }

        private static char Ligature(char a, char b) => (a, b) switch
        {
            ('-', '-') => '–',
            ('–', '-') => '—',
            ('‘', '‘') => '“',
            ('’', '’') => '”',
            ('!', '‘') => '¡',
            ('?', '‘') => '¿',
            ('f', 'f') => 'ﬀ',
            ('f', 'i') => 'ﬁ',
            ('f', 'l') => 'ﬂ',
            ('ﬀ', 'i') => 'ﬃ',
            ('ﬀ', 'l') => 'ﬄ',
            _ => '\0'
        };

        private void AddText(string text)
        {
            StartParagraph();
            suppressSpace = false;
            boundary = false;
            foreach (char c in text)
                HList.Add(new TexChar(c, style));
        }

        private void Interword()
        {
            StartParagraph();
            int quad = metrics.Quad(style.font);
            TexGlue glue = style.font.family == TexFamily.Mono
                ? new TexGlue { width = (int)(quad * 0.525) }
                : new TexGlue { width = quad / 3, stretch = quad / 6, shrink = quad / 9 };
            HList.Add(new TexGlueNode(glue, true, style));
        }

        private void Space(float em)
        {
            StartParagraph();
            HList.Add(new TexGlueNode(new TexGlue { width = (int)(metrics.Quad(style.font) * em) }, false, style));
        }

        private void Kern(float em)
        {
            StartParagraph();
            HList.Add(new TexKern((int)(metrics.Quad(style.font) * em), style));
        }

        private void StartParagraph()
        {
            if (mode == Mode.Horizontal) return;
            if (tables.Count > 0 && tables[^1].cell == null)
            {
                StartCell();
                return;
            }
            EnsureDocument();

            TexParStyle parStyle = new TexParStyle { kind = quote ? TexParKind.Quote : TexParKind.Text };
            parStyle.indent = !noIndent && !afterHeading && !afterEnvironment && list == null && !quote;
            noIndent = afterHeading = afterEnvironment = false;
            if (list != null && !itemPending) VSkip(ListSkip(itemSeps, list.depth));
            if (list != null)
            {
                parStyle.list = list.kind;
                parStyle.listDepth = list.depth;
                parStyle.listKindDepth = list.kindDepth;
                parStyle.itemNumber = list.count;
                parStyle.item = itemPending;
            }
            itemPending = false;
            par = new TexParagraph(parStyle) { line = sourceLine };
            mode = Mode.Horizontal;
            boundary = true;
        }

        private void EndParagraph()
        {
            for (int i = unaligned.Count - 1; i >= 0; i--)
                if (unaligned[i].target == vtarget)
                {
                    unaligned[i].table.align = align == TexAlign.Justify ? TexAlign.Left : align;
                    unaligned.RemoveAt(i);
                }
            if (mode != Mode.Horizontal || box != null) return;
            List<TexNode> hlist = par!.list;
            while (hlist.Count > 0 && hlist[^1] is TexGlueNode { interword: true }) hlist.RemoveAt(hlist.Count - 1);
            par.style.align = align;
            if (align != TexAlign.Justify) par.style.indent = false;
            if (hlist.Count > 0 || par.style.item)
            {
                if (vtarget == vlist) TakePending(par);
                vtarget.Add(par);
            }
            par = null;
            mode = Mode.Vertical;
        }

        private void Add(TexParagraph paragraph)
        {
            EnsureDocument();
            vtarget.Add(paragraph);
        }

        private void EnsureDocument()
        {
            if (inDocument) return;
            expander.Error("LaTeX Error: Missing \\begin{document}");
            inDocument = true;
        }

        // Starts a paragraph of its own for a heading or a note's text; Resume puts the outer state back.
        private void Suspend(TexParStyle parStyle, TexFont font, List<TexNode>? target = null)
        {
            EnsureDocument();
            frames.Push(new Frame(mode, par, vtarget, style, list, align, quote));
            vtarget = target ?? vtarget;
            list = null;
            quote = false;
            align = parStyle.align;
            style = new TexStyle(font, null, false);
            par = new TexParagraph(parStyle) { line = sourceLine };
            mode = Mode.Horizontal;
            boundary = true;
        }

        private void Resume()
        {
            if (frames.Count == 0) return;
            EndParagraph();
            Frame frame = frames.Pop();
            mode = frame.mode;
            par = frame.par;
            vtarget = frame.vtarget;
            style = frame.style;
            list = frame.list;
            align = frame.align;
            quote = frame.quote;
        }

        private void SetFont(TexFont font) => SetStyle(style with { font = font });

        private void SetStyle(TexStyle to)
        {
            TexStyle outer = style;
            expander.Save(() => style = outer);
            style = to;
        }

        private int Size(int index) => (int)Math.Round(sizePoints[sizeOption][index] * Unity);
        #endregion

        #region ---- math ----
        // $ starts inline math, $$ display math.
        private void MathShift()
        {
            StringBuilder source = new StringBuilder();
            bool display = false, first = true;
            expander.passUndefined = true;
            TexToken? after = null;
            while (expander.Next(out TexToken t))
            {
                if (!t.IsCs && t.cat == TexCatcode.MathShift)
                {
                    if (first && !display)
                    {
                        display = true;
                        continue;
                    }
                    if (display)
                    {
                        bool got = expander.Next(out TexToken second);
                        if (!got || second.IsCs || second.cat != TexCatcode.MathShift)
                        {
                            expander.Error("Display math should end with $$");
                            if (got) after = second;
                        }
                    }
                    break;
                }
                first = false;
                if (Ends(t))
                {
                    after = t;
                    break;
                }
                Append(source, t);
            }
            expander.passUndefined = false;
            Place(source.ToString(), display);
            if (after is TexToken pending) Dispatch(pending);
        }

        // \( \), \[ \] and the math environments, up to their closing token; amsmath's numbered and wrapped.
        private void MathUntil(TexToken close, bool display, string? environment = null)
        {
            StringBuilder source = new StringBuilder();
            expander.passUndefined = true;
            TexToken? after = null;
            bool closed = false;
            while (expander.Next(out TexToken t))
            {
                if (t.IsCs && t.name == close.name)
                {
                    closed = true;
                    break;
                }
                if (Ends(t))
                {
                    after = t;
                    break;
                }
                Append(source, t);
            }
            expander.passUndefined = false;
            if (!closed) expander.Error($"Missing {close} inserted");
            Place(environment == null ? source.ToString() : Number(environment, source.ToString()), display);
            if (after is TexToken pending) Dispatch(pending);
        }

        // amsmath's numbering: a \tag on each numbered row, its \label pointed at the number; the body wrapped in its environment.
        private string Number(string environment, string body)
        {
            bool numbered = !environment.EndsWith('*');
            bool once = environment is "equation" or "multline";
            List<string> rows = once ? new List<string> { body } : Rows(body);
            StringBuilder result = new StringBuilder();
            for (int i = 0; i < rows.Count; i++)
            {
                string row = rows[i];
                Match label = mathLabel.Match(row);
                row = mathLabel.Replace(row, "");
                bool notag = mathNotag.IsMatch(row);
                row = mathNotag.Replace(row, "");

                Match tag = mathTag.Match(row);
                string? number = tag.Success ? tag.Groups[2].Value : null;
                if (number == null && numbered && !notag && row.Trim().Length > 0)
                {
                    expander.StepCounter("equation");
                    number = (Chapters ? expander.Counter("chapter").ToString(CultureInfo.InvariantCulture) + "." : "")
                        + expander.Counter("equation").ToString(CultureInfo.InvariantCulture);
                    row += @"\tag{" + number + "}";
                }
                if (label.Success)
                {
                    string key = label.Groups[1].Value.Trim();
                    if (labels.ContainsKey(key)) expander.Error($"LaTeX Warning: Label `{key}' multiply defined");
                    labels[key] = number ?? currentLabel;
                }

                if (i > 0) result.Append(@"\\");
                result.Append(row);
            }
            return environment.StartsWith("equation") ? result.ToString() : $@"\begin{{{environment}}}{result}\end{{{environment}}}";
        }

        // A body split at its top-level \\, outside braces and nested environments.
        private static List<string> Rows(string body)
        {
            List<string> rows = new List<string>();
            int depth = 0, start = 0;
            for (int i = 0; i < body.Length; i++)
            {
                char c = body[i];
                if (c == '{') depth++;
                else if (c == '}') depth--;
                else if (c != '\\' || i + 1 >= body.Length) continue;
                else if (body[i + 1] == '\\' && depth == 0)
                {
                    rows.Add(body.Substring(start, i - start));
                    start = i + 2;
                    i++;
                }
                else if (string.CompareOrdinal(body, i, @"\begin", 0, 6) == 0) depth++;
                else if (string.CompareOrdinal(body, i, @"\end", 0, 4) == 0) depth--;
                else i++;
            }
            rows.Add(body.Substring(start));
            return rows;
        }

        // A token that cannot be in math: \par, a heading's or note's end, an environment's end.
        private bool Ends(TexToken t)
        {
            if (!t.IsCs) return false;
            bool ends = t.name == "par" || t.name!.StartsWith("@end")
                || t.name.StartsWith("end") && commands.ContainsKey(t.name) && !mathEnvironments.Contains(t.name[3..]);
            if (ends) expander.Error("Missing $ inserted");
            return ends;
        }

        private static void Append(StringBuilder source, TexToken t)
        {
            if (!t.IsCs)
            {
                source.Append(t.ch);
                return;
            }
            string name = t.name!;
            if (mathEnvironments.Contains(name))
            {
                source.Append(@"\begin{").Append(name).Append('}');
                return;
            }
            if (name.StartsWith("end") && mathEnvironments.Contains(name[3..]))
            {
                source.Append(@"\end{").Append(name[3..]).Append('}');
                return;
            }
            source.Append('\\').Append(name);
            if (name.Length > 0 && char.IsLetter(name[^1])) source.Append(' ');
        }

        private void Place(string source, bool display)
        {
            StartParagraph();
            TexMathNode node = new TexMathNode(mathLabel.Replace(source, "").Trim(), display, style);
            HList.Add(node);
            if (mathRef.IsMatch(node.source) || mathEqref.IsMatch(node.source)) mathRefs.Add((node, sourceLine));
            suppressSpace = display;
            boundary = true;
        }
        #endregion

        #region ---- helpers ----
        private static TexToken[] Grouped(IEnumerable<TexToken> tokens) =>
            new[] { TexToken.Char('{', TexCatcode.BeginGroup) }.Concat(tokens).Append(TexToken.Char('}', TexCatcode.EndGroup)).ToArray();

        // Text as letter and other tokens.
        private static TexToken[] Chars(string text) =>
            text.Select(c => TexToken.Char(c, char.IsLetter(c) ? TexCatcode.Letter : TexCatcode.Other)).ToArray();

        // Tokens as their characters, a control sequence as \name.
        private static string Text(IEnumerable<TexToken> tokens)
        {
            StringBuilder text = new StringBuilder();
            foreach (TexToken t in tokens)
                if (t.IsCs) text.Append('\\').Append(t.name);
                else text.Append(t.ch);
            return text.ToString();
        }

        // Comma-separated items outside braces.
        private static List<TexToken[]> Split(TexToken[] tokens)
        {
            List<TexToken[]> items = new List<TexToken[]>();
            List<TexToken> item = new List<TexToken>();
            int depth = 0;
            foreach (TexToken t in tokens)
            {
                if (t.IsChar(t.ch, TexCatcode.BeginGroup)) depth++;
                else if (t.IsChar(t.ch, TexCatcode.EndGroup)) depth--;
                if (depth == 0 && t.IsChar(',', TexCatcode.Other))
                {
                    items.Add(item.ToArray());
                    item.Clear();
                }
                else item.Add(t);
            }
            items.Add(item.ToArray());
            return items.Where(i => i.Any(t => !(t.IsChar(t.ch, TexCatcode.Space)))).ToList();
        }

        private static IEnumerable<string> Options(TexToken[]? tokens) =>
            tokens == null ? Enumerable.Empty<string>() : Split(tokens).Select(o => Text(o).Trim());

        private static string Today()
        {
            DateTime today = DateTime.Today;
            return $"{CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(today.Month)} {today.Day}, {today.Year}";
        }

        // xcolor: a named colour with name!percent[!other] mixing, or an HTML, rgb, RGB or gray value; #RRGGBB.
        private static string? ResolveColor(string model, string spec)
        {
            (int r, int g, int b)? rgb = model switch
            {
                "" => Named(spec),
                "HTML" => spec.Length == 6 && int.TryParse(spec, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int hex)
                    ? (hex >> 16 & 255, hex >> 8 & 255, hex & 255) : null,
                "rgb" => Floats(spec, 3) is float[] f ? (Byte(f[0]), Byte(f[1]), Byte(f[2])) : null,
                "RGB" => Floats(spec, 3) is float[] i ? ((int)i[0], (int)i[1], (int)i[2]) : null,
                "gray" => Floats(spec, 1) is float[] level ? (Byte(level[0]), Byte(level[0]), Byte(level[0])) : null,
                _ => null
            };
            return rgb is (int r, int g, int b) ? $"#{Math.Clamp(r, 0, 255):X2}{Math.Clamp(g, 0, 255):X2}{Math.Clamp(b, 0, 255):X2}" : null;
        }

        private static (int r, int g, int b)? Named(string spec)
        {
            string[] parts = spec.Split('!');
            if (!namedColors.TryGetValue(parts[0], out (int r, int g, int b) color)) return null;
            if (parts.Length == 1) return color;
            if (!float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float percent)) return null;
            (int r, int g, int b) other = (255, 255, 255);
            if (parts.Length > 2 && !namedColors.TryGetValue(parts[2], out other)) return null;
            float t = Math.Clamp(percent, 0f, 100f) / 100f;
            return (Mix(color.r, other.r, t), Mix(color.g, other.g, t), Mix(color.b, other.b, t));
        }

        private static int Mix(int a, int b, float t) => (int)MathF.Round(a * t + b * (1f - t));

        private static int Byte(float unit) => (int)MathF.Round(unit * 255f);

        private static float[]? Floats(string spec, int count)
        {
            string[] parts = spec.Split(',');
            if (parts.Length != count) return null;
            float[] values = new float[count];
            for (int i = 0; i < count; i++)
                if (!float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out values[i])) return null;
            return values;
        }
        #endregion

        #region ---- tables ----
        // text-mode control sequences that stand for characters
        private static readonly Dictionary<string, string> symbols = new Dictionary<string, string>
        {
            ["%"] = "%", ["&"] = "&", ["$"] = "$", ["#"] = "#", ["_"] = "_", ["{"] = "{", ["}"] = "}",
            ["i"] = "ı", ["j"] = "ȷ", ["ss"] = "ß", ["SS"] = "SS", ["ae"] = "æ", ["AE"] = "Æ", ["oe"] = "œ", ["OE"] = "Œ",
            ["o"] = "ø", ["O"] = "Ø", ["l"] = "ł", ["L"] = "Ł", ["aa"] = "å", ["AA"] = "Å",
            ["dag"] = "†", ["ddag"] = "‡", ["S"] = "§", ["P"] = "¶", ["copyright"] = "©", ["pounds"] = "£",
            ["ldots"] = "…", ["dots"] = "…", ["textellipsis"] = "…",
            ["textbackslash"] = "\\", ["textasciitilde"] = "~", ["textasciicircum"] = "^", ["textbar"] = "|",
            ["textless"] = "<", ["textgreater"] = ">", ["textunderscore"] = "_", ["slash"] = "/",
            ["textendash"] = "–", ["textemdash"] = "—", ["textquoteleft"] = "‘", ["textquoteright"] = "’",
            ["textquotedblleft"] = "“", ["textquotedblright"] = "”", ["quotesinglbase"] = "‚", ["quotedblbase"] = "„",
            ["guillemotleft"] = "«", ["guillemotright"] = "»", ["guilsinglleft"] = "‹", ["guilsinglright"] = "›",
            ["textbullet"] = "•", ["textdagger"] = "†", ["textdaggerdbl"] = "‡", ["textsection"] = "§", ["textparagraph"] = "¶",
            ["textregistered"] = "®", ["textcopyright"] = "©", ["textdegree"] = "°", ["euro"] = "€", ["texteuro"] = "€",
            ["textperthousand"] = "‰", ["textexclamdown"] = "¡", ["textquestiondown"] = "¿",
            ["TeX"] = "TeX", ["LaTeX"] = "LaTeX", ["LaTeXe"] = "LaTeX2e"
        };

        // xcolor's base colours
        private static readonly Dictionary<string, (int r, int g, int b)> namedColors = new Dictionary<string, (int, int, int)>
        {
            ["black"] = (0, 0, 0), ["white"] = (255, 255, 255), ["red"] = (255, 0, 0), ["green"] = (0, 255, 0),
            ["blue"] = (0, 0, 255), ["cyan"] = (0, 255, 255), ["magenta"] = (255, 0, 255), ["yellow"] = (255, 255, 0),
            ["gray"] = (128, 128, 128), ["darkgray"] = (64, 64, 64), ["lightgray"] = (191, 191, 191), ["brown"] = (191, 128, 64),
            ["lime"] = (191, 255, 0), ["olive"] = (128, 128, 0), ["orange"] = (255, 128, 0), ["pink"] = (255, 191, 191),
            ["purple"] = (191, 0, 64), ["teal"] = (0, 128, 128), ["violet"] = (128, 0, 128)
        };

        // accent commands and the combining marks they put on a letter
        private static readonly Dictionary<string, char> accents = new Dictionary<string, char>
        {
            ["'"] = '́', ["`"] = '̀', ["^"] = '̂', ["\""] = '̈', ["~"] = '̃', ["="] = '̄',
            ["."] = '̇', ["u"] = '̆', ["v"] = '̌', ["H"] = '̋', ["c"] = '̧', ["k"] = '̨',
            ["r"] = '̊', ["d"] = '̣', ["b"] = '̱'
        };

        // an accent over nothing
        private static readonly Dictionary<char, char> spacingAccents = new Dictionary<char, char>
        {
            ['́'] = '´', ['̀'] = '`', ['̂'] = 'ˆ', ['̈'] = '¨', ['̃'] = '˜', ['̄'] = '¯',
            ['̇'] = '˙', ['̆'] = '˘', ['̌'] = 'ˇ', ['̋'] = '˝', ['̧'] = '¸', ['̨'] = '˛',
            ['̊'] = '˚'
        };
        #endregion
    }
}
