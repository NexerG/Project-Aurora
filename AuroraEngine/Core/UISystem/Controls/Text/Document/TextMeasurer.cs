using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Registry.Assets;
using ArctisAurora.EngineWork.Registry;

namespace ArctisAurora.Core.UISystem.Controls.Text.Document
{
    // A font's line box, em-normalized like the glyph metrics — multiply by the run's font size. It
    // is a property of the font, not of the characters on a line, so two lines in the same style are
    // the same height whether or not either happens to hold a capital or a descender.
    public readonly struct LineMetrics
    {
        public readonly float ascent;
        public readonly float descent;

        public LineMetrics(float ascent, float descent)
        {
            this.ascent = ascent;
            this.descent = descent;
        }
    }

    // Everything the measurer needs from the font system — no atlas texture, no FontAsset, no GPU —
    // so a test can hand it fabricated metrics and assert line breaks with no font file and no
    // device.
    public interface IGlyphMetrics
    {
        Glyph Get(string fontName, char character);
        LineMetrics GetLineMetrics(string fontName);
    }

    // One run's contribution to a line. A line carries a list of these rather than a single run
    // index because a line crosses runs: a paragraph with a bold word mid-sentence puts three
    // segments on one visual line.
    public readonly struct LineSegment
    {
        public readonly int runIndex;
        public readonly int charStart;
        public readonly int charCount;
        public readonly float width;

        public LineSegment(int runIndex, int charStart, int charCount, float width)
        {
            this.runIndex = runIndex;
            this.charStart = charStart;
            this.charCount = charCount;
            this.width = width;
        }
    }

    public sealed class TextLine
    {
        public readonly List<LineSegment> segments = new List<LineSegment>();
        public float width;
        public float ascent;
        public float descent;

        // Y of the line's top within its own block. Document space is this plus the block's top,
        // which the block cache supplies — the measurer never sees document coordinates.
        public float top;

        public float height => ascent + descent;
        public float baseline => top + ascent;
    }

    public sealed class BlockLayout
    {
        public readonly List<TextLine> lines = new List<TextLine>();
        public float width;
        public float height;
    }

    // Turns a block's runs into lines using font metrics alone. Every geometry question about the
    // document is meant to resolve on the output of this, so it holds no state and touches nothing
    // that needs a booted engine.
    public static class TextMeasurer
    {
        // A run reduced to what layout needs. Runs are TextInputControls today and constructing one
        // reaches into the asset registry, so the measurer takes the data rather than the control —
        // that is what keeps it runnable with no registry and no GPU.
        public readonly struct Run
        {
            public readonly string text;
            public readonly string fontName;
            public readonly int fontSize;

            public Run(string text, string fontName, int fontSize)
            {
                this.text = text;
                this.fontName = fontName;
                this.fontSize = fontSize;
            }
        }

        // One character's pen advance, plus the line box of the run it belongs to. The box is per
        // run rather than per character, so a line takes its height from the styles present on it
        // and not from which letters they happened to spell.
        private readonly struct PenChar
        {
            public readonly int runIndex;
            public readonly int charIndex;
            public readonly bool breakAfter;
            public readonly float advance;
            public readonly float ascent;
            public readonly float descent;

            public PenChar(int runIndex, int charIndex, bool breakAfter, float advance, float ascent, float descent)
            {
                this.runIndex = runIndex;
                this.charIndex = charIndex;
                this.breakAfter = breakAfter;
                this.advance = advance;
                this.ascent = ascent;
                this.descent = descent;
            }
        }

        // Bridge from the document model to the measurer's input. Runs are TextInputControls, so this
        // is the one place that reads text off a control — the overload below stays plain data, which
        // is what lets it run against fabricated metrics with no registry and no GPU.
        public static BlockLayout MeasureBlock(ContentBlock block, float contentWidth, IGlyphMetrics metrics, DocumentLayout documentLayout)
        {
            int fontSize = documentLayout.FontSizeFor(block);

            List<Run> runs = new List<Run>(block.inlines.Count);
            foreach (TextRun inline in block.inlines)
                runs.Add(new Run(inline.text, inline.fontName, fontSize));

            return MeasureBlock(runs, contentWidth, metrics, documentLayout);
        }

