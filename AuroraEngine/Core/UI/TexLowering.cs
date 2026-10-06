using ArctisAurora.Core.Filing;
using ArctisAurora.Core.Registry.Assets;
using ArctisAurora.Core.Tex;
using ArctisAurora.EngineWork.Registry;
using SixLabors.ImageSharp;
using System.Globalization;
using System.Text;
using System.Xml.Linq;

namespace ArctisAurora.Core.UI
{
    // A .tex source typeset and lowered to a <Document> tree: a paragraph per block, a run per stretch of one style.
    public static class TexLowering
    {
        public const string RomanFont = "latin-modern";
        public const string MonoFont = "latin-modern-mono";
        public const string MathFont = "latin-modern-math";

        private const float Unity = 65536f;
        private const float PxPerPt = 96f / 72.27f;
        private const float MmPerPt = 25.4f / 72.27f;

        // \tabcolsep, either side of a column, and the narrowest column a table takes
        private const float TabColSep = 6f * PxPerPt;
        private const float MinColumn = 28f;

        // blockLines, when given, gets each block's source line; 0 = none. folder is where pictures and .bib files are found.
        public static XElement Compile(string source, out List<TexError> errors, List<int>? blockLines = null, string? folder = null)
        {
            TexTypesetter typesetter = new TexTypesetter(source, new TexAtlasMetrics(), folder);
            typesetter.Run();
            errors = typesetter.errors;
            return Lower(typesetter, blockLines);
        }

        public static XElement Lower(TexTypesetter typesetter, List<int>? blockLines = null)
        {
            XElement root = new XElement("Document");
            int normal = Px(typesetter.normalSize);
            Dictionary<int, int> headings = HeadingSizes(typesetter.vlist);
            root.Add(Layout(typesetter, normal, headings));
            TexHyphenator hyphenator = TexHyphenator.English ?? NoPatterns;
            Hyphenate(typesetter.vlist, hyphenator, typesetter.hyphenation);
            foreach (TexFootnote note in typesetter.footnotes)
                Hyphenate(note.vlist, hyphenator, typesetter.hyphenation);

            Spacing spacing = new Spacing
            {
                indent = typesetter.parIndent * PxPerPt,
                display = TexTypesetter.displaySkips[typesetter.sizeOption] * PxPerPt
            };
            foreach (TexNode node in typesetter.vlist)
                LowerVertical(node, root, normal, headings, blockLines, spacing);
            return root;
        }

        // Each footnote the marks in nodes anchor, as a <Footnote> of its blocks after them; only in the document's own flow.
        private static void Footnotes(IEnumerable<TexNode> nodes, XElement root, int normal, Dictionary<int, int> headings, List<int>? blockLines)
        {
            if (root.Name.LocalName != "Document") return;
            foreach (TexMathNode mark in nodes.OfType<TexMathNode>())
            {
                if (mark.note == null) continue;
                XElement group = new XElement("Footnote", new XAttribute("Id", mark.note.id));
                Spacing inNote = new Spacing();
                foreach (TexNode node in mark.note.vlist)
                    LowerVertical(node, group, normal, headings, blockLines, inNote);
                root.Add(group);
            }
        }

        // A float as a <Float Kind Placement> of its blocks; the space and page break pending stay with the next block.
        private static void Float(TexFloat f, XElement root, int normal, Dictionary<int, int> headings, List<int>? blockLines, Spacing spacing)
        {
            XElement group = new XElement("Float", new XAttribute("Kind", f.kind), new XAttribute("Placement", f.placement));
            Spacing inFloat = new Spacing { indent = spacing.indent, display = spacing.display };
            foreach (TexNode node in f.vlist)
                LowerVertical(node, group, normal, headings, blockLines, inFloat);
            if (group.HasElements) root.Add(group);
        }

        // the vertical space waiting for the next block, and \parindent and the display skip, px
        private sealed class Spacing
        {
            public float pending;
            public float indent;
            public float display;

            // the page break waiting for the next block: Page, Clear or none
            public string? pageBreak;
        }

        // A block or table under the space and page break pending above it.
        private static void Place(XElement root, XElement element, Spacing spacing)
        {
            if (spacing.pending > 0f) element.SetAttributeValue("SpaceBefore", Format(spacing.pending));
            if (spacing.pageBreak != null) element.SetAttributeValue("PageBreak", spacing.pageBreak);
            spacing.pending = 0f;
            spacing.pageBreak = null;
            root.Add(element);
        }

