using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.Editing;
using ArctisAurora.Core.UISystem.Controls.Text.Document;
using ArctisAurora.EngineWork;
using Silk.NET.Maths;

namespace ArctisAurora.Core.UI
{
    // Where a caret sits: which block, and how many characters into it. A block is one control, so
    // there is no run level to address and no boundary slot to normalize away.
    public readonly struct NextCaretSlot
    {
        public readonly NextBlockControl block;
        public readonly int offset;

        public NextCaretSlot(NextBlockControl block, int offset)
        {
            this.block = block;
            this.offset = offset;
        }

        public bool Equals(NextCaretSlot other) => block == other.block && offset == other.offset;
    }

    // The note's content area: blocks stacked top to bottom, with the caret over them.
    public class NextDocumentControl : ContainerControl, IGlyphPressTarget
    {
        public float blockSpacing;

        // caret and highlight paint, assigned by the editor before either is built
        public string caretColorHex = "#FFFFFF";
        public string selectionColorHex = "#264F78";

        // the model these blocks came from; the file is written from its block list
        internal NextRichTextDocument document = null!;

        // the open note's history, assigned by the editor; null until a session exists
        internal UndoStack undo;

        private NextCaretControl caret;
        private readonly List<NextPanelControl> highlights = new List<NextPanelControl>();

        public NextBlockControl caretBlock { get; private set; }
        public int caretOffset { get; private set; }

        // Where a selection started; equal to the caret means nothing is selected.
        private NextCaretSlot anchor;

        // A line above the caret ends exactly where the caret's line begins; the slack is for the
        // float arithmetic that got them both there.
        private const float bandTolerance = 0.5f;

        public NextCaretSlot Focus => new NextCaretSlot(caretBlock, caretOffset);

        // The editor above takes the focus, so the caret survives anything still inside it.
        public override Control? ActiveContextTarget() => (parent as Control)?.ActiveContextTarget();

