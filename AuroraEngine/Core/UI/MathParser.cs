using ArctisAurora.Core.Filing;

namespace ArctisAurora.Core.UI
{
    public abstract class MathNode { }

    // a {…} group, or the whole formula
    public sealed class MathList : MathNode
    {
        public readonly List<MathNode> items = new List<MathNode>();
    }

    public sealed class MathSymbol : MathNode
    {
        public char ch;
        public FontStyle face;
        public MathClass cls;
        public bool bigOp;
        public bool limits;
    }

    // \text content and operator names
    public sealed class MathText : MathNode
    {
        public string text = string.Empty;
        public FontStyle face;
        public MathClass cls;
        public bool limits;
    }

    public sealed class MathScripts : MathNode
    {
        public MathNode nucleus = null!;
        public MathNode sup;
        public MathNode sub;
    }

    public sealed class MathFraction : MathNode
    {
        public MathNode num = null!;
        public MathNode den = null!;
        public MathStyle? style;
    }

    public sealed class MathRadical : MathNode
    {
        public MathNode body = null!;
        public MathNode degree;
    }

    // \left…\right; '\0' is the empty delimiter
    public sealed class MathDelimited : MathNode
    {
        public char left;
        public char right;
        public MathNode body = null!;
    }

    public sealed class MathAccent : MathNode
    {
        public char accent;
        public bool bar;
        public MathNode body = null!;
    }

    public sealed class MathSpace : MathNode
    {
        public float em;
    }

    public sealed class MathError : MathNode
    {
        public string source = string.Empty;
    }

    public static class MathParser
    {
        // Parses TeX math; anything it cannot read makes the whole formula a MathError.
        public static MathNode Parse(string tex)
        {
            Parser parser = new Parser(tex ?? string.Empty);
            MathList list = parser.ParseList(Stop.End);
            return parser.failed ? new MathError { source = tex ?? string.Empty } : list;
        }

        private enum Stop { End, Brace, Bracket, Right }

        private enum FontMode { Normal, Roman, Bold, Blackboard }

        private sealed class Parser
        {
            private readonly string s;
            private int pos;
            private FontMode mode;
            public bool failed;

            public Parser(string source) { s = source; }

            private bool AtEnd => pos >= s.Length;

            private void SkipSpace()
            {
                while (!AtEnd && char.IsWhiteSpace(s[pos])) pos++;
            }

            private MathList Fail()
            {
                failed = true;
                return new MathList();
            }

            public MathList ParseList(Stop stop)
            {
                MathList list = new MathList();
                while (!failed)
                {
                    SkipSpace();
                    if (AtEnd)
                    {
                        if (stop != Stop.End) Fail();
                        break;
                    }

                    char c = s[pos];
                    if (c == '}')
                    {
                        if (stop != Stop.Brace) Fail();
                        else pos++;
                        break;
                    }
                    if (c == ']' && stop == Stop.Bracket)
                    {
                        pos++;
                        break;
                    }
                    if (stop == Stop.Right && PeekCommand() == "right") break;

                    MathNode atom = ParseScripted();
                    if (atom != null) list.items.Add(atom);
                }
                return list;
            }

            private string PeekCommand()
            {
                if (AtEnd || s[pos] != '\\') return null;
                int end = pos + 1;
                while (end < s.Length && char.IsAsciiLetter(s[end])) end++;
                return s.Substring(pos + 1, end - pos - 1);
            }

            private MathNode ParseScripted()
            {
                MathNode nucleus = ParseAtom();
                if (failed) return null;

                MathNode sup = null, sub = null;
                while (!failed)
                {
                    SkipSpace();
                    if (AtEnd || (s[pos] != '^' && s[pos] != '_')) break;

                    bool isSup = s[pos] == '^';
                    pos++;
                    if ((isSup ? sup : sub) != null) { Fail(); break; }
                    MathNode argument = ParseArgument();
                    if (isSup) sup = argument; else sub = argument;
                }

                if (sup == null && sub == null) return nucleus;
                return new MathScripts { nucleus = nucleus ?? new MathList(), sup = sup, sub = sub };
            }

            // A {…} group, one command, or one character.
            private MathNode ParseArgument()
            {
                SkipSpace();
                if (AtEnd) return Fail();
                if (s[pos] == '{')
                {
                    pos++;
                    return ParseList(Stop.Brace);
                }
                if (s[pos] == '}' || s[pos] == '^' || s[pos] == '_') return Fail();
                return ParseAtom();
            }

            // null for an empty nucleus in front of ^ or _
            private MathNode ParseAtom()
            {
                char c = s[pos];
                switch (c)
                {
                    case '{':
                        pos++;
                        return ParseList(Stop.Brace);
                    case '\\':
                        return ParseCommand();
                    case '^':
                    case '_':
                        return null;
                    case '~':
                        pos++;
                        return new MathSpace { em = 1f / 3f };
                    case '&':
                    case '#':
                    case '$':
                        return Fail();
                    case '\'':
                        pos++;
                        return new MathSymbol { ch = '′', face = FontStyle.Regular, cls = MathClass.Ord };
                }

                pos++;
                return Character(c);
            }

            private MathNode Character(char c)
            {
                if (MathSymbols.IsBigOp(c, out bool limits))
                    return new MathSymbol { ch = c, face = FontStyle.Regular, cls = MathClass.Op, bigOp = true, limits = limits };

                char drawn = c == '-' ? '−' : c == '*' ? '∗' : c;
                return new MathSymbol { ch = drawn, face = FaceFor(drawn), cls = MathSymbols.ClassOf(c) };
            }