        // what Runs leaves on the tree for PageRefs: a \pageref's run, and a label's block and offset
        private sealed record PageRefNote(string key);
        private sealed record LabelSite(string key, int offset);

        // Every \pageref run and label site in a lowered tree, labels by top-level block index; a label in a table stands at the table's top.
        public static TexPageRefs PageRefs(XElement root)
        {
            TexPageRefs found = new TexPageRefs();
            int index = 0;
            foreach (XElement entry in root.Elements().SelectMany(e => e.Name.LocalName is "Footnote" or "Float" ? e.Elements() : new[] { e })
                         .Where(e => e.Name.LocalName is "Block" or "Table"))
            {
                foreach (XElement e in entry.DescendantsAndSelf())
                {
                    foreach (PageRefNote note in e.Annotations<PageRefNote>())
                        found.refs.Add((e, note.key));
                    foreach (LabelSite site in e.Annotations<LabelSite>())
                        found.labels.TryAdd(site.key, (index, e == entry ? site.offset : 0));
                }
                index++;
            }
            return found;
        }

        #region ---- layout ----
        private static XElement Layout(TexTypesetter typesetter, int normal, Dictionary<int, int> headings)
        {
            bool a4 = typesetter.paper == "a4";
            float side = (typesetter.paperWidth - TexTypesetter.textWidths[typesetter.sizeOption] * MmPerPt) / 2f;
            float top = (typesetter.paperHeight - TexTypesetter.TextHeight * MmPerPt) / 2f;

            XElement layout = new XElement("DocumentLayout",
                new XAttribute("LineHeight", "1.2"),
                new XAttribute("BlockSpacing", "0"),
                new XAttribute("ListIndent", Format(normal * 1.875f)),
                new XAttribute("OptimalBreaks", "true"),
                new XElement("Page",
                    new XAttribute("Mode", "Paged"),
                    new XAttribute("Size", a4 ? "A4" : "Letter"),
                    new XAttribute("MarginTop", Format(typesetter.marginTop ?? top)),
                    new XAttribute("MarginBottom", Format(typesetter.marginBottom ?? top)),
                    new XAttribute("MarginLeft", Format(typesetter.marginLeft ?? side)),
                    new XAttribute("MarginRight", Format(typesetter.marginRight ?? side)),
                    new XAttribute("Style", typesetter.pageStyle),
                    new XAttribute("FootnoteSkip", Format(TexTypesetter.footnoteSkips[typesetter.sizeOption] * MmPerPt)),
                    new XAttribute("FloatSep", Format(TexTypesetter.floatSeps[typesetter.sizeOption] * MmPerPt)),
                    new XAttribute("TextFloatSep", Format(TexTypesetter.TextFloatSep * MmPerPt)),
                    new XAttribute("InTextSep", Format(TexTypesetter.floatSeps[typesetter.sizeOption] * MmPerPt)),
                    typesetter.pageStyles.Select(s => PageStyle(s.Key, s.Value))),
                Style("Text", normal, RomanFont),
                Style("Quote", normal, RomanFont),
                Style("Code", normal, MonoFont));
            foreach (KeyValuePair<int, int> heading in headings.OrderBy(h => h.Key))
                layout.Add(Style("Heading" + heading.Key, heading.Value, RomanFont));
            return layout;
        }

        // A page style's slots as text with {page}/{leftmark}/{rightmark}, each in its first character's font.
        private static XElement PageStyle(string name, TexPageStyle style)
        {
            XElement element = new XElement("PageStyle", new XAttribute("Name", name));
            if (style.headRule > 0f) element.SetAttributeValue("HeadRule", Format(style.headRule * PxPerPt));
            if (style.footRule > 0f) element.SetAttributeValue("FootRule", Format(style.footRule * PxPerPt));
            foreach (KeyValuePair<string, TexParagraph> slot in style.slots)
            {
                StringBuilder text = new StringBuilder();
                foreach (TexNode node in slot.Value.list)
                    if (node is TexChar c) text.Append(c.ch switch
                    {
                        TexTypesetter.PageField => "{page}",
                        TexTypesetter.LeftMarkField => "{leftmark}",
                        TexTypesetter.RightMarkField => "{rightmark}",
                        _ => c.ch.ToString()
                    });
                    else if (node is TexGlueNode or TexKern && text.Length > 0 && text[^1] != ' ') text.Append(' ');
                string written = text.ToString().Trim();
                if (written.Length == 0 || slot.Value.list.OfType<TexChar>().FirstOrDefault() is not TexChar first) continue;

                XElement placed = new XElement("Slot", new XAttribute("Place", slot.Key), new XAttribute("Text", written),
                    new XAttribute("FontName", first.style.font.family == TexFamily.Mono ? MonoFont : RomanFont),
                    new XAttribute("FontSize", Px(first.style.font.size)));
                if (first.style.font.bold) placed.SetAttributeValue("Bold", "true");
                if (first.style.font.italic) placed.SetAttributeValue("Italic", "true");
                element.Add(placed);
            }
            return element;
        }

