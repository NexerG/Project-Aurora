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
        private static readonly Regex ordered = new Regex(@"^([ \t]*)(\d{1,9})[.)] (.*)$");
        private const string fence = "```";
        private const string quote = "> ";

        private const string escapable = "\\`*_~#>-+[]=<.)";

        // inline HTML Obsidian renders, read and written for what Markdown has no syntax for
        private static readonly Regex htmlTag = new Regex(@"\G<(/?)(u|mark|span)((?:\s+style=""[^""]*"")?)\s*>", RegexOptions.IgnoreCase);
        private static readonly Regex cssColor = new Regex(@"(?<![-\w])color\s*:\s*(#[0-9a-fA-F]{6}(?:[0-9a-fA-F]{2})?)");
        private static readonly Regex cssBackground = new Regex(@"(?<![-\w])background(?:-color)?\s*:\s*(#[0-9a-fA-F]{6}(?:[0-9a-fA-F]{2})?)");

        // what ==text== stands for
        public const string DefaultHighlightHex = "#FFF3A3";

        // list markers written as numbers
        private static readonly string[] numbered = { "Decimal", "UpperAlpha", "LowerAlpha", "LowerRoman", "UpperRoman" };

        // frontmatter key -> the tree attribute it stands for, on <Document>, <DocumentLayout> or <Page>
        private static readonly (string key, string element, string attribute)[] properties =
        {
            ("Palette", "Document", "Palette"),
            ("Created", "Document", "Created"),
            ("Modified", "Document", "Modified"),
            ("LineHeight", "DocumentLayout", "LineHeight"),
            ("BlockSpacing", "DocumentLayout", "BlockSpacing"),
            ("ListIndent", "DocumentLayout", "ListIndent"),
            ("PageMode", "Page", "Mode"),
            ("PageSize", "Page", "Size"),
            ("Landscape", "Page", "Landscape"),
            ("PageWidth", "Page", "Width"),
            ("PageHeight", "Page", "Height"),
            ("MarginTop", "Page", "MarginTop"),
            ("MarginBottom", "Page", "MarginBottom"),
            ("MarginLeft", "Page", "MarginLeft"),
            ("MarginRight", "Page", "MarginRight"),
            ("PageGap", "Page", "Gap")
        };

        // Whether a frontmatter key is one the note reads into its own properties.
        public static bool IsProperty(string key) =>
            properties.Any(p => string.Equals(p.key, key, StringComparison.OrdinalIgnoreCase));

        #region ---- read ----
        public static XElement Read(string text, string name)
        {
            XElement root = new XElement("Document", new XAttribute("Name", name));
            List<int> listWidths = new List<int>();

            if (Frontmatter.Split(text, out string front, out string body))
            {
                root.SetAttributeValue("Frontmatter", front);
                ReadProperties(root, front);
                text = body;
            }
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

                if ((match = ordered.Match(line)).Success)
                {
                    XElement block = Block(null, ParseInline(match.Groups[3].Value));
                    block.SetAttributeValue("List", "Bullet");
                    block.SetAttributeValue("Marker", "Decimal");

                    int level = ListLevel(listWidths, match.Groups[1].Value);
                    if (level > 0) block.SetAttributeValue("Level", level);

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

        private static void ReadProperties(XElement root, string block)
        {
            foreach ((string key, string element, string attribute) in properties)
            {
                string? value = Frontmatter.Get(block, key);
                if (value != null) PropertyHolder(root, element, true)!.SetAttributeValue(attribute, value);
            }
        }

        // The element a property lives on, made on the way when create is set.
        private static XElement? PropertyHolder(XElement root, string element, bool create)
        {
            if (element == "Document") return root;

            XElement? layout = root.Elements().FirstOrDefault(e => e.Name.LocalName == "DocumentLayout");
            if (layout == null && create) root.AddFirst(layout = new XElement("DocumentLayout"));
            if (layout == null || element == "DocumentLayout") return layout;

            XElement? page = layout.Elements().FirstOrDefault(e => e.Name.LocalName == "Page");
            if (page == null && create) layout.Add(page = new XElement("Page"));
            return page;
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
            bool bold = false, italic = false, strike = false, marked = false;
            string? color = null, highlight = null;

            // open HTML tags, each with the colours it replaced
            Stack<(string tag, string? color, string? highlight)> tags = new Stack<(string, string?, string?)>();

            void Flush(bool code)
            {
                if (pending.Length == 0) return;

                XElement run = PlainRun(pending.ToString());
                if (bold) run.SetAttributeValue("Bold", "true");
                if (italic) run.SetAttributeValue("Italic", "true");
                if (strike) run.SetAttributeValue("Strikethrough", "true");
                if (tags.Any(t => t.tag == "u")) run.SetAttributeValue("Underline", "true");
                if (color != null) run.SetAttributeValue("ColorHex", color);
                string? shown = highlight ?? (marked ? DefaultHighlightHex : null);
                if (shown != null) run.SetAttributeValue("HighlightHex", shown);
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

                if (c == '<' && htmlTag.Match(s, i) is { Success: true } tag)
                {
                    string name = tag.Groups[2].Value.ToLowerInvariant();
                    bool closing = tag.Groups[1].Length > 0;

                    if (closing && tags.Count > 0 && tags.Peek().tag == name)
                    {
                        Flush(false);
                        (_, color, highlight) = tags.Pop();
                        i += tag.Length - 1;
                        continue;
                    }

                    if (!closing && s.IndexOf($"</{name}>", i, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        Flush(false);
                        tags.Push((name, color, highlight));

                        string style = tag.Groups[3].Value;
                        if (name == "mark") highlight = DefaultHighlightHex;
                        if (cssBackground.Match(style) is { Success: true } background) highlight = Hex6(background.Groups[1].Value);
                        if (cssColor.Match(style) is { Success: true } foreground) color = Hex6(foreground.Groups[1].Value);

                        i += tag.Length - 1;
                        continue;
                    }
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
                else if (c == '=' && At(s, i, "==") && (marked || s.IndexOf("==", i + 2) >= 0))
                {
                    Flush(false);
                    marked = !marked;
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

        // The note keeps #RRGGBB; an Obsidian alpha pair is dropped.
        private static string Hex6(string hex) => hex.Length > 7 ? hex[..7].ToUpperInvariant() : hex.ToUpperInvariant();

        private static bool WordAt(string s, int i) => i >= 0 && i < s.Length && char.IsLetterOrDigit(s[i]);
        #endregion

        #region ---- write ----
        public static string Write(XElement document)
        {
            List<string> lines = new List<string>();
            List<(int count, string? marker)> counters = new List<(int, string?)>();
            bool inFence = false;

            foreach (XElement block in document.Elements())
            {
                if (block.Name.LocalName != "Block") continue;

                string styling = (string?)block.Attribute("StylingType") ?? "Text";
                List<XElement> runs = block.Elements().ToList();
                int number = Count(counters, block);

                if ((styling == "Code") != inFence)
                {
                    lines.Add(fence);
                    inFence = !inFence;
                }

                lines.Add(inFence ? Flatten(runs, out _) : BlockLine(block, styling, runs, number));
            }

            if (inFence) lines.Add(fence);

            string? front = (string?)document.Attribute("Frontmatter");
            foreach ((string key, string element, string attribute) in properties)
                front = Frontmatter.Set(front, key, (string?)PropertyHolder(document, element, false)?.Attribute(attribute));

            if (front != null) lines.Insert(0, front);
            else if (lines.Count > 0 && (lines[0] == "---" || lines[0] == "+++")) lines[0] = "\\" + lines[0];

            return string.Join("\n", lines);
        }

        // A list item's number at its level, counted the way DocumentControl.RenumberLists counts.
        private static int Count(List<(int count, string? marker)> counters, XElement block)
        {
            string? list = (string?)block.Attribute("List");
            int level = (int?)block.Attribute("Level") ?? 0;

            if (list == null || list == "None")
            {
                counters.Clear();
                return 0;
            }

            if (counters.Count > level + 1) counters.RemoveRange(level + 1, counters.Count - level - 1);
            if (list == "Task")
            {
                if (counters.Count > level) counters.RemoveRange(level, counters.Count - level);
                return 0;
            }

            string? marker = (string?)block.Attribute("Marker");
            while (counters.Count <= level) counters.Add((0, marker));
            int number = counters[level].marker == marker ? counters[level].count + 1 : 1;
            counters[level] = (number, marker);
            return number;
        }

        private static string BlockLine(XElement block, string styling, List<XElement> runs, int number)
        {
            string content = WriteInline(runs);
            string? list = (string?)block.Attribute("List");

            if (list == "Bullet" || list == "Task")
            {
                string indent = new string('\t', (int?)block.Attribute("Level") ?? 0);
                if (list == "Task")
                    return indent + ((bool?)block.Attribute("Checked") == true ? "- [x] " : "- [ ] ") + content;

                if (numbered.Contains((string?)block.Attribute("Marker")))
                    return indent + number + ". " + content;

                return indent + "- " + (task.IsMatch("- " + content) ? "\\" + content : content);
            }

            if (styling.StartsWith("Heading") && int.TryParse(styling["Heading".Length..], out int level))
                return new string('#', level) + " " + content;

            if (styling == "Quote") return quote + content;

            // "1. " escapes its dot, since a backslash before a digit escapes nothing
            if (ordered.Match(content) is { Success: true } numberLike)
                return content.Insert(numberLike.Groups[1].Length + numberLike.Groups[2].Length, "\\");

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
            string text = Flatten(runs, out List<string> styles);
            string reread = Flatten(ParseInline(plain), out List<string> rereadStyles);

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
                string? highlight = (string?)run.Attribute("HighlightHex");
                string? color = (string?)run.Attribute("ColorHex");
                if (highlight != null)
                    want.Add(string.Equals(highlight, DefaultHighlightHex, StringComparison.OrdinalIgnoreCase)
                        ? "==" : $"<mark style=\"background:{highlight}\">");
                if (color != null) want.Add($"<span style=\"color:{color}\">");
                if ((bool?)run.Attribute("Underline") == true) want.Add("<u>");
                if ((bool?)run.Attribute("Strikethrough") == true) want.Add("~~");
                if ((bool?)run.Attribute("Bold") == true) want.Add("**");
                if ((bool?)run.Attribute("Italic") == true) want.Add("*");

                int keep = 0;
                while (keep < open.Count && want.Contains(open[keep])) keep++;
                for (int i = open.Count - 1; i >= keep; i--) line.Append(Closing(open[i]));
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
                            : "`*_~=<".IndexOf(c) >= 0;
                        if (needs) line.Append('\\');
                        line.Append(c);
                    }
                else
                    line.Append(text);
            }

            for (int i = open.Count - 1; i >= 0; i--) line.Append(Closing(open[i]));
            return line.ToString();
        }

        // A Markdown marker closes with itself, an HTML tag with its end tag.
        private static string Closing(string marker) =>
            marker[0] != '<' ? marker : "</" + marker[1..marker.IndexOfAny(new[] { ' ', '>' })] + ">";

        // The runs' text, and one style key per character.
        private static string Flatten(List<XElement> runs, out List<string> styles)
        {
            StringBuilder text = new StringBuilder();
            styles = new List<string>();

            foreach (XElement run in runs)
            {
                string slice = (string?)run.Attribute("Text") ?? string.Empty;
                int mask = ((bool?)run.Attribute("Bold") == true ? 1 : 0)
                         | ((bool?)run.Attribute("Italic") == true ? 2 : 0)
                         | ((bool?)run.Attribute("Strikethrough") == true ? 4 : 0)
                         | ((string?)run.Attribute("StylingType") == "Code" ? 8 : 0)
                         | ((bool?)run.Attribute("Underline") == true ? 16 : 0);
                string key = $"{mask}|{((string?)run.Attribute("ColorHex"))?.ToUpperInvariant()}|{((string?)run.Attribute("HighlightHex"))?.ToUpperInvariant()}";

                text.Append(slice);
                for (int i = 0; i < slice.Length; i++) styles.Add(key);
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
