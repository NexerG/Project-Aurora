using ArctisAurora.Core.Diagnostics;
using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.Editing;
using ArctisAurora.Core.Filing;
using ArctisAurora.EngineWork;
using System.Numerics;

namespace ArctisAurora.Core.UI
{
    // Where a caret sits: which block, and how many characters into it. A block is one control, so
    // there is no run level to address and no boundary slot to normalize away.
    public readonly struct CaretSlot
    {
        public readonly BlockControl block;
        public readonly int offset;

        public CaretSlot(BlockControl block, int offset)
        {
            this.block = block;
            this.offset = offset;
        }

        public bool Equals(CaretSlot other) => block == other.block && offset == other.offset;
    }

    // A style change as data, so an edit record can carry one and redo can replay it. A member left
    // null is one the change does not speak to — which is what lets bold over a multicoloured
    // selection keep every colour in it.
    public readonly struct StyleDelta
    {
        public readonly bool? bold;
        public readonly bool? italic;
        public readonly bool? strikethrough;
        public readonly string colorHex;
        public readonly int? fontSize;

        public StyleDelta(bool? bold = null, bool? italic = null, bool? strikethrough = null,
            string colorHex = null, int? fontSize = null)
        {
            this.bold = bold;
            this.italic = italic;
            this.strikethrough = strikethrough;
            this.colorHex = colorHex;
            this.fontSize = fontSize;
        }

        public void Apply(ref StyleSpan span)
        {
            bool isBold = bold ?? span.IsBold;
            bool isItalic = italic ?? span.IsItalic;
            span.style = isBold ? (isItalic ? FontStyle.BoldItalic : FontStyle.Bold)
                                : isItalic ? FontStyle.Italic : FontStyle.Regular;

            if (strikethrough.HasValue) span.strikethrough = strikethrough.Value;
            if (colorHex != null) span.colorHex = colorHex;
            if (fontSize.HasValue)
            {
                span.fontSizeAuthored = true;
                span.fontSize = fontSize.Value;
            }
        }

        // This delta with another laid over it; the newer one wins wherever it speaks.
        public StyleDelta With(StyleDelta over) => new StyleDelta(
            over.bold ?? bold, over.italic ?? italic, over.strikethrough ?? strikethrough,
            over.colorHex ?? colorHex, over.fontSize ?? fontSize);

        // Whether applying this would move anything on the span.
        public bool Changes(StyleSpan span) =>
            (bold.HasValue && bold != span.IsBold)
            || (italic.HasValue && italic != span.IsItalic)
            || (strikethrough.HasValue && strikethrough != span.strikethrough)
            || (colorHex != null && colorHex != span.colorHex)
            || (fontSize.HasValue && fontSize != span.fontSize);
    }

    // The style the next character will take: the span the caret sits in, with an armed change laid
    // over it. Resolved against the block, so an unset span member reads as what it draws as.
    public readonly struct CaretStyle
    {
        public readonly bool bold;
        public readonly bool italic;
        public readonly bool strikethrough;
        public readonly string colorHex;
        public readonly int fontSize;

        public CaretStyle(BlockControl block, StyleSpan span, StyleDelta armed)
        {
            bold = armed.bold ?? span.IsBold;
            italic = armed.italic ?? span.IsItalic;
            strikethrough = armed.strikethrough ?? span.strikethrough;
            colorHex = armed.colorHex ?? span.colorHex ?? block.colorHex;
            fontSize = armed.fontSize ?? (span.fontSize > 0 ? span.fontSize : block.fontSize);
        }
    }

    // The note's content area: blocks stacked top to bottom, with the caret over them.
    public class DocumentControl : ContainerControl, IGlyphPressTarget
    {
        public float blockSpacing;

        // caret and highlight paint, assigned by the editor before either is built
        public string caretColorHex = "#FFFFFF";
        public string selectionColorHex = "#264F78";

        // the model these blocks came from; the file is written from its block list
        internal RichTextDocument document = null!;

        // the open note's history, assigned by the editor; null until a session exists
        internal UndoStack undo;

        private CaretControl caret;
        private readonly List<PanelControl> highlights = new List<PanelControl>();