        private static XElement Style(string type, int size, string font) =>
            new XElement("TextStyle", new XAttribute("Type", type), new XAttribute("FontSize", size), new XAttribute("FontName", font));

        // each heading level at the size its first heading is set in
        private static Dictionary<int, int> HeadingSizes(List<TexNode> vlist)
        {
            Dictionary<int, int> sizes = new Dictionary<int, int>();
            foreach (TexNode node in vlist)
                if (node is TexParagraph { style.kind: TexParKind.Heading } p && !sizes.ContainsKey(Level(p)))
                    if (p.list.OfType<TexChar>().FirstOrDefault() is TexChar first) sizes[Level(p)] = Px(first.style.font.size);
            return sizes;
        }
        #endregion

        #region ---- blocks ----
        private static void LowerVertical(TexNode node, XElement root, int normal, Dictionary<int, int> headings, List<int>? blockLines, Spacing spacing)
        {
            switch (node)
            {
                case TexVGlue g:
                    float width = g.glue.width / Unity * PxPerPt;
                    spacing.pending = g.merge ? MathF.Max(spacing.pending, width) : spacing.pending + width;
                    break;
                case TexParagraph p:
                    List<List<TexNode>> lines = Lines(p.list);
                    bool placed = false;
                    for (int i = 0; i < lines.Count; i++)
                    {
                        List<List<TexNode>> pieces = Displays(lines[i]);
                        for (int k = 0; k < pieces.Count; k++)
                        {
                            bool display = pieces[k] is [TexMathNode { display: true }];
                            if (!display && !pieces[k].Any(n => n is not TexGlueNode and not TexKern and not TexPenalty and not TexLabelMark and not TexFloat)) continue;

                            XElement block = Block(p, pieces[k], i == 0 && k == 0, normal, headings);
                            if (p.style.indent && i == 0 && k == 0) block.SetAttributeValue("Indent", Format(spacing.indent));
                            if (!placed)
                            {
                                if (p.pageStyle != null) block.SetAttributeValue("PageStyle", p.pageStyle);
                                if (p.markLeft != null) block.SetAttributeValue("MarkLeft", p.markLeft);
                                if (p.markRight != null) block.SetAttributeValue("MarkRight", p.markRight);
                                placed = true;
                            }
                            if (k > 0) spacing.pending = MathF.Max(spacing.pending, spacing.display);
                            Place(root, block, spacing);
                            blockLines?.Add(p.line);
                        }
                    }
                    Footnotes(p.list, root, normal, headings, blockLines);
                    foreach (TexFloat f in p.list.OfType<TexFloat>())
                        Float(f, root, normal, headings, blockLines, spacing);
                    break;
                case TexFloat f:
                    Float(f, root, normal, headings, blockLines, spacing);
                    break;
                case TexPageBreak b:
                    spacing.pageBreak = b.clear || spacing.pageBreak == "Clear" ? "Clear" : "Page";
                    break;
                case TexRuleNode:
                    Place(root, new XElement("Block", new XAttribute("StylingType", "Rule")), spacing);
                    blockLines?.Add(0);
                    break;
                case TexTable t:
                    Place(root, Table(t, normal, headings), spacing);
                    blockLines?.Add(t.line);
                    Footnotes(t.rows.SelectMany(r => r).SelectMany(c => Flat(c.vlist)).OfType<TexParagraph>().SelectMany(p => p.list), root, normal, headings, blockLines);
                    break;
            }
        }

        // A line cut around its display formulas, each display a piece of its own: TeX breaks the text either side separately.
        private static List<List<TexNode>> Displays(List<TexNode> line)
        {
            List<List<TexNode>> pieces = new List<List<TexNode>> { new List<TexNode>() };
            foreach (TexNode node in line)
                if (node is TexMathNode { display: true })
                {
                    pieces.Add(new List<TexNode> { node });
                    pieces.Add(new List<TexNode>());
                }
                else pieces[^1].Add(node);
            return pieces;
        }

