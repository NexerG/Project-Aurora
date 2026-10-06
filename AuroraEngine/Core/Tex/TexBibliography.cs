using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ArctisAurora.Core.Tex
{
    // One @type{key, field = value, …} of a .bib database; field names lower case.
    public sealed class TexBibEntry
    {
        public readonly string type;
        public readonly string key;
        public readonly Dictionary<string, string> fields = new Dictionary<string, string>();

        public TexBibEntry(string type, string key)
        {
            this.type = type;
            this.key = key;
        }

        public string? this[string field] => fields.TryGetValue(field, out string? value) && value.Length > 0 ? value : null;
    }

    // BibTeX: a .bib database read, and cited entries written as a thebibliography in a standard style.
    public static class TexBibliography
    {
        public static readonly HashSet<string> styles = new HashSet<string> { "plain", "unsrt", "abbrv", "alpha" };

        private static readonly string[] monthKeys = { "jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec" };

        private readonly record struct Name(List<string> first, List<string> von, List<string> last, List<string> jr);

        #region ---- reading ----
        public static void Parse(string text, List<TexBibEntry> into, Action<string> error)
        {
            Dictionary<string, string> strings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int m = 0; m < monthKeys.Length; m++)
                strings[monthKeys[m]] = CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(m + 1);

            int i = 0;
            while ((i = text.IndexOf('@', i)) >= 0)
            {
                i++;
                string type = Identifier(text, ref i).ToLowerInvariant();
                SkipSpace(text, ref i);
                if (type == "comment") continue;
                if (i >= text.Length || text[i] is not ('{' or '('))
                {
                    error($"I was expecting a `{{' or a `(' on line {Line(text, i)}");
                    continue;
                }
                char close = text[i] == '{' ? '}' : ')';
                i++;

                if (type == "preamble")
                {
                    Value(text, ref i, strings, error);
                    SkipPast(text, ref i, close);
                    continue;
                }
                if (type == "string")
                {
                    SkipSpace(text, ref i);
                    string name = Identifier(text, ref i);
                    SkipSpace(text, ref i);
                    if (i < text.Length && text[i] == '=')
                    {
                        i++;
                        strings[name] = Value(text, ref i, strings, error);
                    }
                    else error($"I was expecting an `=' on line {Line(text, i)}");
                    SkipPast(text, ref i, close);
                    continue;
                }

                SkipSpace(text, ref i);
                int keyStart = i;
                while (i < text.Length && text[i] != ',' && text[i] != close && !char.IsWhiteSpace(text[i])) i++;
                TexBibEntry entry = new TexBibEntry(type, text[keyStart..i]);
                while (true)
                {
                    SkipSpace(text, ref i);
                    if (i >= text.Length)
                    {
                        error($"End of file in entry {entry.key}");
                        break;
                    }
                    if (text[i] == ',')
                    {
                        i++;
                        continue;
                    }
                    if (text[i] == close)
                    {
                        i++;
                        break;
                    }

                    string field = Identifier(text, ref i).ToLowerInvariant();
                    SkipSpace(text, ref i);
                    if (field.Length == 0 || i >= text.Length || text[i] != '=')
                    {
                        error($"I was expecting a field name and `=' on line {Line(text, i)}");
                        SkipPast(text, ref i, close);
                        break;
                    }
                    i++;
                    entry.fields[field] = Value(text, ref i, strings, error);
                }
                into.Add(entry);
            }
        }

        // {…}, "…", digits or a @string name, joined by #; whitespace runs become one space.
        private static string Value(string text, ref int i, Dictionary<string, string> strings, Action<string> error)
        {
            StringBuilder value = new StringBuilder();
            while (true)
            {
                SkipSpace(text, ref i);
                if (i >= text.Length) break;
                char c = text[i];
                if (c is '{' or '"') value.Append(Delimited(text, ref i, c == '{' ? '}' : '"'));
                else if (char.IsDigit(c))
                {
                    int start = i;
                    while (i < text.Length && char.IsDigit(text[i])) i++;
                    value.Append(text, start, i - start);
                }
                else
                {
                    string name = Identifier(text, ref i);
                    if (name.Length == 0)
                    {
                        error($"I was expecting a field value on line {Line(text, i)}");
                        break;
                    }
                    if (strings.TryGetValue(name, out string? defined)) value.Append(defined);
                    else error($"Warning--string name \"{name}\" is undefined");
                }
                SkipSpace(text, ref i);
                if (i < text.Length && text[i] == '#')
                {
                    i++;
                    continue;
                }
                break;
            }
            return string.Join(' ', value.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        }

        // What lies between an opening { or " at i and its closing mark, braces balanced.
        private static string Delimited(string text, ref int i, char close)
        {
            int start = ++i, depth = 0;
            for (; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '{') depth++;
                else if (c == '}' && depth > 0) depth--;
                else if (c == close && depth == 0) return text[start..i++];
            }
            return text[start..];
        }

        private static string Identifier(string text, ref int i)
        {
            int start = i;
            while (i < text.Length && !char.IsWhiteSpace(text[i]) && "{}(),=\"#%'@".IndexOf(text[i]) < 0) i++;
            return text[start..i];
        }

        private static void SkipSpace(string text, ref int i)
        {
            while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
        }

        private static void SkipPast(string text, ref int i, char close)
        {
            int depth = 0;
            for (; i < text.Length; i++)
            {
                if (text[i] == '{') depth++;
                else if (text[i] == '}' && depth > 0) depth--;
                else if (text[i] == close && depth == 0)
                {
                    i++;
                    return;
                }
            }
        }

        private static int Line(string text, int i) => text.AsSpan(0, Math.Min(i, text.Length)).Count('\n') + 1;
        #endregion

        #region ---- formatting ----
        // The entries as LaTeX: \begin{thebibliography}, a \bibitem each, sorted unless unsrt; alpha gives labels.
        public static string Format(List<TexBibEntry> cited, string style)
        {
            bool alpha = style == "alpha", abbreviate = style == "abbrv";
            List<(TexBibEntry entry, string label, string sort)> items = cited
                .Select(e => (e, alpha ? AlphaLabel(e) : "", SortKey(e))).ToList();

            if (alpha)
            {
                items = items.OrderBy(x => Purify(x.label) + "    " + x.sort, StringComparer.Ordinal).ToList();
                foreach (IGrouping<string, int> same in Enumerable.Range(0, items.Count).GroupBy(n => items[n].label).Where(g => g.Count() > 1))
                {
                    char suffix = 'a';
                    foreach (int n in same)
                        items[n] = (items[n].entry, items[n].label + suffix++, items[n].sort);
                }
            }
            else if (style != "unsrt") items = items.OrderBy(x => x.sort, StringComparer.Ordinal).ToList();

            string widest = alpha ? items.Select(x => x.label).DefaultIfEmpty("").MaxBy(l => l.Length)! : items.Count.ToString(CultureInfo.InvariantCulture);
            StringBuilder bbl = new StringBuilder();
            bbl.Append("\\begin{thebibliography}{").Append(widest).Append("}\n\n");
            foreach ((TexBibEntry entry, string label, _) in items)
            {
                bbl.Append("\\bibitem");
                if (alpha) bbl.Append('[').Append(label).Append(']');
                bbl.Append('{').Append(entry.key).Append("}\n");
                bbl.Append(string.Join("\n\\newblock ", Blocks(entry, abbreviate).Select(Period))).Append("\n\n");
            }
            bbl.Append("\\end{thebibliography}\n");
            return bbl.ToString();
        }

        // plain.bst's blocks for each entry type, each a sentence.
        private static List<string> Blocks(TexBibEntry e, bool abbreviate)
        {
            List<string> blocks = new List<string>();
            string? authors = Names(e["author"], abbreviate);
            string? editors = Names(e["editor"], abbreviate) is string names ? names + (NameCount(e["editor"]) > 1 ? ", editors" : ", editor") : null;
            string? inBook = editors != null ? Join(", ", "In " + editors, Em(e["booktitle"])) : e["booktitle"] != null ? "In " + Em(e["booktitle"]) : null;
            string? date = Join(" ", e["month"], e["year"]);
            string? title = e["title"] is string t ? SentenceCase(t) : null;
            string? emTitle = Em(e["title"]);
            string? edition = e["edition"] is string ed ? ed.ToLowerInvariant() + " edition" : null;

            void Add(params string?[] parts)
            {
                if (Join(", ", parts) is string block) blocks.Add(block);
            }

            switch (e.type)
            {
                case "article":
                    Add(authors);
                    Add(title);
                    Add(Em(e["journal"]), JournalVolume(e), date);
                    break;
                case "book":
                    Add(authors ?? editors);
                    Add(emTitle, Series(e));
                    Add(e["publisher"], e["address"], edition, date);
                    break;
                case "booklet":
                    Add(authors);
                    Add(title);
                    Add(e["howpublished"], e["address"], date);
                    break;
                case "inbook":
                    Add(authors ?? editors);
                    Add(emTitle, Series(e), e["chapter"] is string chapter ? "chapter " + chapter : null, Pages(e["pages"]));
                    Add(e["publisher"], e["address"], edition, date);
                    break;
                case "incollection":
                    Add(authors);
                    Add(title);
                    Add(inBook, Series(e), e["chapter"] is string inChapter ? "chapter " + inChapter : null, Pages(e["pages"]));
                    Add(e["publisher"], e["address"], edition, date);
                    break;
                case "inproceedings" or "conference":
                    Add(authors);
                    Add(title);
                    Add(inBook, Series(e), Pages(e["pages"]), e["address"], date);
                    Add(e["organization"], e["publisher"]);
                    break;
                case "manual":
                    Add(authors ?? e["organization"]);
                    Add(emTitle);
                    Add(authors != null ? e["organization"] : null, e["address"], edition, date);
                    break;
                case "mastersthesis":
                    Add(authors);
                    Add(title);
                    Add(e["type"] ?? "Master's thesis", e["school"], e["address"], date);
                    break;
                case "phdthesis":
                    Add(authors);
                    Add(emTitle);
                    Add(e["type"] ?? "PhD thesis", e["school"], e["address"], date);
                    break;
                case "proceedings":
                    Add(editors ?? e["organization"]);
                    Add(emTitle, Series(e));
                    Add(e["address"], date);
                    Add(editors != null ? e["organization"] : null, e["publisher"]);
                    break;
                case "techreport":
                    Add(authors);
                    Add(title);
                    Add(Join(" ", e["type"] ?? "Technical Report", e["number"]), e["institution"], e["address"], date);
                    break;
                case "unpublished":
                    Add(authors);
                    Add(title);
                    Add(e["note"], date);
                    return blocks;
                default:
                    Add(authors);
                    Add(title);
                    Add(e["howpublished"], date);
                    break;
            }
            Add(e["note"]);
            return blocks;
        }

        // volume(number):pages, or pages alone
        private static string? JournalVolume(TexBibEntry e)
        {
            if (e["volume"] is not string volume) return Pages(e["pages"]);
            string text = volume + (e["number"] is string number ? "(" + number + ")" : "");
            return e["pages"] is string pages ? text + ":" + Dashed(pages) : text;
        }

        private static string? Series(TexBibEntry e)
        {
            if (e["volume"] is string volume) return "volume " + volume + (e["series"] is string series ? " of " + Em(series) : "");
            if (e["number"] is string number) return "number " + number + (e["series"] is string inSeries ? " in " + inSeries : "");
            return e["series"];
        }

        private static string? Pages(string? pages)
        {
            if (pages == null) return null;
            return (pages.IndexOfAny(new[] { '-', ',', '+' }) >= 0 ? "pages " : "page ") + Dashed(pages);
        }

        private static string Dashed(string pages) => Regex.Replace(pages, @"\s*-+\s*", "--");

        private static string? Em(string? text) => text == null ? null : "{\\em " + text + "}";

        private static string? Join(string separator, params string?[] parts)
        {
            string[] present = parts.Where(p => !string.IsNullOrEmpty(p)).Select(p => p!).ToArray();
            return present.Length == 0 ? null : string.Join(separator, present);
        }

        private static string Period(string block) => block.TrimEnd('}') is { Length: > 0 } bare && ".?!".IndexOf(bare[^1]) >= 0 ? block : block + ".";

        // BibTeX's "t": lower case but the first letter, the one after a colon and anything braced.
        private static string SentenceCase(string title)
        {
            StringBuilder text = new StringBuilder();
            int depth = 0;
            bool keep = true;
            foreach (char c in title)
            {
                if (c == '{')
                {
                    depth++;
                    keep = false;
                }
                else if (c == '}') depth = Math.Max(0, depth - 1);
                if (depth > 0 || c == '}' || char.IsWhiteSpace(c))
                {
                    text.Append(c);
                    continue;
                }
                text.Append(keep ? c : char.ToLowerInvariant(c));
                keep = c == ':';
            }
            return text.ToString();
        }
        #endregion

        #region ---- names ----
        private static List<string> SplitNames(string field)
        {
            List<string> names = new List<string>();
            List<string> words = Words(field, out _);
            List<string> current = new List<string>();
            foreach (string word in words)
            {
                if (word.Equals("and", StringComparison.OrdinalIgnoreCase))
                {
                    names.Add(string.Join(' ', current));
                    current.Clear();
                }
                else current.Add(word);
            }
            names.Add(string.Join(' ', current));
            return names.Where(n => n.Length > 0).ToList();
        }

        private static int NameCount(string? field) => field == null ? 0 : SplitNames(field).Count;

        // Words split on spaces outside braces; commas outside braces are words of their own.
        private static List<string> Words(string text, out int commas)
        {
            List<string> words = new List<string>();
            StringBuilder word = new StringBuilder();
            int depth = 0;
            commas = 0;
            void Flush()
            {
                if (word.Length > 0) words.Add(word.ToString());
                word.Clear();
            }

            foreach (char c in text)
            {
                if (c == '{') depth++;
                else if (c == '}') depth = Math.Max(0, depth - 1);
                if (depth == 0 && (char.IsWhiteSpace(c) || c == '~')) Flush();
                else if (depth == 0 && c == ',')
                {
                    Flush();
                    words.Add(",");
                    commas++;
                }
                else word.Append(c);
            }
            Flush();
            return words;
        }

        // First von Last; von Last, First; von Last, Jr, First.
        private static Name ParseName(string text)
        {
            List<string> words = Words(text, out int commas);
            List<List<string>> parts = new List<List<string>> { new List<string>() };
            foreach (string word in words)
                if (word == ",") parts.Add(new List<string>());
                else parts[^1].Add(word);

            List<string> first = new List<string>(), von = new List<string>(), last = new List<string>(), jr = new List<string>();
            if (commas == 0)
            {
                List<string> all = parts[0];
                int start = all.FindIndex(w => Lower(w));
                if (start >= 0 && start < all.Count - 1)
                {
                    int end = all.FindLastIndex(all.Count - 2, w => Lower(w));
                    first.AddRange(all.Take(start));
                    von.AddRange(all.Skip(start).Take(end - start + 1));
                    last.AddRange(all.Skip(end + 1));
                }
                else
                {
                    first.AddRange(all.Take(Math.Max(0, all.Count - 1)));
                    last.AddRange(all.Skip(Math.Max(0, all.Count - 1)));
                }
            }
            else
            {
                List<string> head = parts[0];
                int end = head.FindLastIndex(Math.Max(0, head.Count - 2), w => Lower(w));
                if (head.Count > 1 && end >= 0 && Lower(head[0])) von.AddRange(head.Take(end + 1));
                last.AddRange(head.Skip(von.Count));
                if (commas >= 2)
                {
                    jr.AddRange(parts[1]);
                    first.AddRange(parts[2]);
                }
                else first.AddRange(parts[1]);
            }
            return new Name(first, von, last, jr);
        }

        private static bool Lower(string word) => word.Length > 0 && char.IsLower(word[0]);

        private static string Display(Name n, bool abbreviate)
        {
            string first = abbreviate ? string.Join("~", n.first.Select(Initial)) : string.Join(" ", n.first);
            string text = string.Join(" ", new[] { first, string.Join(" ", n.von), string.Join(" ", n.last) }.Where(p => p.Length > 0));
            return n.jr.Count > 0 ? text + ", " + string.Join(" ", n.jr) : text;
        }

        // D. for Donald, J.-P. for Jean-Paul, {\'E}. for {\'E}mile.
        private static string Initial(string word) => string.Join("-", word.Split('-').Where(p => p.Length > 0).Select(p =>
        {
            if (p[0] != '{') return p[0] + ".";
            int close = p.IndexOf('}');
            return (close > 0 ? p[..(close + 1)] : p) + ".";
        }));

        // plain.bst's format.names: A; A and B; A, B, and C; "others" last gives et al.
        private static string? Names(string? field, bool abbreviate)
        {
            if (field == null) return null;
            List<string> names = SplitNames(field);
            StringBuilder text = new StringBuilder();
            for (int i = 0; i < names.Count; i++)
            {
                bool others = names[i].Equals("others", StringComparison.OrdinalIgnoreCase);
                string shown = others ? "" : Display(ParseName(names[i]), abbreviate);
                if (i == 0) text.Append(shown);
                else if (i < names.Count - 1) text.Append(", ").Append(shown);
                else
                {
                    if (names.Count > 2) text.Append(',');
                    text.Append(others ? " et~al." : " and " + shown);
                }
            }
            return text.ToString();
        }
        #endregion

        #region ---- sorting and labels ----
        // author (or editor) as von Last First Jr, then year, then title without a leading article
        private static string SortKey(TexBibEntry e)
        {
            string? field = e["author"] ?? e["editor"];
            string names = field == null ? e["organization"] ?? e["key"] ?? e.key : string.Join("   ", SplitNames(field).Select(n =>
            {
                Name name = ParseName(n);
                return string.Join(" ", name.von.Concat(name.last).Concat(name.first).Concat(name.jr));
            }));
            string title = e["title"] ?? "";
            foreach (string article in new[] { "A ", "An ", "The " })
                if (title.StartsWith(article, StringComparison.Ordinal)) title = title[article.Length..];
            return Purify(names) + "    " + Purify(e["year"] ?? "") + "    " + Purify(title);
        }

        // alpha.bst: Knu84 for one author, KR88 for two to four, ABC+90 for more.
        private static string AlphaLabel(TexBibEntry e)
        {
            string? field = e["author"] ?? e["editor"];
            string label;
            if (field == null)
            {
                string from = Purify(e["key"] ?? e["organization"] ?? e.key, true).Replace(" ", "");
                label = from[..Math.Min(3, from.Length)];
            }
            else
            {
                List<string> names = SplitNames(field);
                string Initials(Name n) => string.Concat(n.von.Concat(n.last).Select(w => Purify(w, true).Trim() is { Length: > 0 } p ? p[..1] : ""));
                if (names.Count == 1)
                {
                    Name only = ParseName(names[0]);
                    label = Initials(only);
                    if (label.Length < 2)
                    {
                        string last = Purify(string.Join(" ", only.last), true);
                        label = last[..Math.Min(3, last.Length)];
                    }
                }
                else
                {
                    StringBuilder text = new StringBuilder();
                    int shown = names.Count > 4 ? 3 : names.Count;
                    for (int i = 0; i < shown; i++)
                        text.Append(names[i].Equals("others", StringComparison.OrdinalIgnoreCase) ? "+" : Initials(ParseName(names[i])));
                    if (names.Count > 4) text.Append('+');
                    label = text.ToString();
                }
            }
            string year = Purify(e["year"] ?? "");
            return label + (year.Length >= 2 ? year[^2..] : year);
        }

        // Letters, digits and spaces only, accents' commands dropped; lower case unless kept.
        private static string Purify(string text, bool keepCase = false)
        {
            StringBuilder pure = new StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\\')
                {
                    i++;
                    while (i < text.Length && char.IsLetter(text[i])) i++;
                    if (i < text.Length && !char.IsLetter(text[i]) && !char.IsWhiteSpace(text[i]) && text[i] != '{') continue;
                    i--;
                    continue;
                }
                if (char.IsLetterOrDigit(c)) pure.Append(keepCase ? c : char.ToLowerInvariant(c));
                else if (char.IsWhiteSpace(c) || c is '-' or '~') pure.Append(' ');
            }
            return pure.ToString();
        }
        #endregion
    }
}