        public BlockControl caretBlock { get; private set; }
        public int caretOffset { get; private set; }

        // Where a selection started; equal to the caret means nothing is selected.
        private CaretSlot anchor;

        // a style change with nothing to apply it to, spent on the next character typed
        private StyleDelta pending;

        // A line above the caret ends exactly where the caret's line begins; the slack is for the
        // float arithmetic that got them both there.
        private const float bandTolerance = 0.5f;

        public CaretSlot Focus => new CaretSlot(caretBlock, caretOffset);

        // The editor above takes the focus, so the caret survives anything still inside it.
        public override Control? ActiveContextTarget() => (parent as Control)?.ActiveContextTarget();

        #region ---- caret ----
        // extend keeps the anchor where it is, which is what shift does; otherwise the selection
        // collapses onto the new position.
        public void SetCaret(BlockControl block, int offset, bool extend = false)
        {
            if (block == null) return;

            caretBlock = block;
            caretOffset = Math.Clamp(offset, 0, block.Length);
            if (!extend) anchor = Focus;

            if (caret == null)
            {
                caret = new CaretControl { colorHex = caretColorHex, hitTestable = false };
                AddChild(caret);
            }
            caret.Focus();

            InvalidateArrange();
        }

        public void CollapseSelection() => anchor = Focus;

        internal void Blur() => caret?.Blur();

        internal void FocusCaret() => caret?.Focus();

        // The run tells the document which character was pressed; the document owns the caret.
        public void GlyphPressed(TextRunControl run, int index)
        {
            if (run is not BlockControl block) return;

            SetCaret(block, index, Extending);
            Editor?.BeginSelectionDrag();
        }

        // A press that landed on the document itself — the gap between blocks, or past the last one.
        public override bool OnPointerPress(PointerEvent e)
        {
            if (CaretOffText(e.point, out BlockControl block, out int offset))
            {
                SetCaret(block, offset, Extending);
                Editor?.BeginSelectionDrag();
            }

            return true;
        }

        // Two clicks take the word, three the visual line.
        public override bool OnPointerTap(PointerEvent e)
        {
            if (e.tapCount == 2) SelectWord();
            else if (e.tapCount >= 3) Editor?.SelectLine();
            else return false;

            return true;
        }

        private DocumentEditorControl Editor => parent as DocumentEditorControl;

        internal static bool Extending => InputHandler.instance.IsModifierDown(InputModifier.Extend);
        #endregion

        #region ---- caret navigation ----
        // The caret's rect in the space the blocks are arranged in.
        internal bool CaretPoint(out float x, out float y, out float height)
        {
            x = y = height = 0f;
            if (caretBlock == null) return false;

            CaretGeometry geometry = caretBlock.CaretAt(caretOffset);
            Vector2 origin = caretBlock.TextOrigin;

            x = origin.X + geometry.x;
            y = origin.Y + geometry.top;
            height = geometry.height;
            return true;
        }

        // Nearest caret slot to a point. The candidate is a visual line rather than a block — a line
        // closer in y always wins and x only breaks ties within a band, which is what makes one
        // primitive answer up/down, line start/end and page moves.
        // bandMin/bandMax restrict which lines may answer: up and down need the line the caret is
        // already on excluded, or the gap between blocks makes it the nearest one to a point just
        // outside it and the caret never leaves.
        internal bool CaretAtPoint(float x, float y, out BlockControl block, out int offset,
            float bandMin = float.NegativeInfinity, float bandMax = float.PositiveInfinity)
        {
            block = null;
            offset = 0;

            BlockControl best = null;
            float bestLineTop = 0f;
            float bestY = float.MaxValue;
            float bestX = float.MaxValue;

            foreach (Entity child in children)
            {
                if (child is not BlockControl candidate) continue;

                IReadOnlyList<TextLine> lines = candidate.Lines;
                if (lines == null) continue;

                Vector2 origin = candidate.TextOrigin;

                foreach (TextLine line in lines)
                {
                    float top = origin.Y + line.top;

                    if (top < bandMin - bandTolerance || top + line.height > bandMax + bandTolerance)
                        continue;

                    float dy = Distance(y, top, top + line.height);
                    float dx = Distance(x, origin.X, origin.X + line.width);
                    if (dy > bestY || (dy == bestY && dx >= bestX)) continue;

                    bestY = dy;
                    bestX = dx;
                    best = candidate;
                    bestLineTop = line.top;
                }
            }

            if (best == null) return false;

            block = best;
            offset = best.IndexAt(new Vector2(x, best.TextOrigin.Y + bestLineTop + 1f));
            return true;
        }