        // a paragraph split at its forced breaks
        private static List<List<TexNode>> Lines(List<TexNode> list)
        {
            List<List<TexNode>> lines = new List<List<TexNode>> { new List<TexNode>() };
            foreach (TexNode node in list)
                if (node is TexPenalty { penalty: <= TexPenalty.Forced }) lines.Add(new List<TexNode>());
                else lines[^1].Add(node);
            return lines;
        }

        private static XElement Block(TexParagraph p, List<TexNode> line, bool first, int normal, Dictionary<int, int> headings)
        {
            XElement block = new XElement("Block");
            TexParStyle s = p.style;
            string font = RomanFont;
            int size = normal;
            switch (s.kind)
            {
                case TexParKind.Heading:
                    block.SetAttributeValue("StylingType", "Heading" + Level(p));
                    size = headings.GetValueOrDefault(Level(p), normal);
                    break;
                case TexParKind.Code:
                    block.SetAttributeValue("StylingType", "Code");
                    font = MonoFont;
                    break;
                case TexParKind.Quote:
                    block.SetAttributeValue("StylingType", "Quote");
                    break;
            }
            if (s.align != TexAlign.Left && s.kind != TexParKind.Code) block.SetAttributeValue("Align", s.align.ToString());

            if (first && s.item && s.list is TexListKind.Itemize or TexListKind.Enumerate)
            {
                block.SetAttributeValue("List", "Bullet");
                block.SetAttributeValue("Level", s.listDepth - 1);
                block.SetAttributeValue("Marker", Marker(s.list, s.listKindDepth));
                if (s.list == TexListKind.Enumerate) block.SetAttributeValue("Start", s.itemNumber);
            }

            Runs(line, block, font, size, s.kind == TexParKind.Code);
            if (!block.HasElements) block.Add(new XElement("Run"));
            return block;
        }

        private static int Level(TexParagraph p) => Math.Clamp(p.style.level, 1, 6);

        private static string Marker(TexListKind kind, int depth) => kind == TexListKind.Enumerate
            ? ((depth - 1) % 4) switch { 0 => "Decimal", 1 => "LowerAlpha", 2 => "LowerRoman", _ => "UpperAlpha" }
            : ((depth - 1) % 4) switch { 0 => "Disc", 1 => "Circle", 2 => "Square", _ => "SquareOutline" };

