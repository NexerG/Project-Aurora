using ArctisAurora.Core.Filing;
using ArctisAurora.Core.Filing.Serialization;
using System.Numerics;
using System.Text;

namespace ArctisAurora.Core.Pdf
{
    // One source face as Type 3 fonts of up to 256 glyphs each, every glyph drawn from the face's outline.
    public sealed class PdfType3Font
    {
        private readonly PdfDocument pdf;
        private readonly string path;
        private readonly int face;
        private readonly Dictionary<char, (int font, byte code)> codes = new Dictionary<char, (int, byte)>();
        private readonly List<List<char>> fonts = new List<List<char>>();
        private readonly List<int> ids = new List<int>();

        public PdfType3Font(PdfDocument pdf, string path, int face)
        {
            this.pdf = pdf;
            this.path = path;
            this.face = face;
        }

        // The font object and code that draw a character, assigned on first use.
        public (int id, byte code) Code(char character)
        {
            if (!codes.TryGetValue(character, out (int font, byte code) found))
            {
                if (fonts.Count == 0 || fonts[^1].Count == 256)
                {
                    fonts.Add(new List<char>());
                    ids.Add(pdf.Reserve());
                }
                found = (fonts.Count - 1, (byte)fonts[^1].Count);
                fonts[^1].Add(character);
                codes.Add(character, found);
            }
            return (ids[found.font], found.code);
        }

        // Reads the used glyphs' outlines and writes every font object.
        public void Write()
        {
            AuroraFont font = ReadDirectory(out float unitsPerEm);
            for (int f = 0; f < fonts.Count; f++)
            {
                font.textData = new AuroraFont.TextData { characterCount = fonts[f].Count, characters = fonts[f].ToArray() };
                Glyph[] glyphs = AuroraFont.ReadFaceGlyphs(font, path);

                StringBuilder procs = new StringBuilder();
                StringBuilder widths = new StringBuilder();
                StringBuilder names = new StringBuilder();
                Vector4 box = new Vector4(float.MaxValue, float.MaxValue, float.MinValue, float.MinValue);
                for (int i = 0; i < glyphs.Length; i++)
                {
                    float advance = glyphs[i].regular.advanceWidth * 1000f;
                    (string proc, Vector4 ink) = Procedure(glyphs[i], advance, unitsPerEm);
                    if (ink.X <= ink.Z) box = new Vector4(MathF.Min(box.X, ink.X), MathF.Min(box.Y, ink.Y), MathF.Max(box.Z, ink.Z), MathF.Max(box.W, ink.W));
                    procs.Append($"/g{i} {pdf.AddStream("", Encoding.Latin1.GetBytes(proc))} 0 R ");
                    widths.Append(PdfDocument.Num(advance)).Append(' ');
                    names.Append($"/g{i}");
                }
                if (box.X > box.Z) box = Vector4.Zero;

                int toUnicode = pdf.AddStream("", Encoding.Latin1.GetBytes(ToUnicode(fonts[f])));
                pdf.Set(ids[f], "<< /Type /Font /Subtype /Type3 " +
                                $"/FontBBox [{PdfDocument.Num(box.X)} {PdfDocument.Num(box.Y)} {PdfDocument.Num(box.Z)} {PdfDocument.Num(box.W)}] " +
                                "/FontMatrix [0.001 0 0 0.001 0 0] " +
                                $"/CharProcs << {procs}>> " +
                                $"/Encoding << /Type /Encoding /Differences [0 {names}] >> " +
                                $"/FirstChar 0 /LastChar {glyphs.Length - 1} /Widths [{widths}] " +
                                $"/ToUnicode {toUnicode} 0 R /Resources << >> >>");
            }
        }