        // A point that landed on no line at all. Below every block there is no line worth resolving
        // against, so the document's end stands in; anywhere else the nearest slot does.
        internal bool CaretOffText(Vector2 point, out BlockControl block, out int offset)
        {
            BlockControl last = LastBlock();
            if (last != null && point.Y > last.arrangedRect.Bottom)
            {
                block = last;
                offset = last.Length;
                return true;
            }

            return CaretAtPoint(point.X, point.Y, out block, out offset);
        }

        // Blocks in document order, so a caret can step out of one and into its neighbour.
        internal BlockControl AdjacentBlock(BlockControl from, int direction)
        {
            List<BlockControl> blocks = Blocks();

            int index = blocks.IndexOf(from);
            if (index < 0) return null;

            int adjacent = index + direction;
            return adjacent >= 0 && adjacent < blocks.Count ? blocks[adjacent] : null;
        }

        private BlockControl LastBlock()
        {
            BlockControl last = null;
            foreach (Entity child in children)
                if (child is BlockControl block) last = block;

            return last;
        }

        private static float Distance(float value, float low, float high) =>
            value < low ? low - value : (value > high ? value - high : 0f);
        #endregion

        #region ---- selection ----
        private enum CharClass { Space, Word, Symbol }

        private static CharClass ClassOf(char c) =>
            char.IsWhiteSpace(c) ? CharClass.Space
            : char.IsLetterOrDigit(c) || c == '_' ? CharClass.Word
            : CharClass.Symbol;

        public bool HasSelection => caretBlock != null && !anchor.Equals(Focus);

        // The run of one character class around the caret, inside the block.
        public void SelectWord()
        {
            if (caretBlock == null) return;

            string s = caretBlock.text ?? string.Empty;
            CharClass? right = caretOffset < s.Length ? ClassOf(s[caretOffset]) : null;
            CharClass? left = caretOffset > 0 ? ClassOf(s[caretOffset - 1]) : null;

            // either edge of a word takes the word, not the space beside it
            CharClass? picked = right == CharClass.Word || left == CharClass.Word
                ? CharClass.Word : right ?? left;
            if (picked == null) return;

            int start = caretOffset;
            while (start > 0 && ClassOf(s[start - 1]) == picked) start--;

            int end = caretOffset;
            while (end < s.Length && ClassOf(s[end]) == picked) end++;

            anchor = new CaretSlot(caretBlock, start);
            SetCaret(caretBlock, end, true);
        }

        // The first block's start to the last block's end, whatever the caret was doing.
        public void SelectAll()
        {
            List<BlockControl> blocks = Blocks();
            if (blocks.Count == 0) return;

            anchor = new CaretSlot(blocks[0], 0);
            SetCaret(blocks[^1], blocks[^1].Length, true);
        }

        // The two ends in reading order, since a drag can run backwards.
        internal bool OrderedSelection(out DocumentAddress from, out DocumentAddress to)
        {
            from = to = default;
            if (!HasSelection) return false;

            DocumentAddress a = AddressOf(anchor.block, anchor.offset);
            DocumentAddress b = AddressOf(caretBlock, caretOffset);
            if (a.block < 0 || b.block < 0) return false;

            bool forward = a.block < b.block || (a.block == b.block && a.offset <= b.offset);
            from = forward ? a : b;
            to = forward ? b : a;
            return true;
        }

        // Boxes for the selected range, one per visual line it covers. Everything unused is arranged
        // to nothing rather than destroyed — a drag would otherwise create and free controls every
        // tick, each one a pool allocation and a full paint-order permute.
        private void ArrangeSelection()
        {
            int used = 0;

            if (OrderedSelection(out DocumentAddress from, out DocumentAddress to))
            {
                List<BlockControl> blocks = Blocks();

                for (int i = from.block; i <= to.block && i < blocks.Count; i++)
                    used = HighlightBlock(blocks[i],
                        i == from.block ? from.offset : 0,
                        i == to.block ? to.offset : blocks[i].Length,
                        used);
            }

            for (int i = used; i < highlights.Count; i++)
                highlights[i].Arrange(new LayoutRect(arrangedRect.x, arrangedRect.y, 0f, 0f));
        }