        // Columns at their widest single-column cell, p{} columns at their width, a merged cell widening its last column.
        private static XElement Table(TexTable t, int normal, Dictionary<int, int> headings)
        {
            int count = Math.Max(t.columns.Count, t.rows.Count == 0 ? 1 : t.rows.Max(r => r.Sum(c => c.span)));
            float[] widths = new float[count];
            List<List<(TexTableCell cell, List<TexNode> nodes, float width)>> rows = t.rows.Select(r => r.Select(c =>
            {
                List<TexNode> nodes = Flat(c.vlist).ToList();
                return (c, nodes, Width(nodes));
            }).ToList()).ToList();

            foreach (List<(TexTableCell cell, List<TexNode> nodes, float width)> row in rows)
            {
                int c = 0;
                foreach ((TexTableCell cell, _, float width) in row)
                {
                    if (cell.span == 1 && c < count) widths[c] = MathF.Max(widths[c], width);
                    c += cell.span;
                }
            }
            for (int i = 0; i < t.columns.Count; i++)
                if (t.columns[i].width > 0) widths[i] = t.columns[i].width / Unity * PxPerPt;
            foreach (List<(TexTableCell cell, List<TexNode> nodes, float width)> row in rows)
            {
                int c = 0;
                foreach ((TexTableCell cell, _, float width) in row)
                {
                    int end = Math.Min(c + cell.span, count);
                    if (cell.span > 1 && end > c)
                    {
                        float have = widths[c..end].Sum() + 2f * TabColSep * (end - c - 1);
                        if (width > have) widths[end - 1] += width - have;
                    }
                    c += cell.span;
                }
            }

            XElement table = new XElement("Table", new XAttribute("Borders", "false"), new XAttribute("Padding", $"{Format(MathF.Round(TabColSep, 2))} 0"));
            if (t.align != TexAlign.Left) table.SetAttributeValue("Align", t.align.ToString());
            for (int i = 0; i < count; i++)
            {
                XElement column = new XElement("Column", new XAttribute("Width", Format(MathF.Max(MinColumn, MathF.Ceiling(widths[i] + 2f * TabColSep)))));
                int left = i < t.vrules.Count ? t.vrules[i] : 0;
                int right = i == t.columns.Count - 1 && t.vrules.Count > t.columns.Count ? t.vrules[t.columns.Count] : 0;
                if (left > 0) column.SetAttributeValue("RuleLeft", left > 1 ? "Double" : "Plain");
                if (right > 0) column.SetAttributeValue("RuleRight", right > 1 ? "Double" : "Plain");
                table.Add(column);
            }

            Dictionary<(int row, int cell, bool above), (TableRule kind, float width, RuleTrim trim)> rules = CellRules(t);
            for (int r = 0; r < rows.Count; r++)
            {
                XElement written = new XElement("Row");
                for (int i = 0; i < rows[r].Count; i++)
                {
                    (TexTableCell cell, List<TexNode> nodes, _) = rows[r][i];
                    XElement element = new XElement("Cell");
                    if (cell.span > 1) element.SetAttributeValue("ColumnSpan", cell.span);
                    foreach (bool above in new[] { true, false })
                    {
                        if (!rules.TryGetValue((r, i, above), out (TableRule kind, float width, RuleTrim trim) rule)) continue;
                        element.SetAttributeValue(above ? "RuleAbove" : "RuleBelow", rule.width > 0f ? $"{rule.kind} {Format(rule.width)}" : rule.kind.ToString());
                        if (rule.trim != RuleTrim.None) element.SetAttributeValue(above ? "TrimAbove" : "TrimBelow", rule.trim.ToString());
                    }
                    Spacing inCell = new Spacing();
                    foreach (TexNode node in nodes)
                        LowerVertical(node, element, normal, headings, null, inCell);
                    written.Add(element);
                }
                table.Add(written);
            }
            return table;
        }

        // Each rule on the cells it runs along: above its row, or under the last; two plain rules at one place are a double.
        private static Dictionary<(int row, int cell, bool above), (TableRule kind, float width, RuleTrim trim)> CellRules(TexTable t)
        {
            Dictionary<(int, int, bool), (TableRule kind, float width, RuleTrim trim)> rules = new Dictionary<(int, int, bool), (TableRule, float, RuleTrim)>();
            if (t.rows.Count == 0) return rules;

            foreach (TexTableRule rule in t.rules)
            {
                bool above = rule.row < t.rows.Count;
                int r = above ? rule.row : t.rows.Count - 1;
                TableRule kind = rule.kind switch
                {
                    TexRuleKind.Heavy => TableRule.Heavy,
                    TexRuleKind.Light => TableRule.Light,
                    TexRuleKind.Cmid => TableRule.Cmid,
                    _ => TableRule.Plain
                };
                for (int i = 0, c = 0; i < t.rows[r].Count; c += t.rows[r][i].span, i++)
                {
                    int end = c + t.rows[r][i].span - 1;
                    if (end < rule.from || c > rule.to) continue;
                    RuleTrim trim = (rule.trimLeft && c <= rule.from ? RuleTrim.Left : RuleTrim.None)
                        | (rule.trimRight && end >= rule.to ? RuleTrim.Right : RuleTrim.None);
                    bool doubled = kind == TableRule.Plain && rules.TryGetValue((r, i, above), out var had) && had.kind == TableRule.Plain;
                    rules[(r, i, above)] = (doubled ? TableRule.Double : kind, rule.width / Unity * PxPerPt, trim);
                }
            }
            return rules;
        }

        // A tabular inside a cell, which a note cannot hold, as a paragraph per row.
        private static IEnumerable<TexNode> Flat(List<TexNode> vlist)
        {
            foreach (TexNode node in vlist)
            {
                if (node is not TexTable inner)
                {
                    yield return node;
                    continue;
                }
                foreach (List<TexTableCell> row in inner.rows)
                {
                    TexParagraph line = new TexParagraph(new TexParStyle { align = TexAlign.Left });
                    foreach (TexParagraph p in row.SelectMany(c => Flat(c.vlist)).OfType<TexParagraph>())
                    {
                        if (line.list.Count > 0 && p.list.OfType<TexChar>().FirstOrDefault() is TexChar first) line.list.Add(new TexChar(' ', first.style));
                        line.list.AddRange(p.list);
                    }
                    yield return line;
                }
            }
        }
        #endregion

