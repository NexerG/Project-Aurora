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
        private const float MinColumn = 24f;

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

            foreach (TexNode node in typesetter.vlist)
                LowerVertical(node, root, normal, headings, blockLines);

            if (typesetter.endnotes.Count > 0)
            {
                root.Add(new XElement("Block", new XAttribute("StylingType", "Heading1"), new XElement("Run", new XAttribute("Text", "Notes"), new XAttribute("Bold", "true"))));
                blockLines?.Add(0);
                foreach (TexNode node in typesetter.endnotes)
                    LowerVertical(node, root, normal, headings, blockLines);
            }
            return root;
        }

        #region ---- layout ----
        private static XElement Layout(TexTypesetter typesetter, int normal, Dictionary<int, int> headings)
        {
            bool a4 = typesetter.paper == "a4";
            float side = (typesetter.paperWidth - TexTypesetter.textWidths[typesetter.sizeOption] * MmPerPt) / 2f;
            float top = (typesetter.paperHeight - TexTypesetter.TextHeight * MmPerPt) / 2f;

            XElement layout = new XElement("DocumentLayout",
                new XAttribute("LineHeight", "1.2"),
                new XAttribute("BlockSpacing", Format(normal * 0.5f)),
                new XAttribute("ListIndent", Format(normal * 1.875f)),
                new XElement("Page",
                    new XAttribute("Mode", "Paged"),
                    new XAttribute("Size", a4 ? "A4" : "Letter"),
                    new XAttribute("MarginTop", Format(typesetter.marginTop ?? top)),
                    new XAttribute("MarginBottom", Format(typesetter.marginBottom ?? top)),
                    new XAttribute("MarginLeft", Format(typesetter.marginLeft ?? side)),
                    new XAttribute("MarginRight", Format(typesetter.marginRight ?? side))),
                Style("Text", normal, RomanFont),
                Style("Quote", normal, RomanFont),
                Style("Code", normal, MonoFont));
            foreach (KeyValuePair<int, int> heading in headings.OrderBy(h => h.Key))
                layout.Add(Style("Heading" + heading.Key, heading.Value, RomanFont));
            return layout;
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
        private static void LowerVertical(TexNode node, XElement root, int normal, Dictionary<int, int> headings, List<int>? blockLines)
        {
            switch (node)
            {
                case TexParagraph p:
                    List<List<TexNode>> lines = Lines(p.list);
                    for (int i = 0; i < lines.Count; i++)
                    {
                        root.Add(Block(p, lines[i], i == 0, normal, headings));
                        blockLines?.Add(p.line);
                    }
                    break;
                case TexRuleNode:
                    root.Add(new XElement("Block", new XAttribute("StylingType", "Rule")));
                    blockLines?.Add(0);
                    break;
                case TexTable t:
                    root.Add(Table(t, normal, headings));
                    blockLines?.Add(t.line);
                    break;
            }
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

            XElement table = new XElement("Table");
            if (!t.ruled) table.SetAttributeValue("Borders", "false");
            foreach (float width in widths)
                table.Add(new XElement("Column", new XAttribute("Width", Format(MathF.Max(MinColumn, MathF.Ceiling(width + 2f * TabColSep))))));
            foreach (List<(TexTableCell cell, List<TexNode> nodes, float width)> row in rows)
            {
                XElement written = new XElement("Row");
                foreach ((TexTableCell cell, List<TexNode> nodes, _) in row)
                {
                    XElement element = new XElement("Cell");
                    if (cell.span > 1) element.SetAttributeValue("ColumnSpan", cell.span);
                    foreach (TexNode node in nodes)
                        LowerVertical(node, element, normal, headings, null);
                    written.Add(element);
                }
                table.Add(written);
            }
            return table;
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

            void Flush()
            {
                if (current is TexStyle s && text.Length > 0) block.Add(TextRun(text.ToString(), s, blockFont, blockSize));
                text.Clear();
            }

            void Put(string chars, TexStyle s)
            {
                if (current != s) Flush();
                current = s;
                text.Append(chars);
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
                            Put(Spaces(g.glue.width, g.style), g.style);
                            break;
                        case TexKern k:
                            Put(Spaces(k.width, k.style), k.style);
                            break;
                        case TexHBox b:
                            Walk(b.list, true);
                            break;
                        case TexRefNode r:
                            Put(r.text, r.style);
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
                            break;
                        case TexMathNode m:
                            Flush();
                            current = null;
                            XElement run = new XElement("Run", new XAttribute("Math", m.source), new XAttribute("FontName", MathFont));
                            if (m.display) run.SetAttributeValue("Display", "true");
                            int px = Px(m.style.font.size);
                            if (px != blockSize) Size(run, px);
                            if (m.style.color != null) run.SetAttributeValue("ColorHex", m.style.color);
                            block.Add(run);
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