        // The x span comes from CaretAt, the same function that places the caret — so the highlight
        // cannot drift from the caret drawn inside it.
        private int HighlightBlock(BlockControl block, int from, int to, int used)
        {
            IReadOnlyList<TextLine> lines = block.Lines;
            if (lines == null || to <= from) return used;

            Vector2 origin = block.TextOrigin;

            foreach (TextLine line in lines)
            {
                if (line.segments.Count == 0) continue;

                LineSegment last = line.segments[line.segments.Count - 1];
                int lineStart = line.segments[0].charStart;
                int lineEnd = last.charStart + last.charCount;

                int start = Math.Max(from, lineStart);
                int end = Math.Min(to, lineEnd);
                if (end <= start) continue;

                // A wrapped line's end slot belongs to the line below, so CaretAt would answer for
                // the wrong line — the line's own width is the right edge in that case.
                float left = start == lineStart ? 0f : block.CaretAt(start).x;
                float right = end == lineEnd ? line.width : block.CaretAt(end).x;

                Highlight(used++).Arrange(new LayoutRect(
                    origin.X + left, origin.Y + line.top, right - left, line.height));
            }

            return used;
        }

        // Highlights live at the head of the child list so they draw behind the blocks — paint order
        // is the tree's DFS order, which is why the caret, added last, draws over the text.
        private PanelControl Highlight(int index)
        {
            while (highlights.Count <= index)
            {
                PanelControl box = new PanelControl
                {
                    colorHex = selectionColorHex,
                    hitTestable = false
                };
                box.parent = this;
                children.Insert(highlights.Count, box);
                highlights.Add(box);
                MarkTreeOrderDirty();
            }
            return highlights[index];
        }
        #endregion

        #region ---- editing ----
        // Removes the selected range; false when nothing was selected.
        public bool DeleteSelection()
        {
            if (!OrderedSelection(out DocumentAddress from, out DocumentAddress to)) return false;

            DocumentFragment fragment = CaptureFragment(from, to);
            undo?.Push(new DeleteRangeEdit(this, from, to, fragment));
            DeleteBetween(from, to);
            return true;
        }

        // Splits the caret's block in two, the second carrying everything from the caret on.
        public void SplitBlock()
        {
            DeleteSelection();

            if (caretBlock == null) return;
            SplitBlockAt(AddressOf(caretBlock, caretOffset));
        }

        // The split itself, addressed rather than read off the caret, so redo can replay it.
        internal void SplitBlockAt(DocumentAddress at)
        {
            if (!Resolve(at, out BlockControl block, out int offset)) return;

            BlockControl tail = block.SplitAt(offset);
            InsertBlockAfter(block, tail);
            tail.ApplyLayout(document.layout);
            block.InvalidateLayout();

            undo?.Push(new SplitEdit(this, at));

            SetCaret(tail, 0);
        }

        // Records the insert before the write, because the address is read off the caret the write is
        // about to advance. An armed style is spent here: the character is written with the style
        // beside it and then restyled, so the partition and both records are a selection restyle's.
        internal void TypeChar(char c)
        {
            if (caretBlock == null) return;

            BlockControl block = caretBlock;
            DocumentAddress at = AddressOf(block, caretOffset);
            StyleDelta armed = pending;
            pending = default;

            undo?.Push(new TextEdit(this, at, c.ToString(), true));
            block.InsertText(at.offset, c.ToString());

            SetCaret(block, at.offset + 1);

            if (!armed.Changes(block.StyleAt(at.offset + 1))) return;

            ApplyStyleTo(at, new DocumentAddress(at.block, at.offset + 1), armed);
            CollapseSelection();
        }