        #region ---- hyphenation ----
        private static readonly TexHyphenator NoPatterns = new TexHyphenator(Array.Empty<string>(), Array.Empty<string>());

        // Soft hyphens into every word of a paragraph that is not code, cells included.
        private static void Hyphenate(List<TexNode> vlist, TexHyphenator hyphenator, Dictionary<string, bool[]> extra)
        {
            foreach (TexNode node in vlist)
                switch (node)
                {
                    case TexParagraph { style.kind: not TexParKind.Code } p:
                        HyphenateWords(p.list, hyphenator, extra);
                        foreach (TexFloat f in p.list.OfType<TexFloat>())
                            Hyphenate(f.vlist, hyphenator, extra);
                        break;
                    case TexFloat f:
                        Hyphenate(f.vlist, hyphenator, extra);
                        break;
                    case TexTable t:
                        foreach (List<TexTableCell> row in t.rows)
                            foreach (TexTableCell cell in row)
                                Hyphenate(cell.vlist, hyphenator, extra);
                        break;
                }
        }

        // A word is a run of letters in one style, not monospaced; one holding a soft hyphen already is left as written.
        private static void HyphenateWords(List<TexNode> list, TexHyphenator hyphenator, Dictionary<string, bool[]> extra)
        {
            StringBuilder letters = new StringBuilder();
            List<int> starts = new List<int>();
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] is not TexChar { style.font.family: not TexFamily.Mono } first || !char.IsLetter(first.ch)) continue;

                int end = i;
                bool marked = false, plain = true;
                letters.Clear();
                starts.Clear();
                while (end < list.Count && list[end] is TexChar c && c.style == first.style && (char.IsLetter(c.ch) || c.ch == TextMeasurer.SoftHyphen))
                {
                    marked |= c.ch == TextMeasurer.SoftHyphen;
                    starts.Add(letters.Length);
                    plain &= Append(letters, c.ch);
                    end++;
                }

