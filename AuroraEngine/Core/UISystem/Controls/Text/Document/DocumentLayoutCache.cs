namespace ArctisAurora.Core.UISystem.Controls.Text.Document
{
    // A resolved place in the document — which block, which run inside it, and how many characters
    // into that run. This is what a click resolves to and what a caret is: P3's cursor is a pair of
    // these (anchor + head) and a selection is the span between them.
    public readonly struct DocumentPosition
    {
        public readonly int blockIndex;
        public readonly int runIndex;
        public readonly int charOffset;

        public DocumentPosition(int blockIndex, int runIndex, int charOffset)
        {
            this.blockIndex = blockIndex;
            this.runIndex = runIndex;
            this.charOffset = charOffset;
        }
    }

    // Where a position sits in document space: enough to draw a caret as a bar of `height` at `x`
    // starting at `top`, and to scroll it into view. `baseline` comes along because anything else
    // placed on that line needs it and it is already known by the time the rest is.
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

    // The document's geometry, and the only thing allowed to answer a question about it. Holds one
    // BlockLayout per block plus the prefix sum of their tops, so any point in document space maps to
    // a position and back without a single control existing — which is what lets the view materialize
    // only the visible blocks (L2) while clicks, arrows, find-next and pagination still work on the
    // parts that are not on screen.
    //
    // Deliberately additive for now: nothing drives the view off this yet, so the cached geometry can
    // be raised alongside what already renders and compared against it before anything trusts it.
    public sealed class DocumentLayoutCache
    {
        private readonly IGlyphMetrics metrics;

        private RichTextDocument document;
        private float contentWidth;

        private readonly List<BlockLayout> blocks = new List<BlockLayout>();

        // Prefix sum of block heights and the gaps between them: blockTops[i] is block i's Y in
        // document space, and the final entry is the total scroll extent — which is why this carries
        // one entry more than there are blocks.
        private readonly List<float> blockTops = new List<float>();

        public DocumentLayoutCache(IGlyphMetrics metrics)
        {
            this.metrics = metrics;
        }

        public int BlockCount => blocks.Count;
        public BlockLayout BlockAt(int index) => blocks[index];
        public float TopOf(int index) => blockTops[index];

        // Total document height, and so the scroll extent the view sizes itself to — taken from here
        // rather than from measuring children, because at L2 most children do not exist.
        public float Extent => blockTops.Count == 0 ? 0f : blockTops[blockTops.Count - 1];

        public void Rebuild(RichTextDocument document, float contentWidth)
        {
            this.document = document;
            this.contentWidth = contentWidth;

            blocks.Clear();
            blockTops.Clear();

            float spacing = document.layout.blockSpacing;
            float top = 0f;

            for (int i = 0; i < document.blocks.Count; i++)
            {
                blockTops.Add(top);
                BlockLayout layout = Measure(i);
                blocks.Add(layout);

                top += layout.height;
                if (i < document.blocks.Count - 1) top += spacing;
            }

            blockTops.Add(top);
        }

        // Re-measures one block and slides everything below it — the typing path, where a keystroke
        // changes one paragraph and leaves every other block's lines exactly as they were. The shift
        // is a loop rather than a Fenwick tree because a document is a few hundred blocks and the
        // measurement that just ran costs more than the additions do.
        public void InvalidateBlock(int index)
        {
            float previousHeight = blocks[index].height;
            blocks[index] = Measure(index);

            float delta = blocks[index].height - previousHeight;
            if (delta == 0f) return;

            for (int i = index + 1; i < blockTops.Count; i++)
                blockTops[i] += delta;
        }

        // A width change rewraps every block, so there is nothing to salvage. This is the viewport
        // resize path; blocks added or removed by an edit also come through here until P3 shows that
        // a full re-measure per structural edit is actually too slow to do.
        //
        // Returns whether it actually rewrapped, because everything downstream of the cache is
        // keyed by line and segment index — a rewrap silently changes what those indices mean, so a
        // view holding controls against the old ones has to be told to drop them.
        public bool SetContentWidth(float width)
        {
            if (width == contentWidth) return false;
            Rebuild(document, width);
            return true;
        }

        // Point in document space to the position the caret should take. One code path for mouse
        // clicks and for keyboard navigation that has to land on a column, and it works on blocks
        // that have no controls.
        public DocumentPosition HitTest(float x, float y)
        {
            if (blocks.Count == 0) return new DocumentPosition(0, 0, 0);

            int blockIndex = BlockAtY(y);
            BlockLayout block = blocks[blockIndex];
            TextLine line = block.lines[LineAtY(block, y - blockTops[blockIndex])];

            return PositionInLine(blockIndex, line, x);
        }

        // The inverse — where to actually draw the caret, and what ScrollIntoView aims at.
        public CaretGeometry CharToPoint(DocumentPosition position)
        {
            BlockLayout block = blocks[position.blockIndex];
            ContentBlock content = ContentAt(position.blockIndex);
            int fontSize = document.layout.FontSizeFor(content);
            float blockTop = blockTops[position.blockIndex];

            for (int i = 0; i < block.lines.Count; i++)
            {
                TextLine line = block.lines[i];
                bool isLastLine = i == block.lines.Count - 1;

                if (TryMeasureTo(line, isLastLine, content, fontSize, position, out float x))
                    return Geometry(line, blockTop, x);
            }

            // No line claimed it, which means the position points past the block's content. Clamp to
            // the end rather than returning nothing — a caret always has somewhere to be.
            TextLine last = block.lines[block.lines.Count - 1];
            return Geometry(last, blockTop, last.width);
        }

        private BlockLayout Measure(int index)
            => TextMeasurer.MeasureBlock(ContentAt(index), contentWidth, metrics, document.layout);

        // Every concrete block is a ContentBlock today. A CodeBlock or TableBlock (B1/B2) needs its
        // own measurement path, and throwing here is better than quietly caching a zero-height block:
        // that would not fail, it would draw every block after it at the wrong Y.
        private ContentBlock ContentAt(int index) => (ContentBlock)document.blocks[index];

        // Rightmost entry not past y, so a point above the document clamps to the first block and one
        // below it to the last — a drag that runs off the top or bottom edge still resolves. Public
        // because the view starts its visible-range walk here as well as hit-testing.
        public int BlockAtY(float y)
        {
            int low = 0;
            int high = blocks.Count - 1;

            while (low < high)
            {
                int mid = (low + high + 1) / 2;
                if (blockTops[mid] <= y) low = mid;
                else high = mid - 1;
            }
            return low;
        }

        private static int LineAtY(BlockLayout block, float y)
        {
            int low = 0;
            int high = block.lines.Count - 1;

            while (low < high)
            {
                int mid = (low + high + 1) / 2;
                if (block.lines[mid].top <= y) low = mid;
                else high = mid - 1;
            }
            return low;
        }

        // Walks the line's segments in order, re-deriving per-character advances as it goes. They are
        // re-derived rather than cached because storing one float per character is megabytes at 100
        // pages against a cache budget of ~100 KB, and a click only ever needs the single line it
        // landed on.
        private DocumentPosition PositionInLine(int blockIndex, TextLine line, float x)
        {
            ContentBlock content = ContentAt(blockIndex);
            int fontSize = document.layout.FontSizeFor(content);
            float pen = 0f;

            foreach (LineSegment segment in line.segments)
            {
                // A segment the click is entirely past is skipped by its cached width, without
                // measuring characters the caret cannot land on anyway.
                if (x > pen + segment.width)
                {
                    pen += segment.width;
                    continue;
                }

                TextRun run = content.inlines[segment.runIndex];
                TextMeasurer.Run measured = new TextMeasurer.Run(run.text, run.fontName, fontSize);

                for (int i = 0; i < segment.charCount; i++)
                {
                    float advance = TextMeasurer.MeasureAdvance(run.text[segment.charStart + i], measured, metrics);

                    // Past a character's midpoint belongs to the slot after it, the way every text
                    // editor behaves — snapping to the nearest edge instead of the containing cell is
                    // what makes clicking the back half of a word put the caret at its end.
                    if (x < pen + advance * 0.5f)
                        return new DocumentPosition(blockIndex, segment.runIndex, segment.charStart + i);

                    pen += advance;
                }
            }

            LineSegment lastSegment = line.segments[line.segments.Count - 1];
            return new DocumentPosition(blockIndex, lastSegment.runIndex, lastSegment.charStart + lastSegment.charCount);
        }

        // X of a position within one line, or false when the line does not hold it.
        private bool TryMeasureTo(TextLine line, bool isLastLine, ContentBlock content, int fontSize,
            DocumentPosition position, out float x)
        {
            x = 0f;

            for (int s = 0; s < line.segments.Count; s++)
            {
                LineSegment segment = line.segments[s];
                int end = segment.charStart + segment.charCount;

                // The slot one past a segment's last character is a real caret position — between two
                // runs on one line it is the boundary between them. The exception is the end of a
                // wrapped line, where that same offset is also the first slot of the line below;
                // letting the line below claim it puts the caret in front of the wrapped word instead
                // of trailing off the right edge of the line above.
                bool endBelongsHere = isLastLine || s < line.segments.Count - 1;

                bool holds = segment.runIndex == position.runIndex
                    && position.charOffset >= segment.charStart
                    && (position.charOffset < end || (endBelongsHere && position.charOffset == end));

                if (!holds)
                {
                    x += segment.width;
                    continue;
                }

                TextRun run = content.inlines[segment.runIndex];
                TextMeasurer.Run measured = new TextMeasurer.Run(run.text, run.fontName, fontSize);

                for (int i = segment.charStart; i < position.charOffset; i++)
                    x += TextMeasurer.MeasureAdvance(run.text[i], measured, metrics);

                return true;
            }

            return false;
        }

        private static CaretGeometry Geometry(TextLine line, float blockTop, float x)
            => new CaretGeometry(x, blockTop + line.top, line.height, blockTop + line.baseline);
    }
}