        // Blocks sit between the highlight boxes at the head of the child list and the caret at its
        // end, so a new one goes in beside its neighbour rather than at either end.
        private void InsertBlockAfter(BlockControl after, BlockControl block)
        {
            children.Insert(children.IndexOf(after) + 1, block);
            block.parent = this;
            MarkTreeOrderDirty();
            InvalidateLayout();

            document.blocks.Insert(document.blocks.IndexOf(after) + 1, block);
        }

        // Destroy detaches from the child list itself; the model list is the other place it lives.
        private void RemoveBlock(BlockControl block)
        {
            document.blocks.Remove(block);
            block.Destroy();
        }

        internal List<BlockControl> Blocks()
        {
            List<BlockControl> blocks = new List<BlockControl>();
            foreach (Entity child in children)
                if (child is BlockControl block) blocks.Add(block);

            return blocks;
        }
        #endregion

        #region ---- styling ----
        // What a toggle reads its current state from, and what the format bar reflects. The
        // selection's start rather than the caret's own end, so a backwards drag reads the same
        // style a forwards one does.
        public CaretStyle? StyleSource
        {
            get
            {
                BlockControl block = caretBlock;
                int offset = caretOffset;

                if (OrderedSelection(out DocumentAddress from, out _)
                    && Resolve(from, out BlockControl start, out int startOffset))
                {
                    block = start;
                    offset = startOffset;
                }

                return block == null ? null : new CaretStyle(block, block.StyleAt(offset), pending);
            }
        }

        public TextStyleType CaretBlockStyling => caretBlock?.stylingType ?? TextStyleType.Text;

        // Restyles the selected range; with nothing selected the style is armed for the next
        // character instead. False either way when nothing was written.
        public bool ApplyStyle(StyleDelta delta)
        {
            if (OrderedSelection(out DocumentAddress from, out DocumentAddress to))
                return ApplyStyleTo(from, to, delta);

            ArmStyle(delta);
            return false;
        }

        // Holds a style for the next character typed. Merged into what is already armed, so bold
        // then italic types both; dropped once it agrees with the span the caret is in, which is
        // what makes a second toggle disarm rather than pin the span's own style onto it.
        public void ArmStyle(StyleDelta delta)
        {
            if (caretBlock == null) return;

            pending = pending.With(delta);
            if (!pending.Changes(caretBlock.StyleAt(caretOffset))) pending = default;
        }

        // Restyles an addressed range; false when an end names a block the document does not have.
        public bool ApplyStyleTo(DocumentAddress from, DocumentAddress to, StyleDelta delta)
        {
            int count = Blocks().Count;
            if (from.block < 0 || to.block < 0 || from.block >= count || to.block >= count) return false;

            List<BlockSnapshot> before = SnapshotBlocks(from.block, to.block);

            ApplyStyleBetween(from, to, delta);
            undo?.Push(new StyleRangeEdit(this, from.block, before, from, to, delta));
            return true;
        }

        // Addressed rather than read off the selection, so redo can replay it against the spans undo
        // restored. Offsets are block-relative, so nothing here has to survive a re-partition.
        internal void ApplyStyleBetween(DocumentAddress from, DocumentAddress to, StyleDelta delta)
        {
            List<BlockControl> blocks = Blocks();
            if (from.block < 0 || to.block >= blocks.Count || to.block < from.block) return;

            for (int b = from.block; b <= to.block; b++)
                blocks[b].StyleRange(
                    b == from.block ? Math.Clamp(from.offset, 0, blocks[b].Length) : 0,
                    b == to.block ? Math.Clamp(to.offset, 0, blocks[b].Length) : blocks[b].Length,
                    delta);

            anchor = new CaretSlot(blocks[from.block],
                Math.Clamp(from.offset, 0, blocks[from.block].Length));
            SetCaret(blocks[to.block], Math.Clamp(to.offset, 0, blocks[to.block].Length), true);
        }

        // The styling type of every block the range touches; with nothing selected, the caret's own.
        public bool SetBlockStyling(TextStyleType type)
        {
            if (caretBlock == null) return false;

            int first, last;
            if (OrderedSelection(out DocumentAddress from, out DocumentAddress to))
            {
                first = from.block;
                last = to.block;
            }
            else first = last = Blocks().IndexOf(caretBlock);

            if (first < 0 || last < 0) return false;

            List<BlockSnapshot> before = SnapshotBlocks(first, last);
            SetBlockStylingBetween(first, last, type);
            undo?.Push(new StyleRangeEdit(this, first, before, type));
            return true;
        }