        public static BlockLayout MeasureBlock(IReadOnlyList<Run> runs, float contentWidth, IGlyphMetrics metrics, DocumentLayout documentLayout)
        {
            List<PenChar> chars = Flatten(runs, metrics, documentLayout);
            BlockLayout layout = new BlockLayout();

            if (chars.Count == 0)
            {
                layout.lines.Add(EmptyLine(runs, metrics, documentLayout));
                layout.height = layout.lines[0].height;
                return layout;
            }

            int lineStart = 0;
            int lastBreak = -1;     // last character this line is allowed to end on
            float penX = 0f;

            for (int i = 0; i < chars.Count; i++)
            {
                PenChar c = chars[i];

                // A break character is never what pushes a line over: trailing spaces are allowed to
                // hang past the column, and letting one wrap would push the break onto the next line
                // where it would show up as a leading indent.
                if (!c.breakAfter && penX + c.advance > contentWidth && i > lineStart)
                {
                    // End the line at the last break opportunity. Without one the word is wider than
                    // the column, so it has to split mid-word or nothing would ever fit.
                    int breakAt = lastBreak >= 0 ? lastBreak : i - 1;

                    AppendLine(layout, chars, lineStart, breakAt);

                    lineStart = breakAt + 1;
                    lastBreak = -1;

                    // Everything between the break and here moves down with the wrapped word.
                    penX = 0f;
                    for (int j = lineStart; j < i; j++) penX += chars[j].advance;
                }

                penX += c.advance;
                if (c.breakAfter) lastBreak = i;
            }

            AppendLine(layout, chars, lineStart, chars.Count - 1);

            foreach (TextLine line in layout.lines)
                if (line.width > layout.width) layout.width = line.width;

            return layout;
        }

        private static List<PenChar> Flatten(IReadOnlyList<Run> runs, IGlyphMetrics metrics, DocumentLayout documentLayout)
        {
            List<PenChar> chars = new List<PenChar>();

            for (int r = 0; r < runs.Count; r++)
            {
                Run run = runs[r];
                if (string.IsNullOrEmpty(run.text)) continue;

                // Resolved once per run, not per character — the line box does not vary within a run.
                (float ascent, float descent) = LineBox(run, metrics, documentLayout);

                for (int i = 0; i < run.text.Length; i++)
                {
                    char c = run.text[i];
                    bool breakAfter = c == ' ' || c == '\t';
                    chars.Add(new PenChar(r, i, breakAfter, MeasureAdvance(c, run, metrics), ascent, descent));
                }
            }
            return chars;
        }

        // The CSS line box, which is what Obsidian gets from line-height and Word from its spacing
        // multiplier: the box is the font size times the document's multiple, and the font's own
        // ink box is centred in it. The leftover space splits evenly above and below — half-leading —
        // so the baseline lands at a fixed offset for a given style rather than tracking the glyphs.
        private static (float ascent, float descent) LineBox(Run run, IGlyphMetrics metrics, DocumentLayout documentLayout)
        {
            LineMetrics box = metrics.GetLineMetrics(run.fontName);

            float lineHeight = run.fontSize * documentLayout.lineHeight;
            float halfLeading = (lineHeight - (box.ascent + box.descent) * run.fontSize) * 0.5f;
            float ascent = halfLeading + box.ascent * run.fontSize;

            return (ascent, lineHeight - ascent);
        }

