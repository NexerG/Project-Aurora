namespace ArctisAurora.Core.Tex
{
    // Characters to tokens by TeX's line states, reading categories as they stand when each character is reached.
    internal sealed class TexReader
    {
        private enum State { NewLine, MidLine, SkipBlanks }

        private readonly string[] lines;
        private readonly Func<char, TexCatcode> catcode;
        private readonly Action<string, int, int> error;

        // the line being read, trailing spaces cut and the end-of-line character appended
        private int lineIndex = -1;
        private string current = string.Empty;
        private int pos;
        private State state;

        // lines read ahead of the source, numbered 0 and below
        private readonly int preludeLines;

        private int Line => lineIndex + 1 - preludeLines;

        public TexReader(string source, string prelude, Func<char, TexCatcode> catcode, Action<string, int, int> error)
        {
            string[] split = source.Split('\n');
            string[] own = split.Length > 1 && split[^1].Length == 0 ? split[..^1] : split;
            string[] before = prelude.Length == 0 ? Array.Empty<string>() : prelude.TrimEnd('\n').Split('\n');
            lines = before.Concat(own).ToArray();
            preludeLines = before.Length;
            this.catcode = catcode;
            this.error = error;
        }

        public bool Next(out TexToken token)
        {
            while (true)
            {
                if (pos >= current.Length && !NextLine())
                {
                    token = default;
                    return false;
                }

                int column = pos + 1;
                char c = current[pos++];
                TexCatcode cat = catcode(c);
                switch (cat)
                {
                    case TexCatcode.Escape:
                        token = ControlSequence(column);
                        return true;
                    case TexCatcode.EndLine:
                        pos = current.Length;
                        if (state == State.NewLine)
                        {
                            token = TexToken.Cs("par", Line, column);
                            return true;
                        }
                        if (state == State.MidLine)
                        {
                            token = TexToken.Char(' ', TexCatcode.Space, Line, column);
                            return true;
                        }
                        continue;
                    case TexCatcode.Space:
                        if (state != State.MidLine) continue;
                        state = State.SkipBlanks;
                        token = TexToken.Char(' ', TexCatcode.Space, Line, column);
                        return true;
                    case TexCatcode.Comment:
                        pos = current.Length;
                        continue;
                    case TexCatcode.Ignored:
                        continue;
                    case TexCatcode.Invalid:
                        error("Text line contains an invalid character", Line, column);
                        continue;
                    default:
                        state = State.MidLine;
                        token = TexToken.Char(c, cat, Line, column);
                        return true;
                }
            }
        }

        // Characters up to a terminator, lines joined by \n; null when the source ends first.
        public string? ReadRaw(string terminator)
        {
            System.Text.StringBuilder text = new System.Text.StringBuilder();
            while (true)
            {
                string body = current.Length > 0 ? current[..^1] : string.Empty;
                int at = pos < body.Length ? body.IndexOf(terminator, pos, StringComparison.Ordinal) : -1;
                if (at >= 0)
                {
                    text.Append(body, pos, at - pos);
                    pos = at + terminator.Length;
                    state = State.MidLine;
                    return text.ToString();
                }
                if (pos < body.Length) text.Append(body, pos, body.Length - pos);
                pos = current.Length;
                if (!NextLine()) return null;
                text.Append('\n');
            }
        }

        public bool ReadRawChar(out char c)
        {
            c = '\0';
            if (pos >= current.Length - 1) return false;
            c = current[pos++];
            state = State.MidLine;
            return true;
        }

        private bool NextLine()
        {
            if (++lineIndex >= lines.Length) return false;

            string raw = lines[lineIndex];
            if (raw.EndsWith('\r')) raw = raw[..^1];
            current = raw.TrimEnd(' ') + "\r";
            pos = 0;
            state = State.NewLine;
            return true;
        }

        private TexToken ControlSequence(int column)
        {
            if (pos >= current.Length) return TexToken.Cs(string.Empty, Line, column);

            char first = current[pos];
            if (catcode(first) == TexCatcode.Letter)
            {
                int start = pos;
                while (pos < current.Length && catcode(current[pos]) == TexCatcode.Letter) pos++;
                state = State.SkipBlanks;
                return TexToken.Cs(current[start..pos], Line, column);
            }

            pos++;
            state = catcode(first) == TexCatcode.Space ? State.SkipBlanks : State.MidLine;
            return TexToken.Cs(first.ToString(), Line, column);
        }
    }
}
