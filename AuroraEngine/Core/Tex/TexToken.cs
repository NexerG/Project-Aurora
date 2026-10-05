namespace ArctisAurora.Core.Tex
{
    // A character with its category, a control sequence, or a macro body's parameter slot.
    public readonly struct TexToken
    {
        // control sequence name; null for a character
        public readonly string? name;
        public readonly char ch;
        public readonly TexCatcode cat;

        // 1-9 for a #n slot in a macro body
        public readonly byte param;

        // where it was read
        public readonly int line;
        public readonly int column;

        private TexToken(string? name, char ch, TexCatcode cat, byte param, int line, int column)
        {
            this.name = name;
            this.ch = ch;
            this.cat = cat;
            this.param = param;
            this.line = line;
            this.column = column;
        }

        public static TexToken Char(char ch, TexCatcode cat, int line = 0, int column = 0) => new TexToken(null, ch, cat, 0, line, column);

        public static TexToken Cs(string name, int line = 0, int column = 0) => new TexToken(name, '\0', TexCatcode.Escape, 0, line, column);

        public static TexToken Param(int n) => new TexToken(null, '#', TexCatcode.Parameter, (byte)n, 0, 0);

        public bool IsCs => name != null;

        public bool IsChar(char c, TexCatcode category) => name == null && param == 0 && ch == c && cat == category;

        // Equal as TeX compares tokens: the same name, or the same character and category.
        public bool Same(TexToken other) =>
            name != null ? name == other.name : other.name == null && ch == other.ch && cat == other.cat && param == other.param;

        public override string ToString() => name != null ? "\\" + name : param > 0 ? "#" + param : ch.ToString();
    }
}