        #region ---- caret ----
        // extend keeps the anchor where it is, which is what shift does; otherwise the selection
        // collapses onto the new position.
        public void SetCaret(NextBlockControl block, int offset, bool extend = false)
        {
            if (block == null) return;

            caretBlock = block;
            caretOffset = Math.Clamp(offset, 0, block.Length);
            if (!extend) anchor = Focus;

            if (caret == null)
            {
                caret = new NextCaretControl { colorHex = caretColorHex, hitTestable = false };
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
            if (run is not NextBlockControl block) return;

            SetCaret(block, index, Extending);
            Editor?.BeginSelectionDrag();
        }

        // A press that landed on the document itself — the gap between blocks, or past the last one.
        public override bool OnPointerPress(PointerEvent e)
        {
            if (CaretOffText(e.point, out NextBlockControl block, out int offset))
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

        private NextDocumentEditorControl Editor => parent as NextDocumentEditorControl;

        internal static bool Extending => InputHandler.instance.IsModifierDown(InputModifier.Extend);
        #endregion

        #region ---- caret navigation ----
        // The caret's rect in the space the blocks are arranged in.
        internal bool CaretPoint(out float x, out float y, out float height)
        {
            x = y = height = 0f;
            if (caretBlock == null) return false;

            CaretGeometry geometry = caretBlock.CaretAt(caretOffset);
            Vector2D<float> origin = caretBlock.TextOrigin;

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
        internal bool CaretAtPoint(float x, float y, out NextBlockControl block, out int offset,
            float bandMin = float.NegativeInfinity, float bandMax = float.PositiveInfinity)
        {
            block = null;
            offset = 0;

            NextBlockControl best = null;
            float bestLineTop = 0f;
            float bestY = float.MaxValue;
            float bestX = float.MaxValue;

            foreach (Entity child in children)
            {
                if (child is not NextBlockControl candidate) continue;

                IReadOnlyList<TextLine> lines = candidate.Lines;
                if (lines == null) continue;

                Vector2D<float> origin = candidate.TextOrigin;

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
            offset = best.IndexAt(new Vector2D<float>(x, best.TextOrigin.Y + bestLineTop + 1f));
            return true;
        }

        // A point that landed on no line at all. Below every block there is no line worth resolving
        // against, so the document's end stands in; anywhere else the nearest slot does.
        internal bool CaretOffText(Vector2D<float> point, out NextBlockControl block, out int offset)
        {
            NextBlockControl last = LastBlock();
            if (last != null && point.Y > last.arrangedRect.Bottom)
            {
                block = last;
                offset = last.Length;
                return true;
            }

            return CaretAtPoint(point.X, point.Y, out block, out offset);
        }

        // Blocks in document order, so a caret can step out of one and into its neighbour.
        internal NextBlockControl AdjacentBlock(NextBlockControl from, int direction)
        {
            List<NextBlockControl> blocks = Blocks();

            int index = blocks.IndexOf(from);
            if (index < 0) return null;

            int adjacent = index + direction;
            return adjacent >= 0 && adjacent < blocks.Count ? blocks[adjacent] : null;
        }

        private NextBlockControl LastBlock()
        {
            NextBlockControl last = null;
            foreach (Entity child in children)
                if (child is NextBlockControl block) last = block;

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

            anchor = new NextCaretSlot(caretBlock, start);
            SetCaret(caretBlock, end, true);
        }

        // The first block's start to the last block's end, whatever the caret was doing.
        public void SelectAll()
        {
            List<NextBlockControl> blocks = Blocks();
            if (blocks.Count == 0) return;

            anchor = new NextCaretSlot(blocks[0], 0);
            SetCaret(blocks[^1], blocks[^1].Length, true);
        }

        // The two ends in reading order, since a drag can run backwards.
        internal bool OrderedSelection(out NextDocumentAddress from, out NextDocumentAddress to)
        {
            from = to = default;
            if (!HasSelection) return false;

            NextDocumentAddress a = AddressOf(anchor.block, anchor.offset);
            NextDocumentAddress b = AddressOf(caretBlock, caretOffset);
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

            if (OrderedSelection(out NextDocumentAddress from, out NextDocumentAddress to))
            {
                List<NextBlockControl> blocks = Blocks();

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
        private int HighlightBlock(NextBlockControl block, int from, int to, int used)
        {
            IReadOnlyList<TextLine> lines = block.Lines;
            if (lines == null || to <= from) return used;

            Vector2D<float> origin = block.TextOrigin;

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
        private NextPanelControl Highlight(int index)
        {
            while (highlights.Count <= index)
            {
                NextPanelControl box = new NextPanelControl
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
            if (!OrderedSelection(out NextDocumentAddress from, out NextDocumentAddress to)) return false;

            NextDocumentFragment fragment = CaptureFragment(from, to);
            undo?.Push(new NextDeleteRangeEdit(this, from, to, fragment));
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
        internal void SplitBlockAt(NextDocumentAddress at)
        {
            if (!Resolve(at, out NextBlockControl block, out int offset)) return;

            NextBlockControl tail = block.SplitAt(offset);
            InsertBlockAfter(block, tail);
            tail.ApplyLayout(document.layout);
            block.InvalidateLayout();

            undo?.Push(new NextSplitEdit(this, at));

            SetCaret(tail, 0);
        }

        // Records the insert before the write, because the address is read off the caret the write is
        // about to advance.
        internal void TypeChar(char c)
        {
            if (caretBlock == null) return;

            NextDocumentAddress at = AddressOf(caretBlock, caretOffset);

            undo?.Push(new NextTextEdit(this, at, c.ToString(), true));
            caretBlock.InsertText(caretOffset, c.ToString());

            SetCaret(caretBlock, caretOffset + 1);
        }

        // Blocks sit between the highlight boxes at the head of the child list and the caret at its
        // end, so a new one goes in beside its neighbour rather than at either end.
        private void InsertBlockAfter(NextBlockControl after, NextBlockControl block)
        {
            children.Insert(children.IndexOf(after) + 1, block);
            block.parent = this;
            MarkTreeOrderDirty();
            InvalidateLayout();

            document.blocks.Insert(document.blocks.IndexOf(after) + 1, block);
        }

        // Destroy detaches from the child list itself; the model list is the other place it lives.
        private void RemoveBlock(NextBlockControl block)
        {
            document.blocks.Remove(block);
            block.Destroy();
        }

        internal List<NextBlockControl> Blocks()
        {
            List<NextBlockControl> blocks = new List<NextBlockControl>();
            foreach (Entity child in children)
                if (child is NextBlockControl block) blocks.Add(block);

            return blocks;
        }
        #endregion

        #region ---- addressing ----
        internal NextDocumentAddress AddressOf(NextBlockControl block, int offset) =>
            new NextDocumentAddress(Blocks().IndexOf(block), offset);

        internal bool Resolve(NextDocumentAddress at, out NextBlockControl block, out int offset)
        {
            block = null;
            offset = 0;

            List<NextBlockControl> blocks = Blocks();
            if (at.block < 0 || at.block >= blocks.Count) return false;

            block = blocks[at.block];
            offset = Math.Clamp(at.offset, 0, block.Length);
            return true;
        }

        private void CaretTo(NextDocumentAddress at)
        {
            if (Resolve(at, out NextBlockControl block, out int offset)) SetCaret(block, offset);
        }
        #endregion

        #region ---- undo primitives ----
        internal void InsertText(NextDocumentAddress at, string insert)
        {
            if (!Resolve(at, out NextBlockControl block, out int offset)) return;

            block.InsertText(offset, insert);
            SetCaret(block, offset + insert.Length);
        }

        internal void RemoveText(NextDocumentAddress at, int count)
        {
            if (!Resolve(at, out NextBlockControl block, out int offset)) return;
            if (offset + count > block.Length) return;

            block.RemoveText(offset, count);
            SetCaret(block, offset);
        }

        // The head block keeps its prefix and the caret, the tail block's suffix joins it, and every
        // block the range crossed whole goes with the tail.
        internal void DeleteBetween(NextDocumentAddress from, NextDocumentAddress to)
        {
            List<NextBlockControl> blocks = Blocks();
            if (from.block < 0 || to.block >= blocks.Count || from.block > to.block) return;

            NextBlockControl head = blocks[from.block];

            if (from.block == to.block)
            {
                head.RemoveText(from.offset, to.offset - from.offset);
                SetCaret(head, from.offset);
                return;
            }

            NextBlockControl tail = blocks[to.block];
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
        private NextDocumentFragment CaptureFragment(NextDocumentAddress from, NextDocumentAddress to)
        {
            NextDocumentFragment fragment = new NextDocumentFragment();
            List<NextBlockControl> blocks = Blocks();

            for (int i = from.block; i <= to.block && i < blocks.Count; i++)
                fragment.blocks.Add(blocks[i].SliceSnapshot(
                    i == from.block ? from.offset : 0,
                    i == to.block ? to.offset : blocks[i].Length));

            return fragment;
        }

        // The inverse of a range delete. The head block takes its cut text back, and when the range
        // crossed blocks the survivors that were merged into it move back out into rebuilt ones.
        internal void InsertFragment(NextDocumentAddress at, NextDocumentFragment fragment)
        {
            if (fragment.blocks.Count == 0) return;
            if (!Resolve(at, out NextBlockControl head, out int offset)) return;

            if (fragment.blocks.Count == 1)
            {
                head.InsertSlice(offset, fragment.blocks[0]);
                SetCaret(head, offset);
                return;
            }

            // everything after the insertion point in the head block was moved there by the merge
            NextBlockSnapshot rest = head.SliceSnapshot(offset, head.Length);
            head.RemoveText(offset, head.Length - offset);
            head.AppendSlice(fragment.blocks[0]);

            NextBlockControl previous = head;
            for (int i = 1; i < fragment.blocks.Count - 1; i++)
            {
                NextBlockControl block = NextBlockControl.From(fragment.blocks[i]);
                InsertBlockAfter(previous, block);
                block.ApplyLayout(document.layout);
                previous = block;
            }

            NextBlockControl last = NextBlockControl.From(fragment.blocks[^1]);
            last.AppendSlice(rest);
            InsertBlockAfter(previous, last);
            last.ApplyLayout(document.layout);

            head.ApplyLayout(document.layout);
            SetCaret(head, offset);
        }

        // The inverse of a split.
        internal void JoinBlockWithNext(NextDocumentAddress at)
        {
            List<NextBlockControl> blocks = Blocks();
            if (at.block < 0 || at.block + 1 >= blocks.Count) return;

            NextBlockControl head = blocks[at.block];
            NextBlockControl tail = blocks[at.block + 1];

            head.AppendBlock(tail);
            RemoveBlock(tail);

            head.ApplyLayout(document.layout);
            CaretTo(at);
        }
        #endregion

        #region ---- layout ----
        public override Vector2D<float> Measure(Vector2D<float> availableSize)
        {
            float height = 0f;
            int blocks = 0;

            foreach (Entity child in children)
            {
                if (child is not NextBlockControl block) continue;

                height += block.Measure(new Vector2D<float>(availableSize.X, float.MaxValue)).Y;
                blocks++;
            }

            if (blocks > 1) height += blockSpacing * (blocks - 1);

            caret?.Measure(availableSize);
            foreach (NextPanelControl box in highlights)
                box.Measure(availableSize);

            arrange.desired = new Vector2D<float>(availableSize.X, height);
            SetFlag(ArrangeFlags.MeasureDirty, false);
            return arrange.desired;
        }

        public override void Arrange(LayoutRect finalRect)
        {
            WriteArranged(finalRect);

            LayoutRect inner = finalRect.Shrink(arrange.padding);
            float y = inner.y;

            foreach (Entity child in children)
            {
                if (child is not NextBlockControl block) continue;

                block.Arrange(new LayoutRect(inner.x, y, inner.width, block.DesiredSize.Y));
                y += block.DesiredSize.Y + blockSpacing;
            }

            // after the blocks, so every line's geometry is this frame's
            ArrangeSelection();
            ArrangeCaret();

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
            Vector2D<float> origin = caretBlock.TextOrigin;

            caret.Arrange(new LayoutRect(origin.X + geometry.x, origin.Y + geometry.top,
                NextCaretControl.Width, geometry.height));
        }
        #endregion
    }
}