        // A glyph's drawing in 1000 units per em, and its ink box; an empty glyph draws nothing.
        private static (string proc, Vector4 ink) Procedure(Glyph glyph, float advance, float unitsPerEm)
        {
            GlyphMetrics m = glyph.regular;
            float scale = MathF.Max(m.xMax - m.xMin, m.yMax - m.yMin);
            if (scale <= 0f) scale = 1f;
            float k = 1000f / unitsPerEm;
            Vector2 At(Vector2 p) => new Vector2((p.X * scale + m.xMin) * k, (p.Y * scale + m.yMin) * k);
            string P(Vector2 p) => $"{PdfDocument.Num(p.X)} {PdfDocument.Num(p.Y)}";

            StringBuilder path = new StringBuilder();
            Vector4 ink = new Vector4(float.MaxValue, float.MaxValue, float.MinValue, float.MinValue);
            void Grow(Vector2 p) => ink = new Vector4(MathF.Min(ink.X, p.X), MathF.Min(ink.Y, p.Y), MathF.Max(ink.Z, p.X), MathF.Max(ink.W, p.Y));

            foreach (List<Edge> contour in glyph.edgeContours)
            {
                if (contour.Count == 0) continue;
                Vector2 start = At(contour[0].p0);
                Grow(start);
                path.Append(P(start)).Append(" m\n");
                foreach (Edge e in contour)
                {
                    Vector2 c0 = At(e.c0), c1 = At(e.c1), p1 = At(e.p1);
                    Grow(c0);
                    Grow(c1);
                    Grow(p1);
                    path.Append($"{P(c0)} {P(c1)} {P(p1)} c\n");
                }
                path.Append("h\n");
            }

            if (path.Length == 0)
                return ($"{PdfDocument.Num(advance)} 0 0 0 0 0 d1\n", ink);
            return ($"{PdfDocument.Num(advance)} 0 {PdfDocument.Num(ink.X)} {PdfDocument.Num(ink.Y)} {PdfDocument.Num(ink.Z)} {PdfDocument.Num(ink.W)} d1\n{path}f\n", ink);
        }

        // Codes back to text; a ligature maps to its letters so search finds the word.
        private static string ToUnicode(List<char> characters)
        {
            StringBuilder map = new StringBuilder();
            map.Append("/CIDInit /ProcSet findresource begin\n12 dict begin\nbegincmap\n");
            map.Append("/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def\n");
            map.Append("/CMapName /Adobe-Identity-UCS def\n/CMapType 2 def\n");
            map.Append("1 begincodespacerange\n<00> <FF>\nendcodespacerange\n");
            for (int start = 0; start < characters.Count; start += 100)
            {
                int count = Math.Min(100, characters.Count - start);
                map.Append($"{count} beginbfchar\n");
                for (int i = start; i < start + count; i++)
                {
                    char c = characters[i];
                    string text = c >= 'ﬀ' && c <= 'ﬆ' ? c.ToString().Normalize(NormalizationForm.FormKC) : c.ToString();
                    map.Append($"<{i:X2}> <");
                    foreach (char u in text) map.Append(((int)u).ToString("X4"));
                    map.Append(">\n");
                }
                map.Append("endbfchar\n");
            }
            map.Append("endcmap\nCMapName currentdict /CMap defineresource pop\nend\nend\n");
            return map.ToString();
        }

        // The face's table directory and units per em, read from the source file itself.
        private AuroraFont ReadDirectory(out float unitsPerEm)
        {
            using BinaryReader reader = new BinaryReader(File.OpenRead(path));
            reader.BaseStream.Position = AssetImporter.FaceOffset(reader, face);

            AuroraFont font = new AuroraFont();
            font.fontMeta = new AuroraFont.FontMeta
            {
                version = AssetImporter.ReadUInt32BE(reader),
                tableCount = AssetImporter.ReadUInt16BE(reader)
            };
            reader.BaseStream.Position += 6;
            font.tableEntries = new AuroraFont.TableEntry[font.fontMeta.tableCount];
            for (int i = 0; i < font.fontMeta.tableCount; i++)
                font.tableEntries[i] = new AuroraFont.TableEntry
                {
                    name = new string(reader.ReadChars(4)),
                    checksum = AssetImporter.ReadUInt32BE(reader),
                    offset = AssetImporter.ReadUInt32BE(reader),
                    length = AssetImporter.ReadUInt32BE(reader)
                };

            reader.BaseStream.Position = font.tableEntries.First(t => t.name == "head").offset + 18;
            unitsPerEm = AssetImporter.ReadUInt16BE(reader);
            return font;
        }
    }
}