            private static bool IsItalicLetter(char c) =>
                char.IsAsciiLetter(c) || (c >= 'α' && c <= 'ω') || c == 'ϑ' || c == 'ϕ' || c == 'ϖ' || c == 'ϱ' || c == 'ϵ';

            private FontStyle FaceFor(char c)
            {
                if (mode == FontMode.Bold) return FontStyle.Bold;
                if (mode == FontMode.Normal && IsItalicLetter(c)) return FontStyle.Italic;
                return FontStyle.Regular;
            }

            private string ReadCommandName()
            {
                pos++;
                if (AtEnd) return null;
                int start = pos;
                if (char.IsAsciiLetter(s[pos]))
                    while (!AtEnd && char.IsAsciiLetter(s[pos])) pos++;
                else
                    pos++;
                return s.Substring(start, pos - start);
            }

            private MathNode ParseCommand()
            {
                string name = ReadCommandName();
                if (name == null) return Fail();

                switch (name)
                {
                    case ",": return new MathSpace { em = 3f / 18f };
                    case ":": return new MathSpace { em = 4f / 18f };
                    case ";": return new MathSpace { em = 5f / 18f };
                    case "!": return new MathSpace { em = -3f / 18f };
                    case " ": return new MathSpace { em = 1f / 3f };
                    case "quad": return new MathSpace { em = 1f };
                    case "qquad": return new MathSpace { em = 2f };

                    case "frac":
                    case "dfrac":
                    case "tfrac":
                    {
                        MathNode num = ParseArgument();
                        MathNode den = ParseArgument();
                        MathStyle? style = name == "dfrac" ? MathStyle.Display : name == "tfrac" ? MathStyle.Text : null;
                        return new MathFraction { num = num, den = den, style = style };
                    }

                    case "sqrt":
                    {
                        MathNode degree = null;
                        SkipSpace();
                        if (!AtEnd && s[pos] == '[')
                        {
                            pos++;
                            degree = ParseList(Stop.Bracket);
                        }
                        return new MathRadical { body = ParseArgument(), degree = degree };
                    }

                    case "left":
                    {
                        char left = ReadDelimiter();
                        MathList body = ParseList(Stop.Right);
                        if (failed || PeekCommand() != "right") return Fail();
                        ReadCommandName();
                        char right = ReadDelimiter();
                        return new MathDelimited { left = left, right = right, body = body };
                    }

                    case "text": return new MathText { text = ReadRawGroup(), face = FontStyle.Regular, cls = MathClass.Ord };
                    case "mathrm": return InMode(FontMode.Roman);
                    case "mathbf": return InMode(FontMode.Bold);
                    case "mathbb": return InMode(FontMode.Blackboard);

                    case "bar":
                    case "overline":
                        return new MathAccent { bar = true, body = ParseArgument() };
                }

                if (MathSymbols.accents.TryGetValue(name, out char accent))
                    return new MathAccent { accent = accent, body = ParseArgument() };

                if (MathSymbols.operatorNames.TryGetValue(name, out bool opLimits))
                {
                    string text = name == "liminf" ? "lim inf" : name == "limsup" ? "lim sup" : name;
                    return new MathText { text = text, face = FontStyle.Regular, cls = MathClass.Op, limits = opLimits };
                }

                if (MathSymbols.bigOps.TryGetValue(name, out (char ch, bool limits) op))
                    return new MathSymbol { ch = op.ch, face = FontStyle.Regular, cls = MathClass.Op, bigOp = true, limits = op.limits };

                if (MathSymbols.commands.TryGetValue(name, out (char ch, MathClass cls) symbol))
                    return new MathSymbol { ch = symbol.ch, face = FaceFor(symbol.ch), cls = symbol.cls };

                return Fail();
            }

            // \mathrm, \mathbf and \mathbb: the argument parsed under another font mode
            private MathNode InMode(FontMode inner)
            {
                FontMode outer = mode;
                mode = inner;
                MathNode argument = ParseArgument();
                mode = outer;
                if (inner == FontMode.Blackboard) Blackboard(argument);
                return argument;
            }

            private static void Blackboard(MathNode node)
            {
                if (node is MathSymbol symbol && MathSymbols.blackboard.TryGetValue(symbol.ch, out char doubleStruck))
                    symbol.ch = doubleStruck;
                else if (node is MathList list)
                    foreach (MathNode item in list.items) Blackboard(item);
            }

            // \text's braces, taken verbatim
            private string ReadRawGroup()
            {
                SkipSpace();
                if (AtEnd || s[pos] != '{') { Fail(); return string.Empty; }

                int depth = 0;
                int start = pos + 1;
                for (; pos < s.Length; pos++)
                {
                    if (s[pos] == '\\' && pos + 1 < s.Length) { pos++; continue; }
                    if (s[pos] == '{') depth++;
                    else if (s[pos] == '}' && --depth == 0)
                    {
                        string text = s.Substring(start, pos - start);
                        pos++;
                        return text.Replace("\\{", "{").Replace("\\}", "}");
                    }
                }
                Fail();
                return string.Empty;
            }

            private char ReadDelimiter()
            {
                SkipSpace();
                if (AtEnd) { Fail(); return '\0'; }

                char c = s[pos];
                if (c == '\\')
                {
                    string name = ReadCommandName();
                    if (name != null && MathSymbols.delimiters.TryGetValue(name, out char named)) return named;
                    Fail();
                    return '\0';
                }

                pos++;
                switch (c)
                {
                    case '.': return '\0';
                    case '(': case ')': case '[': case ']': case '|': case '/': return c;
                    case '<': return '⟨';
                    case '>': return '⟩';
                }
                Fail();
                return '\0';
            }
        }
    }
}