        // Groups the run of characters into per-run segments and takes the line's box from the
        // tallest style on it, then stacks it under whatever the block holds so far. Taking a max
        // matters only where a line mixes font sizes; within one style every line comes out equal.
        private static void AppendLine(BlockLayout layout, List<PenChar> chars, int from, int to)
        {
            TextLine line = new TextLine { top = layout.height };

            int segmentRun = chars[from].runIndex;
            int segmentStart = chars[from].charIndex;
            int segmentCount = 0;
            float segmentWidth = 0f;

            for (int i = from; i <= to; i++)
            {
                PenChar c = chars[i];
                if (c.runIndex != segmentRun)
                {
                    line.segments.Add(new LineSegment(segmentRun, segmentStart, segmentCount, segmentWidth));
                    segmentRun = c.runIndex;
                    segmentStart = c.charIndex;
                    segmentCount = 0;
                    segmentWidth = 0f;
                }

                segmentCount++;
                segmentWidth += c.advance;
                line.width += c.advance;
                if (c.ascent > line.ascent) line.ascent = c.ascent;
                if (c.descent > line.descent) line.descent = c.descent;
            }

            line.segments.Add(new LineSegment(segmentRun, segmentStart, segmentCount, segmentWidth));

            layout.lines.Add(line);
            layout.height += line.height;
        }

        // An empty paragraph still occupies a line, or it would be zero tall and drop out of the
        // scroll extent with nowhere for a caret to sit.
        private static TextLine EmptyLine(IReadOnlyList<Run> runs, IGlyphMetrics metrics, DocumentLayout documentLayout)
        {
            TextLine line = new TextLine();
            line.segments.Add(new LineSegment(0, 0, 0, 0f));
            if (runs.Count == 0) return line;

            (line.ascent, line.descent) = LineBox(runs[0], metrics, documentLayout);
            return line;
        }

        // An unimported character falls back to a blank of space width, the same way a glyph control
        // does, so unexpected text measures as a gap instead of throwing. Public because
        // DocumentLayoutCache re-walks a line's advances when hit-testing: two copies of the pen
        // formula would let clicks drift out of step with the lines they are being tested against.
        public static float MeasureAdvance(char character, Run run, IGlyphMetrics metrics)
        {
            Glyph glyph = metrics.Get(run.fontName, character) ?? metrics.Get(run.fontName, ' ');
            return glyph == null ? 0f : glyph.advanceWidth * run.fontSize;
        }
    }

    // Production metrics source. Kept out of TextMeasurer so the measurer itself never reaches for
    // the asset registry, which is what a headless test would have to boot.
    public sealed class FontAssetGlyphMetrics : IGlyphMetrics
    {
        private readonly Dictionary<string, FontAsset> fonts =
            AssetRegistries.GetRegistryByValueType<string, FontAsset>(typeof(FontAsset));

        private readonly Dictionary<string, LineMetrics> lineBoxes = new Dictionary<string, LineMetrics>();

        public Glyph Get(string fontName, char character) => Resolve(fontName).atlasMetaData.GetGlyph(character);

        // The tallest ascent and deepest descent any glyph in the font reaches, so every line is the
        // same height and none of them clip. The font's own hhea ascender/descender would be the
        // better source — GenerateGlyphAtlas already reads both and discards them — but they are not
        // in the .agd, and storing them changes that format and needs every atlas re-baked. Swapping
        // this method's body is the whole migration when that happens.
        public LineMetrics GetLineMetrics(string fontName)
        {
            if (lineBoxes.TryGetValue(fontName, out LineMetrics cached)) return cached;

            float ascent = 0f;
            float descent = 0f;

            foreach (Glyph glyph in Resolve(fontName).atlasMetaData.glyphs)
            {
                int range = glyph.yMax - glyph.yMin;
                if (range == 0) continue;

                // Mirrors GlyphControl: the quad is the padded atlas cell rather than the ink box,
                // and the baseline sits inside that cell at the glyph's yMax fraction.
                float cellHeight = glyph.glyphHeight * GlyphControl.CellScale;
                float glyphAscent = (GlyphControl.atlasInkMargin
                    + (1f - 2f * GlyphControl.atlasInkMargin) * glyph.yMax / range) * cellHeight;

                if (glyphAscent > ascent) ascent = glyphAscent;
                if (cellHeight - glyphAscent > descent) descent = cellHeight - glyphAscent;
            }

            LineMetrics box = new LineMetrics(ascent, descent);
            lineBoxes[fontName] = box;
            return box;
        }

        private FontAsset Resolve(string fontName)
            => fonts.TryGetValue(fontName, out FontAsset named) ? named : fonts["default"];
    }
}
