using ArctisAurora.Core.Diagnostics;
using System.Globalization;
using System.Text;

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
            "inputenc", "fontenc", "lmodern", "textcomp", "babel", "microtype"
        };

        public readonly TexExpander expander;
        public List<TexError> errors => expander.errors;

        // the main vertical list, and \footnote texts as endnotes
        public readonly List<TexNode> vlist = new List<TexNode>();
        public readonly List<TexNode> endnotes = new List<TexNode>();

        // document class and page
        public string documentClass = "article";
        public int sizeOption;
        public string paper = "letter";
        public float? marginTop, marginBottom, marginLeft, marginRight;

        private readonly ITexFontMetrics metrics;
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
        private bool inDocument = true;
        private bool done;
        private bool itemPending;
        private bool boundary;
        private bool suppressSpace;
        private bool warnedSmallCaps;

        private List<TexNode> HList => box?.list ?? par!.list;

        // \normalsize at the class option, sp
        public int normalSize => Size(NormalSize);

        private bool Chapters => documentClass is "report" or "book";

        public TexTypesetter(string source, ITexFontMetrics? metrics = null)
        {
            this.metrics = metrics ?? new FixedMetrics();
            vtarget = vlist;
            style = new TexStyle(new TexFont(TexFamily.Roman, false, false, Size(NormalSize)), null, false);
            expander = new TexExpander(source, TexFormat.Prelude);
            expander.quad = () => this.metrics.Quad(style.font);
            expander.xHeight = () => this.metrics.XHeight(style.font);
            Register();
        }

        public void Run()
        {
            while (!done && expander.Next(out TexToken t)) Dispatch(t);
            EndParagraph();
        }

        private void Dispatch(TexToken t)
        {
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
            On("document", _ => inDocument = true);
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
            On("@endhead", _ => Resume());
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
            foreach (string name in new[] { "enditemize", "endenumerate", "enddescription", "endquote", "endquotation",
                         "endcenter", "endflushleft", "endflushright" })
                On(name, _ => EndParagraph());
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
            foreach ((string env, bool display) in new[] { ("equation", true), ("equation*", true), ("displaymath", true), ("math", false) })
            {
                On(env, _ => MathUntil(TexToken.Cs("end" + env), display));
                On("end" + env, _ => { });
            }
            On("footnote", Footnote);
            On("@endnote", _ => Resume());

            On("\\", t =>
            {
                expander.TakeStar();
                expander.Optional(t);
                if (box != null) return;
                if (mode == Mode.Vertical) expander.Error("LaTeX Error: There's no line here to end");
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
            On("noindent", _ => StartParagraph());
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

            commands["par"] = _ => EndParagraph();
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

        private float Millimetres(TexToken[] value)
        {
            expander.Insert(value.Append(TexToken.Cs("relax")).ToArray());
            int sp = expander.ScanDimen();
            while (expander.Next(out TexToken rest) && !(rest.IsCs && rest.name == "relax")) { }
            return sp / (float)Unity * 25.4f / 72.27f;
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

            int top = Chapters ? 0 : 1;
            bool numbered = !star && depth <= (Chapters ? 2 : 3);
            if (numbered) expander.StepCounter(sectionNames[depth]);
            string number = numbered ? string.Join(".", Enumerable.Range(top, depth - top + 1).Select(d => expander.Counter(sectionNames[d]))) : "";

            if (depth >= 4)
            {
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

        private void BeginList(TexListKind kind)
        {
            EndParagraph();
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
            else if (label == null) list.count++;

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
            bool outer = quote;
            expander.Save(() => quote = outer);
            quote = true;
        }

        private void BeginAligned(TexAlign to)
        {
            EndParagraph();
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
            if (lines.Count > 1 && string.IsNullOrWhiteSpace(lines[0])) lines.RemoveAt(0);
            if (lines.Count > 1 && string.IsNullOrWhiteSpace(lines[^1])) lines.RemoveAt(lines.Count - 1);

            TexStyle code = style with { font = style.font with { family = TexFamily.Mono, bold = false, italic = false } };
            foreach (string line in lines)
            {
                TexParagraph paragraph = new TexParagraph(new TexParStyle { kind = TexParKind.Code, align = TexAlign.Left });
                foreach (char c in line)
                    paragraph.list.Add(new TexChar(c, code));
                Add(paragraph);
            }

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
            string mark = "{}^{" + expander.Counter("footnote").ToString(CultureInfo.InvariantCulture) + "}";
            StartParagraph();
            HList.Add(new TexMathNode(mark, false, style));

            TexFont normal = new TexFont(TexFamily.Roman, false, false, Size(NormalSize));
            Suspend(new TexParStyle { kind = TexParKind.Text }, normal, endnotes);
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
                    expander.Error("Misplaced alignment tab character &");
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
            EnsureDocument();

            TexParStyle parStyle = new TexParStyle { kind = quote ? TexParKind.Quote : TexParKind.Text };
            if (list != null)
            {
                parStyle.list = list.kind;
                parStyle.listDepth = list.depth;
                parStyle.listKindDepth = list.kindDepth;
                parStyle.itemNumber = list.count;
                parStyle.item = itemPending;
            }
            itemPending = false;
            par = new TexParagraph(parStyle);
            mode = Mode.Horizontal;
            boundary = true;
        }

        private void EndParagraph()
        {
            if (mode != Mode.Horizontal || box != null) return;
            List<TexNode> hlist = par!.list;
            while (hlist.Count > 0 && hlist[^1] is TexGlueNode { interword: true }) hlist.RemoveAt(hlist.Count - 1);
            par.style.align = align;
            if (hlist.Count > 0 || par.style.item) vtarget.Add(par);
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
            par = new TexParagraph(parStyle);
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
            Place(source, display);
            if (after is TexToken pending) Dispatch(pending);
        }

        // \( \), \[ \] and the math environments, up to their closing token.
        private void MathUntil(TexToken close, bool display)
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
            Place(source, display);
            if (after is TexToken pending) Dispatch(pending);
        }

        // A token that cannot be in math: \par, a heading's or note's end, an environment's end.
        private bool Ends(TexToken t)
        {
            if (!t.IsCs) return false;
            bool ends = t.name == "par" || t.name!.StartsWith("@end") || t.name.StartsWith("end") && commands.ContainsKey(t.name);
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
            source.Append('\\').Append(t.name);
            if (t.name!.Length > 0 && char.IsLetter(t.name[^1])) source.Append(' ');
        }

        private void Place(StringBuilder source, bool display)
        {
            StartParagraph();
            HList.Add(new TexMathNode(source.ToString().Trim(), display, style));
            suppressSpace = display;
            boundary = true;
        }
        #endregion

        #region ---- helpers ----
        private static TexToken[] Grouped(IEnumerable<TexToken> tokens) =>
            new[] { TexToken.Char('{', TexCatcode.BeginGroup) }.Concat(tokens).Append(TexToken.Char('}', TexCatcode.EndGroup)).ToArray();

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