                int inserted = 0;
                if (!marked && plain && hyphenator.Points(letters.ToString(), extra) is bool[] points)
                    for (int k = end - 1; k > i; k--)
                        if (points[starts[k - i]])
                        {
                            list.Insert(k, new TexChar(TextMeasurer.SoftHyphen, first.style));
                            inserted++;
                        }
                i = end + inserted - 1;
            }
        }

        // A letter as the patterns spell it, ligatures taken apart; false outside a-z.
        private static bool Append(StringBuilder letters, char c)
        {
            if (c is >= 'a' and <= 'z' or >= 'A' and <= 'Z') letters.Append(char.ToLowerInvariant(c));
            else if (c switch { 'ﬀ' => "ff", 'ﬁ' => "fi", 'ﬂ' => "fl", 'ﬃ' => "ffi", 'ﬄ' => "ffl", _ => null } is string parts) letters.Append(parts);
            else return false;
            return true;
        }
        #endregion

        #region ---- measuring ----
        // The widest line of a cell's paragraphs, px.
        private static float Width(List<TexNode> vlist)
        {
            float widest = 0f;
            foreach (TexParagraph p in vlist.OfType<TexParagraph>())
                foreach (List<TexNode> line in Lines(p.list))
                    widest = MathF.Max(widest, Width(line, 0));
            return widest;
        }

        private static float Width(List<TexNode> nodes, int depth)
        {
            float width = 0f;
            foreach (TexNode node in nodes)
                width += node switch
                {
                    TexChar c => Advance(c.ch, c.style),
                    TexGlueNode { interword: true } g => Advance(' ', g.style),
                    TexGlueNode g => g.glue.width / Unity * PxPerPt,
                    TexKern k => k.width / Unity * PxPerPt,
                    TexHBox b when depth < 16 => Width(b.list, depth + 1),
                    TexRefNode r => r.text.Sum(c => Advance(c, r.style)),
                    TexMathNode m => MathWidth(m),
                    TexImage i => PictureSize(i).width is > 0f and float w ? w : Native(i.path).width,
                    _ => 0f
                };
            return width;
        }

        private static float Advance(char c, TexStyle s)
        {
            int px = Px(s.font.size);
            if (c == TextMeasurer.SoftHyphen) return 0f;
            if (Font(s.font.family == TexFamily.Mono ? MonoFont : RomanFont) is not FontAsset font || c >= AtlasMetaData.AdvanceTableSize) return px * 0.5f;
            return font.atlasMetaData.TableAdvance(c, Face(font.atlasMetaData, s.font)) * px;
        }

        private static float MathWidth(TexMathNode m)
        {
            int px = Px(m.style.font.size);
            if (Font(MathFont) is not { mathConstants: not null } font) return m.source.Length * px * 0.5f;
            return MathLayout.Layout(MathParser.Parse(m.source), m.display, font.atlasMetaData, font.mathConstants).width * px;
        }

        private static FontAsset? Font(string name) =>
            AssetRegistries.GetRegistryByValueType<string, FontAsset>(typeof(FontAsset)).GetValueOrDefault(name);

        private static FontStyle Face(AtlasMetaData atlas, TexFont font) => atlas.Effective(font.bold
            ? font.italic ? FontStyle.BoldItalic : FontStyle.Bold
            : font.italic ? FontStyle.Italic : FontStyle.Regular);

        // \includegraphics' size in px; 0 leaves that side to the picture.
        private static (float width, float height) PictureSize(TexImage image)
        {
            float w = image.width > 0 ? image.width / Unity * PxPerPt : 0f, h = image.height > 0 ? image.height / Unity * PxPerPt : 0f;
            bool scaled = image.scale > 0f && w == 0f && h == 0f, fitted = image.keepAspect && w > 0f && h > 0f;
            if (!scaled && !fitted) return (w, h);

            (int nw, int nh) = Native(image.path);
            if (nw <= 0 || nh <= 0) return (w, h);
            if (scaled) return (nw * image.scale, nh * image.scale);
            float fit = MathF.Min(w / nw, h / nh);
            return (nw * fit, nh * fit);
        }

        private static (int width, int height) Native(string path)
        {
            try
            {
                ImageInfo info = Image.Identify(path);
                return (info.Width, info.Height);
            }
            catch (Exception)
            {
                return (0, 0);
            }
        }
        #endregion

        #region ---- runs ----
        // Characters of one style become a run; glue a space, or no-break spaces of about its width; math its own run.
        private static void Runs(List<TexNode> line, XElement block, string blockFont, int blockSize, bool code)
        {
            StringBuilder text = new StringBuilder();
            TexStyle? current = null;
            int flushed = 0;

            void Flush()
            {
                if (current is TexStyle s && text.Length > 0) block.Add(TextRun(text.ToString(), s, blockFont, blockSize));
                flushed += text.Length;
                text.Clear();
            }

            void Put(string chars, TexStyle s)
            {
                if (current != s) Flush();
                current = s;
                text.Append(chars);
            }

            void Spacer(int sp, TexStyle s)
            {
                float px = sp / Unity * PxPerPt;
                if (px < 0.5f) return;
                Flush();
                current = null;
                XElement spacer = TextRun(" ", s, blockFont, blockSize);
                spacer.SetAttributeValue("Space", Format(px));
                block.Add(spacer);
                flushed++;
            }

            void Walk(List<TexNode> nodes, bool unbreakable)
            {
                foreach (TexNode node in nodes)
                    switch (node)
                    {
                        case TexChar c:
                            Put(c.ch.ToString(), c.style);
                            break;
                        case TexGlueNode { interword: true } g:
                            Put(unbreakable ? " " : " ", g.style);
                            break;
                        case TexGlueNode g:
                            Spacer(g.glue.width, g.style);
                            break;
                        case TexKern k:
                            Spacer(k.width, k.style);
                            break;
                        case TexHBox b:
                            Walk(b.list, true);
                            break;
                        case TexRefNode { page: true } r:
                            Flush();
                            current = null;
                            XElement pageRef = TextRun(r.text, r.style, blockFont, blockSize);
                            pageRef.AddAnnotation(new PageRefNote(r.keys[0]));
                            block.Add(pageRef);
                            flushed += r.text.Length;
                            break;
                        case TexRefNode r:
                            Put(r.text, r.style);
                            break;
                        case TexLabelMark l:
                            block.AddAnnotation(new LabelSite(l.key, flushed + text.Length));
                            break;
                        case TexImage i:
                            Flush();
                            current = null;
                            XElement picture = new XElement("Run", new XAttribute("Image", i.path));
                            (float width, float height) = PictureSize(i);
                            if (width > 0f) picture.SetAttributeValue("Width", Format(width));
                            if (height > 0f) picture.SetAttributeValue("Height", Format(height));
                            if (i.angle != 0f) picture.SetAttributeValue("Rotation", Format(-i.angle));
                            block.Add(picture);
                            flushed++;
                            break;
                        case TexMathNode m:
                            Flush();
                            current = null;
                            XElement run = new XElement("Run", new XAttribute("Math", m.source), new XAttribute("FontName", MathFont));
                            if (m.display) run.SetAttributeValue("Display", "true");
                            if (m.note != null) run.SetAttributeValue("Note", m.note.id);
                            int px = Px(m.style.font.size);
                            if (px != blockSize) Size(run, px);
                            if (m.style.color != null) run.SetAttributeValue("ColorHex", m.style.color);
                            block.Add(run);
                            flushed++;
                            break;
                    }
            }

            Walk(line, false);
            Flush();
            if (!code) Trim(block);
        }

        private static XElement TextRun(string text, TexStyle s, string blockFont, int blockSize)
        {
            XElement run = new XElement("Run", new XAttribute("Text", text));
            if (s.font.bold) run.SetAttributeValue("Bold", "true");
            if (s.font.italic) run.SetAttributeValue("Italic", "true");
            if (s.underline) run.SetAttributeValue("Underline", "true");
            if (s.color != null) run.SetAttributeValue("ColorHex", s.color);
            string font = s.font.family == TexFamily.Mono ? MonoFont : RomanFont;
            if (font != blockFont) run.SetAttributeValue("FontName", font);
            int px = Px(s.font.size);
            if (px != blockSize) Size(run, px);
            return run;
        }

        private static void Size(XElement run, int px)
        {
            run.SetAttributeValue("FontSize", px);
            run.SetAttributeValue("FontSizeAuthored", "true");
        }

        // No-break spaces of about a width; nothing under 0.4 of a space.
        private static string Spaces(int width, TexStyle s)
        {
            float space = s.font.size * (s.font.family == TexFamily.Mono ? 0.525f : 1f / 3f);
            if (space <= 0f || width < 0.4f * space) return string.Empty;
            return new string(' ', Math.Max(1, (int)MathF.Round(width / space)));
        }

        // Spaces at a block's ends dropped, as a line break drops them.
        private static void Trim(XElement block)
        {
            XElement? first = block.Elements("Run").FirstOrDefault(r => r.Attribute("Text") != null);
            XElement? last = block.Elements("Run").LastOrDefault(r => r.Attribute("Text") != null);
            if (first != null && block.Elements("Run").First() == first) Cut(first, s => s.TrimStart(' '));
            if (last != null && block.Elements("Run").Last() == last) Cut(last, s => s.TrimEnd(' '));
        }

        private static void Cut(XElement run, Func<string, string> trim)
        {
            string text = trim(run.Attribute("Text")!.Value);
            if (text.Length > 0) run.SetAttributeValue("Text", text);
            else run.Remove();
        }
        #endregion

        private static int Px(int sp) => (int)MathF.Round(sp / Unity * PxPerPt);

        private static string Format(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    // \pageref runs to fill, and the block and offset each label stands at
    public sealed class TexPageRefs
    {
        public readonly List<(XElement run, string key)> refs = new List<(XElement, string)>();
        public readonly Dictionary<string, (int block, int offset)> labels = new Dictionary<string, (int, int)>();
    }

    // em and ex from the baked Latin Modern atlases; cmr10's ratio where a face is missing.
    public sealed class TexAtlasMetrics : ITexFontMetrics
    {
        public int Quad(TexFont font) => font.size;

        public int XHeight(TexFont font)
        {
            Dictionary<string, FontAsset> fonts = AssetRegistries.GetRegistryByValueType<string, FontAsset>(typeof(FontAsset));
            if (!fonts.TryGetValue(font.family == TexFamily.Mono ? TexLowering.MonoFont : TexLowering.RomanFont, out FontAsset? asset))
                return (int)(font.size * 0.430554);

            AtlasMetaData atlas = asset.atlasMetaData;
            FontStyle face = atlas.Effective(font.bold
                ? font.italic ? FontStyle.BoldItalic : FontStyle.Bold
                : font.italic ? FontStyle.Italic : FontStyle.Regular);
            return (int)(atlas.GetGlyph('x').Metrics(face).glyphHeight * font.size);
        }
    }
}
