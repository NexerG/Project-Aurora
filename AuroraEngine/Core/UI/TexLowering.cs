using ArctisAurora.Core.Filing;
using ArctisAurora.Core.Registry.Assets;
using ArctisAurora.Core.Tex;
using ArctisAurora.EngineWork.Registry;
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

        // article's \textwidth at 10pt, 11pt, 12pt and its \textheight, pt
        private static readonly float[] textWidths = { 345f, 360f, 390f };
        private const float TextHeight = 550f;

        public static XElement Compile(string source, out List<TexError> errors)
        {
            TexTypesetter typesetter = new TexTypesetter(source, new TexAtlasMetrics());
            typesetter.Run();
            errors = typesetter.errors;
            return Lower(typesetter);
        }

        public static XElement Lower(TexTypesetter typesetter)
        {
            XElement root = new XElement("Document");
            int normal = Px(typesetter.normalSize);
            Dictionary<int, int> headings = HeadingSizes(typesetter.vlist);
            root.Add(Layout(typesetter, normal, headings));

            foreach (TexNode node in typesetter.vlist)
                LowerVertical(node, root, normal, headings);

            if (typesetter.endnotes.Count > 0)
            {
                root.Add(new XElement("Block", new XAttribute("StylingType", "Heading1"), new XElement("Run", new XAttribute("Text", "Notes"), new XAttribute("Bold", "true"))));
                foreach (TexNode node in typesetter.endnotes)
                    LowerVertical(node, root, normal, headings);
            }
            return root;
        }

        #region ---- layout ----
        private static XElement Layout(TexTypesetter typesetter, int normal, Dictionary<int, int> headings)
        {
            bool a4 = typesetter.paper == "a4";
            float width = a4 ? 210f : 215.9f, height = a4 ? 297f : 279.4f;
            float side = (width - textWidths[typesetter.sizeOption] * MmPerPt) / 2f;
            float top = (height - TextHeight * MmPerPt) / 2f;

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
        private static void LowerVertical(TexNode node, XElement root, int normal, Dictionary<int, int> headings)
        {
            switch (node)
            {
                case TexParagraph p:
                    List<List<TexNode>> lines = Lines(p.list);
                    for (int i = 0; i < lines.Count; i++)
                        root.Add(Block(p, lines[i], i == 0, normal, headings));
                    break;
                case TexRuleNode:
                    root.Add(new XElement("Block", new XAttribute("StylingType", "Rule")));
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
