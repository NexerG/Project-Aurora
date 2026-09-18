using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ArctisAurora.Core.UI
{
    // Markdown <-> <Document> tree: one block per line, the subset the note model can hold.
    public static class MarkdownFormat
    {
        // line prefixes
        private static readonly Regex heading = new Regex(@"^(#{1,6}) (.*)$");
        private static readonly Regex task = new Regex(@"^([ \t]*)- \[([ xX])\](?: (.*))?$");
        private static readonly Regex bullet = new Regex(@"^([ \t]*)- (.*)$");
        private const string fence = "```";
        private const string quote = "> ";

        private const string escapable = "\\`*_~#>-[]";

        #region ---- read ----
        public static XElement Read(string text, string name)
        {
            XElement root = new XElement("Document", new XAttribute("Name", name));
            List<int> listWidths = new List<int>();
            bool inFence = false;

            foreach (string raw in text.Split('\n'))
            {
                string line = raw.TrimEnd('\r');

                if (line.StartsWith(fence))
                {
                    inFence = !inFence;
                    continue;
                }

                if (inFence)
                {
                    root.Add(Block("Code", new List<XElement> { PlainRun(line) }));
                    continue;
                }

                Match match;
                if ((match = task.Match(line)).Success || (match = bullet.Match(line)).Success)
                {
                    bool isTask = match.Groups.Count == 4;
                    XElement block = Block(null, ParseInline(isTask ? match.Groups[3].Value : match.Groups[2].Value));
                    block.SetAttributeValue("List", isTask ? "Task" : "Bullet");

                    int level = ListLevel(listWidths, match.Groups[1].Value);
                    if (level > 0) block.SetAttributeValue("Level", level);
                    if (isTask && match.Groups[2].Value != " ") block.SetAttributeValue("Checked", "true");

                    root.Add(block);
                    continue;
                }

                listWidths.Clear();

                if ((match = heading.Match(line)).Success)
                    root.Add(Block("Heading" + match.Groups[1].Length, ParseInline(match.Groups[2].Value)));
                else if (line.StartsWith(quote))
                    root.Add(Block("Quote", ParseInline(line[quote.Length..])));
                else
                    root.Add(Block(null, ParseInline(line)));
            }

            return root;
        }

        // Nesting depth from the indent, relative to the list items above it.
        private static int ListLevel(List<int> widths, string indent)
        {
            int width = 0;
            foreach (char c in indent) width += c == '\t' ? 4 : 1;

            while (widths.Count > 0 && widths[^1] > width) widths.RemoveAt(widths.Count - 1);
            if (widths.Count > 0 && widths[^1] == width) return widths.Count - 1;

            widths.Add(width);
            return widths.Count - 1;
        }

        private static XElement Block(string? styling, List<XElement> runs)
        {
            XElement block = new XElement("Block");
            if (styling != null) block.SetAttributeValue("StylingType", styling);

            if (runs.Count == 0) runs.Add(PlainRun(string.Empty));
            block.Add(runs);
            return block;
        }

        private static XElement PlainRun(string text)
        {
            XElement run = new XElement("Run");
            if (text.Length > 0) run.SetAttributeValue("Text", text);
            return run;
        }

        private static List<XElement> ParseInline(string s)
        {
            List<XElement> runs = new List<XElement>();
            StringBuilder pending = new StringBuilder();
            bool bold = false, italic = false, strike = false;

            void Flush(bool code)
            {
                if (pending.Length == 0) return;

                XElement run = PlainRun(pending.ToString());
                if (bold) run.SetAttributeValue("Bold", "true");
                if (italic) run.SetAttributeValue("Italic", "true");
                if (strike) run.SetAttributeValue("Strikethrough", "true");
                if (code) run.SetAttributeValue("StylingType", "Code");
                runs.Add(run);
                pending.Clear();
            }

            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];

                if (c == '\\' && i + 1 < s.Length && escapable.IndexOf(s[i + 1]) >= 0)
                {
                    pending.Append(s[++i]);
                    continue;
                }

                if (c == '`')
                {
                    int close = s.IndexOf('`', i + 1);
                    if (close > i + 1)
                    {
                        Flush(false);
                        pending.Append(s, i + 1, close - i - 1);
                        Flush(true);
                        i = close;
                        continue;
                    }
                }
                else if (c == '~' && At(s, i, "~~") && (strike || s.IndexOf("~~", i + 2) >= 0))
                {
                    Flush(false);
                    strike = !strike;
                    i++;
                    continue;
                }
                else if (c == '*' && At(s, i, "**") && (bold || s.IndexOf("**", i + 2) >= 0))
                {
                    Flush(false);
                    bold = !bold;
                    i++;
                    continue;
                }
                else if ((c == '*' && (italic || s.IndexOf('*', i + 1) >= 0))
                      || (c == '_' && (italic ? !WordAt(s, i + 1) : !WordAt(s, i - 1) && s.IndexOf('_', i + 1) >= 0)))
                {
                    Flush(false);
                    italic = !italic;
                    continue;
                }

                pending.Append(c);
            }

            Flush(false);
            return runs;
        }

        private static bool At(string s, int i, string marker) => string.CompareOrdinal(s, i, marker, 0, marker.Length) == 0;

        private static bool WordAt(string s, int i) => i >= 0 && i < s.Length && char.IsLetterOrDigit(s[i]);
        #endregion

        #region ---- write ----
        public static string Write(XElement document)
        {
            List<string> lines = new List<string>();
            bool inFence = false;

            foreach (XElement block in document.Elements())
            {
                if (block.Name.LocalName != "Block") continue;

                string styling = (string?)block.Attribute("StylingType") ?? "Text";
                List<XElement> runs = block.Elements().ToList();

                if ((styling == "Code") != inFence)
                {
                    lines.Add(fence);
                    inFence = !inFence;
                }

                lines.Add(inFence ? Flatten(runs, out _) : BlockLine(block, styling, runs));
            }

            if (inFence) lines.Add(fence);
            return string.Join("\n", lines);
        }

        private static string BlockLine(XElement block, string styling, List<XElement> runs)
        {
            string content = WriteInline(runs);
            string? list = (string?)block.Attribute("List");

            if (list == "Bullet" || list == "Task")
            {
                string indent = new string('\t', (int?)block.Attribute("Level") ?? 0);
                if (list == "Task")
                    return indent + ((bool?)block.Attribute("Checked") == true ? "- [x] " : "- [ ] ") + content;

                return indent + "- " + (task.IsMatch("- " + content) ? "\\" + content : content);
            }

            if (styling.StartsWith("Heading") && int.TryParse(styling["Heading".Length..], out int level))
                return new string('#', level) + " " + content;

            if (styling == "Quote") return quote + content;

            bool structural = heading.IsMatch(content) || bullet.IsMatch(content)
                              || content.StartsWith(quote) || content.StartsWith(fence);
            if (!structural) return content;

            int at = content.Length - content.TrimStart(' ', '\t').Length;
            return content.Insert(at, "\\");
        }

        // Unescaped when it reads back as the same runs, escaped when it would not.
        private static string WriteInline(List<XElement> runs)
        {
            string plain = Compose(runs, false);
            string text = Flatten(runs, out List<int> styles);
            string reread = Flatten(ParseInline(plain), out List<int> rereadStyles);

            return reread == text && rereadStyles.SequenceEqual(styles) ? plain : Compose(runs, true);
        }

        private static string Compose(List<XElement> runs, bool escape)
        {
            StringBuilder line = new StringBuilder();
            List<string> open = new List<string>();

            foreach (XElement run in runs)
            {
                string text = (string?)run.Attribute("Text") ?? string.Empty;
                if (text.Length == 0) continue;

                List<string> want = new List<string>();
                if ((bool?)run.Attribute("Strikethrough") == true) want.Add("~~");
                if ((bool?)run.Attribute("Bold") == true) want.Add("**");
                if ((bool?)run.Attribute("Italic") == true) want.Add("*");

                int keep = 0;
                while (keep < open.Count && want.Contains(open[keep])) keep++;
                for (int i = open.Count - 1; i >= keep; i--) line.Append(open[i]);
                open.RemoveRange(keep, open.Count - keep);

                foreach (string marker in want)
                    if (!open.Contains(marker))
                    {
                        line.Append(marker);
                        open.Add(marker);
                    }

                if ((string?)run.Attribute("StylingType") == "Code")
                    line.Append('`').Append(text).Append('`');
                else if (escape)
                    for (int i = 0; i < text.Length; i++)
                    {
                        char c = text[i];
                        bool needs = c == '\\'
                            ? i + 1 == text.Length || escapable.IndexOf(text[i + 1]) >= 0
                            : "`*_~".IndexOf(c) >= 0;
                        if (needs) line.Append('\\');
                        line.Append(c);
                    }
                else
                    line.Append(text);
            }

            for (int i = open.Count - 1; i >= 0; i--) line.Append(open[i]);
            return line.ToString();
        }

        // The runs' text, and one style mask per character.
        private static string Flatten(List<XElement> runs, out List<int> styles)
        {
            StringBuilder text = new StringBuilder();
            styles = new List<int>();

            foreach (XElement run in runs)
            {
                string slice = (string?)run.Attribute("Text") ?? string.Empty;
                int mask = ((bool?)run.Attribute("Bold") == true ? 1 : 0)
                         | ((bool?)run.Attribute("Italic") == true ? 2 : 0)
                         | ((bool?)run.Attribute("Strikethrough") == true ? 4 : 0)
                         | ((string?)run.Attribute("StylingType") == "Code" ? 8 : 0);

                text.Append(slice);
                for (int i = 0; i < slice.Length; i++) styles.Add(mask);
            }

            return text.ToString();
        }
        #endregion
    }

    // Plain text <-> <Document> tree: one unstyled block per line.
    public static class PlainTextFormat
    {
        public static XElement Read(string text, string name)
        {
            XElement root = new XElement("Document", new XAttribute("Name", name));

            foreach (string line in text.Split('\n'))
            {
                string content = line.TrimEnd('\r');
                XElement run = new XElement("Run");
                if (content.Length > 0) run.SetAttributeValue("Text", content);
                root.Add(new XElement("Block", run));
            }

            return root;
        }

        public static string Write(XElement document)
        {
            StringBuilder text = new StringBuilder();
            bool first = true;

            foreach (XElement block in document.Elements())
            {
                if (block.Name.LocalName != "Block") continue;

                if (!first) text.Append('\n');
                first = false;

                foreach (XElement run in block.Elements())
                    text.Append((string?)run.Attribute("Text"));
            }

            return text.ToString();
        }
    }
}