        internal void SetBlockStylingBetween(int first, int last, TextStyleType type)
        {
            List<BlockControl> blocks = Blocks();

            for (int b = first; b <= last && b < blocks.Count; b++)
            {
                blocks[b].stylingType = type;
                blocks[b].ApplyLayout(document.layout);
            }
        }

        // A style change leaves the text alone, so a span of blocks as data is the whole inverse.
        internal List<BlockSnapshot> SnapshotBlocks(int first, int last)
        {
            List<BlockControl> blocks = Blocks();
            List<BlockSnapshot> snapshots = new List<BlockSnapshot>();

            for (int b = first; b <= last && b < blocks.Count; b++)
                snapshots.Add(blocks[b].Snapshot());

            return snapshots;
        }

        // Undo for both styling primitives. Neither adds or removes a block, so the blocks
        // themselves survive and only their spans are rewritten.
        internal void RestoreBlocks(int firstBlock, List<BlockSnapshot> before)
        {
            List<BlockControl> blocks = Blocks();

            for (int i = 0; i < before.Count; i++)
            {
                int index = firstBlock + i;
                if (index < 0 || index >= blocks.Count) continue;

                blocks[index].Restore(before[i]);
                blocks[index].ApplyLayout(document.layout);
            }
        }
        #endregion

        #region ---- addressing ----
        internal DocumentAddress AddressOf(BlockControl block, int offset) =>
            new DocumentAddress(Blocks().IndexOf(block), offset);

        internal bool Resolve(DocumentAddress at, out BlockControl block, out int offset)
        {
            block = null;
            offset = 0;

            List<BlockControl> blocks = Blocks();
            if (at.block < 0 || at.block >= blocks.Count) return false;

            block = blocks[at.block];
            offset = Math.Clamp(at.offset, 0, block.Length);
            return true;
        }

        private void CaretTo(DocumentAddress at)
        {
            if (Resolve(at, out BlockControl block, out int offset)) SetCaret(block, offset);
        }
        #endregion

        #region ---- undo primitives ----
        internal void InsertText(DocumentAddress at, string insert)
        {
            if (!Resolve(at, out BlockControl block, out int offset)) return;

            block.InsertText(offset, insert);
            SetCaret(block, offset + insert.Length);
        }

        internal void RemoveText(DocumentAddress at, int count)
        {
            if (!Resolve(at, out BlockControl block, out int offset)) return;
            if (offset + count > block.Length) return;

            block.RemoveText(offset, count);
            SetCaret(block, offset);
        }

        // The head block keeps its prefix and the caret, the tail block's suffix joins it, and every
        // block the range crossed whole goes with the tail.
        internal void DeleteBetween(DocumentAddress from, DocumentAddress to)
        {
            List<BlockControl> blocks = Blocks();
            if (from.block < 0 || to.block >= blocks.Count || from.block > to.block) return;

            BlockControl head = blocks[from.block];

            if (from.block == to.block)
            {
                head.RemoveText(from.offset, to.offset - from.offset);
                SetCaret(head, from.offset);
                return;
            }

            BlockControl tail = blocks[to.block];
            head.RemoveText(from.offset, head.Length - from.offset);
            tail.RemoveText(0, to.offset);
            head.AppendBlock(tail);

            for (int i = to.block; i > from.block; i--)
                RemoveBlock(blocks[i]);

            head.ApplyLayout(document.layout);
            SetCaret(head, from.offset);
        }

        // Everything the range covers, as data: the head block's cut suffix, then whole blocks, then
        // the tail block's cut prefix.
        private DocumentFragment CaptureFragment(DocumentAddress from, DocumentAddress to)
        {
            DocumentFragment fragment = new DocumentFragment();
            List<BlockControl> blocks = Blocks();

            for (int i = from.block; i <= to.block && i < blocks.Count; i++)
                fragment.blocks.Add(blocks[i].SliceSnapshot(
                    i == from.block ? from.offset : 0,
                    i == to.block ? to.offset : blocks[i].Length));

            return fragment;
        }

