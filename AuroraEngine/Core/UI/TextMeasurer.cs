using ArctisAurora.Core.Filing;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Registry.Assets;
using ArctisAurora.EngineWork.Registry;

namespace ArctisAurora.Core.UI
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
        public readonly List<LineSegment> segments = new List<LineSegment>(1);
        public float width;
        public float ascent;
        public float descent;

        // Y of the line's top within its own block. Document space is this plus the block's top,
        // which the block cache supplies — the measurer never sees document coordinates.
        public float top;

        // where the line's pen starts, past whatever it wraps around
        public float left;

        // the width the line may fill from left; 0 is the whole wrap width
        public float room;

        // justify: what each space before justifyEnd gains
        public float spaceExtra;
        public int justifyEnd;

        public float height => ascent + descent;
        public float baseline => top + ascent;
    }

    // Where a caret sits, in the space of the control that resolved it.
    public readonly struct CaretGeometry
    {
        public readonly float x;
        public readonly float top;
        public readonly float height;
        public readonly float baseline;

        public CaretGeometry(float x, float top, float height, float baseline)
        {
            this.x = x;
            this.top = top;
            this.height = height;
            this.baseline = baseline;
        }
    }

    // Where a block's next line may go: its top for a line of this height, and the span it may fill,
    // all local to the block's text origin.
    public interface ILineSlots
    {
        float Place(float y, float height, out float left, out float right);
    }

    public sealed class BlockLayout
    {
        public readonly List<TextLine> lines = new List<TextLine>();
        public float width;
        public float height;

        // lines of the previous measure, handed out again by NextLine
        private readonly List<TextLine> spare = new List<TextLine>();

        // flattened characters, kept while the runs' keys match
        internal float[] advances = Array.Empty<float>();
        internal byte[] flags = Array.Empty<byte>();
        internal int count;
        internal readonly List<MeasuredRun> runs = new List<MeasuredRun>();

        // A run's key, where its characters start in the flattened arrays, and its line box.
        internal struct MeasuredRun
        {
            public string text;
            public int charStart;
            public int charCount;
            public AtlasMetaData atlas;
            public FontStyle face;
            public int fontSize;
            public bool picture;
            public bool math;
            public bool floating;

            public int start;
            public int length;
            public float ascent;
            public float descent;

            public bool Matches(in TextMeasurer.Run run) =>
                ReferenceEquals(text, run.text) && charStart == run.charStart && charCount == run.charCount
                && ReferenceEquals(atlas, run.atlas) && face == run.face && fontSize == run.fontSize
                && picture == run.picture && math == run.math && floating == run.floating;
        }

        // Empties the layout, keeping its lines for NextLine.
        internal void Reset()
        {
            spare.AddRange(lines);
            lines.Clear();
            width = 0f;
            height = 0f;
        }

        // A cleared spare line, or a new one.
        internal TextLine NextLine()
        {
            if (spare.Count == 0) return new TextLine();

            TextLine line = spare[^1];
            spare.RemoveAt(spare.Count - 1);
            line.segments.Clear();
            line.width = line.ascent = line.descent = line.top = line.left = line.room = line.spaceExtra = 0f;
            line.justifyEnd = 0;
            return line;
        }
    }

    // Turns a block's runs into lines using font metrics alone. Every geometry question about the
    // document is meant to resolve on the output of this, so it touches nothing that needs a booted
    // engine.
    public static class TextMeasurer
    {
        // atlas cell geometry
        internal const float atlasInkMargin = 0.1f;
        internal const float CellScale = 1f / (1f - 2f * atlasInkMargin);

        // A run reduced to what layout needs. Runs are TextInputControls today and constructing one
        // reaches into the asset registry, so the measurer takes the data rather than the control —
        // that is what keeps it runnable with no registry and no GPU.
        public readonly struct Run
        {
            public readonly string text;
            public readonly string fontName;
            public readonly int fontSize;
            public readonly FontStyle style;

            // resolved once per run
            public readonly AtlasMetaData atlas;
            public readonly FontStyle face;

            // The slice of text this run covers, so several differently-styled spans can share one
            // string instead of each holding a substring cut on every measure.
            public readonly int charStart;
            public readonly int charCount;

            // a picture run: one U+FFFC drawn as an image of this size; a floating one takes no room
            public readonly bool picture;
            public readonly float imageWidth;
            public readonly float imageHeight;
            public readonly bool floating;

            // a formula run: one U+FFFC of imageWidth, imageHeight above the baseline and depth below
            public readonly bool math;
            public readonly float depth;

            public Run(string text, string fontName, AtlasMetaData atlas, int fontSize, FontStyle style = FontStyle.Regular)
            {
                this.text = text;
                this.fontName = fontName;
                this.atlas = atlas;
                this.fontSize = fontSize;
                this.style = style;
                face = atlas.Effective(style);
                charStart = 0;
                charCount = text?.Length ?? 0;
            }

            public Run(string text, int charStart, int charCount, string fontName, AtlasMetaData atlas, int fontSize, FontStyle style,
                bool picture = false, float imageWidth = 0f, float imageHeight = 0f, bool floating = false,
                bool math = false, float depth = 0f)
            {
                this.text = text;
                this.fontName = fontName;
                this.atlas = atlas;
                this.fontSize = fontSize;
                this.style = style;
                face = atlas.Effective(style);
                this.charStart = charStart;
                this.charCount = charCount;
                this.picture = picture;
                this.imageWidth = imageWidth;
                this.imageHeight = imageHeight;
                this.floating = floating;
                this.math = math;
                this.depth = depth;
            }
        }

        // flattened character flags
        private const byte BreakAfter = 1;
        private const byte Picture = 2;
        private const byte Tab = 4;

        // spaces between tab stops
        public const int TabStopSpaces = 4;

        // A character's advance at a pen position within its line; only a tab depends on where it starts.
        public static float MeasureAdvance(char character, in Run run, float penInLine) =>
            character == '\t' ? TabAdvance(run, penInLine) : MeasureAdvance(character, run);

        // From the pen to the next tab stop, a full stop when the pen sits on one.
        public static float TabAdvance(in Run run, float penInLine)
        {
            float stop = TabStopSpaces * MeasureAdvance(' ', run);
            if (stop <= 0f) return 0f;
            return stop - (MathF.Max(0f, penInLine) % stop);
        }

        // The advance character i takes at penX, stored back when it is a tab.
        private static float AdvanceAt(BlockLayout layout, int i, float penX, IReadOnlyList<Run> runs)
        {
            if ((layout.flags[i] & Tab) == 0) return layout.advances[i];

            float advance = TabAdvance(runs[RunOf(layout, i)], penX);
            layout.advances[i] = advance;
            return advance;
        }

        // The run holding flattened character i.
        private static int RunOf(BlockLayout layout, int i)
        {
            List<BlockLayout.MeasuredRun> measured = layout.runs;
            for (int r = 0; r < measured.Count; r++)
                if (i < measured[r].start + measured[r].length) return r;
            return measured.Count - 1;
        }

        // firstLineOffset applies to line 0 only.
        public static BlockLayout MeasureBlock(IReadOnlyList<Run> runs, float contentWidth, IGlyphMetrics metrics,
            float lineHeight, float firstLineOffset = 0f, ILineSlots slots = null, BlockLayout reuse = null)
        {
            BlockLayout layout = reuse ?? new BlockLayout();
            layout.Reset();
            Flatten(layout, runs, metrics, lineHeight);
            int count = layout.count;
            byte[] flags = layout.flags;

            if (slots != null) return MeasureAround(layout, count, runs, metrics, lineHeight, firstLineOffset, slots);

            if (count == 0)
            {
                layout.lines.Add(EmptyLine(layout, runs, metrics, lineHeight));
                layout.height = layout.lines[0].height;
                return layout;
            }

            int lineStart = 0;
            int lastBreak = -1;     // last character this line is allowed to end on
            float penX = firstLineOffset;

            for (int i = 0; i < count; i++)
            {
                float advance = AdvanceAt(layout, i, penX, runs);
                byte f = flags[i];

                // A break character is never what pushes a line over: trailing spaces are allowed to
                // hang past the column, and letting one wrap would push the break onto the next line
                // where it would show up as a leading indent.
                if (((f & BreakAfter) == 0 || (f & Picture) != 0) && penX + advance > contentWidth && i > lineStart)
                {
                    // End the line at the last break opportunity. Without one the word is wider than
                    // the column, so it has to split mid-word or nothing would ever fit.
                    int breakAt = (f & Picture) != 0 || lastBreak < 0 ? i - 1 : lastBreak;

                    AppendLine(layout, lineStart, breakAt);

                    lineStart = breakAt + 1;
                    lastBreak = -1;

                    // Everything between the break and here moves down with the wrapped word.
                    penX = 0f;
                    for (int j = lineStart; j < i; j++) penX += AdvanceAt(layout, j, penX, runs);
                    advance = AdvanceAt(layout, i, penX, runs);
                }

                penX += advance;
                if ((f & BreakAfter) != 0) lastBreak = i;
            }

            AppendLine(layout, lineStart, count - 1);

            foreach (TextLine line in layout.lines)
                if (line.width > layout.width) layout.width = line.width;

            return layout;
        }

        // One line at a time, each placed and narrowed by the slots; a line found taller than its
        // guess asks again with its real height.
        private static BlockLayout MeasureAround(BlockLayout layout, int count, IReadOnlyList<Run> runs,
            IGlyphMetrics metrics, float lineHeight, float firstLineOffset, ILineSlots slots)
        {
            if (count == 0)
            {
                TextLine empty = EmptyLine(layout, runs, metrics, lineHeight);
                empty.top = slots.Place(0f, empty.height, out empty.left, out _);
                layout.lines.Add(empty);
                layout.height = empty.top + empty.height;
                return layout;
            }

            int lineStart = 0;
            while (lineStart < count)
            {
                BlockLayout.MeasuredRun first = layout.runs[RunOf(layout, lineStart)];
                float guess = first.ascent + first.descent;
                float top = 0f, left = 0f, right = 0f;
                int end = lineStart;

                for (int attempt = 0; attempt < 3; attempt++)
                {
                    top = slots.Place(layout.height, guess, out left, out right);
                    end = FillLine(layout, lineStart, count, right - left, lineStart == 0 ? firstLineOffset : 0f, runs);

                    float ascent = 0f, descent = 0f;
                    for (int r = RunOf(layout, lineStart); r < layout.runs.Count && layout.runs[r].start <= end; r++)
                    {
                        if (layout.runs[r].length == 0) continue;
                        ascent = MathF.Max(ascent, layout.runs[r].ascent);
                        descent = MathF.Max(descent, layout.runs[r].descent);
                    }
                    if (ascent + descent <= guess + 0.01f) break;
                    guess = ascent + descent;
                }

                AppendLine(layout, lineStart, end, top, left);
                layout.lines[^1].room = MathF.Max(0f, right - left);
                lineStart = end + 1;
            }

            foreach (TextLine line in layout.lines)
                if (line.left + line.width > layout.width) layout.width = line.left + line.width;

            return layout;
        }

        // The last character one line of this width holds, by the same break rules as MeasureBlock.
        private static int FillLine(BlockLayout layout, int start, int count, float width, float penX, IReadOnlyList<Run> runs)
        {
            byte[] flags = layout.flags;
            int lastBreak = -1;
            for (int i = start; i < count; i++)
            {
                float advance = AdvanceAt(layout, i, penX, runs);
                byte f = flags[i];
                if (((f & BreakAfter) == 0 || (f & Picture) != 0) && penX + advance > width && i > start)
                    return (f & Picture) != 0 || lastBreak < 0 ? i - 1 : lastBreak;

                penX += advance;
                if ((f & BreakAfter) != 0) lastBreak = i;
            }
            return count - 1;
        }

        // Writes the runs' characters into the layout's arrays, keeping text characters whose runs match the last measure.
        private static void Flatten(BlockLayout layout, IReadOnlyList<Run> runs, IGlyphMetrics metrics, float lineHeight)
        {
            List<BlockLayout.MeasuredRun> measured = layout.runs;
            bool keep = measured.Count == runs.Count;
            for (int r = 0; keep && r < runs.Count; r++)
                keep = measured[r].Matches(runs[r]);

            if (!keep)
            {
                int needed = 0;
                for (int r = 0; r < runs.Count; r++)
                    if (!string.IsNullOrEmpty(runs[r].text) && runs[r].charCount > 0)
                        needed += runs[r].charCount;

                int capacity = layout.advances.Length;
                while (capacity < needed) capacity = Math.Max(16, capacity * 2);
                if (capacity != layout.advances.Length)
                {
                    layout.advances = new float[capacity];
                    layout.flags = new byte[capacity];
                }
            }

            float[] advances = layout.advances;
            byte[] flags = layout.flags;
            int count = 0;

            for (int r = 0; r < runs.Count; r++)
            {
                Run run = runs[r];
                BlockLayout.MeasuredRun slot = new BlockLayout.MeasuredRun
                {
                    text = run.text, charStart = run.charStart, charCount = run.charCount, atlas = run.atlas, face = run.face,
                    fontSize = run.fontSize, picture = run.picture, math = run.math, floating = run.floating, start = count
                };

                if (!string.IsNullOrEmpty(run.text) && run.charCount > 0)
                {
                    // Resolved once per run, not per character — the line box does not vary within a run.
                    (float ascent, float descent) = LineBox(run, metrics, lineHeight);
                    slot.length = run.charCount;

                    if (run.picture)
                    {
                        if (!run.floating) (slot.ascent, slot.descent) = (run.imageHeight, descent);
                        for (int i = 0; i < run.charCount; i++, count++)
                        {
                            advances[count] = run.floating ? 0f : run.imageWidth;
                            flags[count] = run.floating ? BreakAfter : (byte)(BreakAfter | Picture);
                        }
                    }
                    else if (run.math)
                    {
                        (slot.ascent, slot.descent) = (MathF.Max(run.imageHeight, ascent), MathF.Max(run.depth, descent));
                        for (int i = 0; i < run.charCount; i++, count++)
                        {
                            advances[count] = run.imageWidth;
                            flags[count] = BreakAfter | Picture;
                        }
                    }
                    else
                    {
                        (slot.ascent, slot.descent) = (ascent, descent);
                        if (keep) count += run.charCount;
                        else
                        {
                            int end = run.charStart + run.charCount;
                            for (int i = run.charStart; i < end; i++, count++)
                            {
                                char c = run.text[i];
                                advances[count] = MeasureAdvance(c, run);
                                flags[count] = c == ' ' ? BreakAfter : c == '\t' ? (byte)(BreakAfter | Tab) : (byte)0;
                            }
                        }
                    }
                }

                if (r < measured.Count) measured[r] = slot;
                else measured.Add(slot);
            }
            if (measured.Count > runs.Count) measured.RemoveRange(runs.Count, measured.Count - runs.Count);

            layout.count = count;
        }

        // The CSS line box, which is what Obsidian gets from line-height and Word from its spacing
        // multiplier: the box is the font size times the document's multiple, and the font's own
        // ink box is centred in it. The leftover space splits evenly above and below — half-leading —
        // so the baseline lands at a fixed offset for a given style rather than tracking the glyphs.
        private static (float ascent, float descent) LineBox(Run run, IGlyphMetrics metrics, float lineHeight)
        {
            LineMetrics box = metrics.GetLineMetrics(run.fontName);

            float lineBox = run.fontSize * lineHeight;
            float halfLeading = (lineBox - (box.ascent + box.descent) * run.fontSize) * 0.5f;
            float ascent = halfLeading + box.ascent * run.fontSize;

            return (ascent, lineBox - ascent);
        }

        // Groups the run of characters into per-run segments and takes the line's box from the
        // tallest style on it, then stacks it under whatever the block holds so far. Taking a max
        // matters only where a line mixes font sizes; within one style every line comes out equal.
        private static void AppendLine(BlockLayout layout, int from, int to) =>
            AppendLine(layout, from, to, layout.height, 0f);

        private static void AppendLine(BlockLayout layout, int from, int to, float top, float left)
        {
            TextLine line = layout.NextLine();
            line.top = top;
            line.left = left;

            float[] advances = layout.advances;
            List<BlockLayout.MeasuredRun> measured = layout.runs;
            float width = 0f;

            for (int r = RunOf(layout, from); r < measured.Count && measured[r].start <= to; r++)
            {
                BlockLayout.MeasuredRun run = measured[r];
                if (run.length == 0) continue;

                int a = Math.Max(from, run.start);
                int b = Math.Min(to, run.start + run.length - 1);
                float segmentWidth = 0f;
                for (int i = a; i <= b; i++)
                {
                    segmentWidth += advances[i];
                    width += advances[i];
                }

                line.segments.Add(new LineSegment(r, run.charStart + (a - run.start), b - a + 1, segmentWidth));
                if (run.ascent > line.ascent) line.ascent = run.ascent;
                if (run.descent > line.descent) line.descent = run.descent;
            }
            line.width = width;

            layout.lines.Add(line);
            layout.height = top + line.height;
        }

        // An empty paragraph still occupies a line, or it would be zero tall and drop out of the
        // scroll extent with nowhere for a caret to sit.
        private static TextLine EmptyLine(BlockLayout layout, IReadOnlyList<Run> runs, IGlyphMetrics metrics, float lineHeight)
        {
            TextLine line = layout.NextLine();
            line.segments.Add(new LineSegment(0, 0, 0, 0f));
            if (runs.Count == 0) return line;

            (line.ascent, line.descent) = LineBox(runs[0], metrics, lineHeight);
            return line;
        }

        // An unimported character falls back to a blank of space width, the same way a glyph control
        // does, so unexpected text measures as a gap instead of throwing. Public because
        // DocumentLayoutCache re-walks a line's advances when hit-testing: two copies of the pen
        // formula would let clicks drift out of step with the lines they are being tested against.
        public static float MeasureAdvance(char character, in Run run)
        {
            if (run.picture) return run.floating ? 0f : run.imageWidth;
            if (run.math) return run.imageWidth;
            if (character < AtlasMetaData.AdvanceTableSize) return run.atlas.TableAdvance(character, run.face) * run.fontSize;

            Glyph glyph = run.atlas.GetGlyph(character) ?? run.atlas.GetGlyph(' ');
            if (glyph == null) return 0f;

            return glyph.Metrics(run.face).advanceWidth * run.fontSize;
        }
    }

    // Production metrics source. Kept out of TextMeasurer so the measurer itself never reaches for
    // the asset registry, which is what a headless test would have to boot.
    public sealed class FontAssetGlyphMetrics : IGlyphMetrics
    {
        private readonly Dictionary<string, FontAsset> fonts =
            AssetRegistries.GetRegistryByValueType<string, FontAsset>(typeof(FontAsset));

        private readonly Dictionary<string, LineMetrics> lineBoxes = new Dictionary<string, LineMetrics>();

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
                int range = glyph.regular.yMax - glyph.regular.yMin;
                if (range == 0) continue;

                // The quad is the padded atlas cell rather than the ink box,
                // and the baseline sits inside that cell at the glyph's yMax fraction.
                float cellHeight = glyph.regular.glyphHeight * TextMeasurer.CellScale;
                float glyphAscent = (TextMeasurer.atlasInkMargin
                    + (1f - 2f * TextMeasurer.atlasInkMargin) * glyph.regular.yMax / range) * cellHeight;

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
