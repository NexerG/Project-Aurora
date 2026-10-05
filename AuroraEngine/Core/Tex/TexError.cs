namespace ArctisAurora.Core.Tex
{
    // A problem in LaTeX source, at the line and column where it was noticed.
    public readonly struct TexError
    {
        public readonly int line;
        public readonly int column;
        public readonly string message;

        public TexError(int line, int column, string message)
        {
            this.line = line;
            this.column = column;
            this.message = message;
        }

        public override string ToString() => $"{line}:{column} {message}";
    }
}