        // The inverse of a range delete. The head block takes its cut text back, and when the range
        // crossed blocks the survivors that were merged into it move back out into rebuilt ones.
        internal void InsertFragment(DocumentAddress at, DocumentFragment fragment)
        {
            if (fragment.blocks.Count == 0) return;
            if (!Resolve(at, out BlockControl head, out int offset)) return;

            if (fragment.blocks.Count == 1)
            {
                head.InsertSlice(offset, fragment.blocks[0]);
                SetCaret(head, offset);
                return;
            }

            // everything after the insertion point in the head block was moved there by the merge
            BlockSnapshot rest = head.SliceSnapshot(offset, head.Length);
            head.RemoveText(offset, head.Length - offset);
            head.AppendSlice(fragment.blocks[0]);

            BlockControl previous = head;
            for (int i = 1; i < fragment.blocks.Count - 1; i++)
            {
                BlockControl block = BlockControl.From(fragment.blocks[i]);
                InsertBlockAfter(previous, block);
                block.ApplyLayout(document.layout);
                previous = block;
            }

            BlockControl last = BlockControl.From(fragment.blocks[^1]);
            last.AppendSlice(rest);
            InsertBlockAfter(previous, last);
            last.ApplyLayout(document.layout);

            head.ApplyLayout(document.layout);
            SetCaret(head, offset);
        }

        // The inverse of a split.
        internal void JoinBlockWithNext(DocumentAddress at)
        {
            List<BlockControl> blocks = Blocks();
            if (at.block < 0 || at.block + 1 >= blocks.Count) return;

            BlockControl head = blocks[at.block];
            BlockControl tail = blocks[at.block + 1];

            head.AppendBlock(tail);
            RemoveBlock(tail);

            head.ApplyLayout(document.layout);
            CaretTo(at);
        }
        #endregion

        #region ---- layout ----
        public override Vector2 Measure(Vector2 availableSize)
        {
            float height = 0f;
            int blocks = 0;

            Profiling.Zone.Start("Document.MeasureBlocks");
            foreach (Entity child in children)
            {
                if (child is not BlockControl block) continue;

                height += block.Measure(new Vector2(availableSize.X, float.MaxValue)).Y;
                blocks++;
            }
            Profiling.Zone.End("Document.MeasureBlocks");

            if (blocks > 1) height += blockSpacing * (blocks - 1);

            caret?.Measure(availableSize);
            foreach (PanelControl box in highlights)
                box.Measure(availableSize);

            arrange.desired = new Vector2(availableSize.X, height);
            SetFlag(ArrangeFlags.MeasureDirty, false);
            return arrange.desired;
        }

        public override void Arrange(LayoutRect finalRect)
        {
            WriteArranged(finalRect);

            LayoutRect inner = finalRect.Shrink(arrange.padding);
            float y = inner.y;

            Profiling.Zone.Start("Document.ArrangeBlocks");
            foreach (Entity child in children)
            {
                if (child is not BlockControl block) continue;

                block.Arrange(new LayoutRect(inner.x, y, inner.width, block.DesiredSize.Y));
                y += block.DesiredSize.Y + blockSpacing;
            }
            Profiling.Zone.End("Document.ArrangeBlocks");

            // after the blocks, so every line's geometry is this frame's
            Profiling.Zone.Start("Document.ArrangeOverlays");
            ArrangeSelection();
            ArrangeCaret();
            Profiling.Zone.End("Document.ArrangeOverlays");

            SetFlag(ArrangeFlags.ArrangeDirty, false);
        }

        // After the blocks, so the caret resolves against this frame's line geometry.
        private void ArrangeCaret()
        {
            if (caret == null) return;

            if (caretBlock == null)
            {
                caret.Arrange(LayoutRect.Empty);
                return;
            }

            CaretGeometry geometry = caretBlock.CaretAt(caretOffset);
            Vector2 origin = caretBlock.TextOrigin;

            caret.Arrange(new LayoutRect(origin.X + geometry.x, origin.Y + geometry.top,
                CaretControl.Width, geometry.height));
        }
        #endregion
    }
}
