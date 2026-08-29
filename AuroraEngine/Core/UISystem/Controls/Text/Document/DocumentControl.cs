using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.Editing;
using ArctisAurora.Core.Registry.Assets;
using ArctisAurora.Core.UISystem.Controls.Containers;
using ArctisAurora.Core.UISystem.Controls.Text.Document.Edits;
using ArctisAurora.EngineWork.Registry;
using Silk.NET.Maths;

namespace ArctisAurora.Core.UISystem.Controls.Text.Document
{
    // Where a caret sits: which run, and how many characters into it.
    public readonly struct CaretSlot
    {
        public readonly TextControl run;
        public readonly int offset;

        public CaretSlot(TextControl run, int offset)
        {
            this.run = run;
            this.offset = offset;
        }

        public bool Equals(CaretSlot other) => run == other.run && offset == other.offset;
    }

    // The document's content area: blocks stacked top to bottom, plus the caret and the selection
    // placed over them.
    public class DocumentControl : AbstractContainerControl
    {
        public float blockSpacing;

        // the model these blocks came from; the file is written from its block list
        internal RichTextDocument document = null!;

        // the open note's history, assigned by the editor; null until a session exists
        internal UndoStack? undo;

        // caret target
        private CaretControl? caret;
        public TextControl? caretRun { get; private set; }

        // Selection runs anchor -> caret. The caret IS the focus end, so only the anchor is stored;
        // anchor == caret means nothing is selected, which is the plain-caret case.
        private CaretSlot anchor;
        private readonly List<SelectionControl> highlights = new List<SelectionControl>();

        private CaretSlot Focus => new CaretSlot(caretRun, caretRun?.cursorPosition ?? 0);

        public DocumentControl()
        {
            maskAsset = AssetRegistries.GetAsset<TextureAsset>("invisible");
            horizontalAlignment = HorizontalAlignment.Stretch;
            BubbleAll();
        }

        // Points the caret at a run; the offset is read off it at Arrange.
        public void SetCaret(TextControl run)
        {
            if (run == null) return;

            if (caretRun != run)
            {
                caretRun?.CancelEdit();
                caretRun = run;
                // Char input goes to whatever the collision handler last made active, so a caret
                // moved by keyboard has to repoint it or typing lands in the run that was clicked.
                UICollisionHandling.activeControl = run;
            }
            run.BeginEdit();

            if (caret == null)
            {
                caret = new CaretControl();
                AddChild(caret);
            }
            caret.Focus();

            InvalidateArrange();
        }

        // extend keeps the anchor where it is, which is what shift does; otherwise the selection
        // collapses onto the new position.
        public void SetCaret(TextControl run, int offset, bool extend = false)
        {
            if (run == null) return;

            CaretSlot slot = Normalize(new CaretSlot(run, Math.Clamp(offset, 0, Length(run))));
            slot.run.cursorPosition = slot.offset;
            if (!extend) anchor = slot;

            SetCaret(slot.run);
        }

        // The end of a run and the start of the next inside one block are the same point on screen,
        // because the block hands the next run the x the previous one ended at. The second is
        // canonical, so an empty selection is just anchor == focus and a step across a run boundary
        // does not cost a dead keypress.
        private CaretSlot Normalize(CaretSlot slot)
        {
            while (slot.run != null && slot.offset >= Length(slot.run))
            {
                TextControl next = AdjacentRun(slot.run, 1);
                if (next == null || BlockOf(next) != BlockOf(slot.run)) break;
                slot = new CaretSlot(next, 0);
            }
            return slot;
        }

        private static string TextOf(TextControl run) => run.text ?? string.Empty;

        private static int Length(TextControl run) => TextOf(run).Length;

        // Anything that moves the caret without going through SetCaret leaves the anchor behind —
        // WriteChar bumps cursorPosition itself, so typing would otherwise select what it typed.
        public void CollapseSelection() => anchor = Focus;

        // Raised by a run handing the active context away; the caret survives anything still inside
        // the editor.
        internal void LoseFocus()
        {
            if (caret == null) return;

            VulkanControl scope = parent as VulkanControl ?? this;
            for (VulkanControl control = UICollisionHandling.activeControl;
                 control != null;
                 control = control.parent as VulkanControl)
                if (ReferenceEquals(control, scope)) return;

            caret.Blur();
        }

        internal void RegainFocus() => caret?.Focus();

        public override Vector2D<float> Measure(Vector2D<float> availableSize)
        {
            float height = 0f;
            int blocks = 0;

            foreach (Entity child in children)
            {
                if (child is not Block block) continue;

                height += block.Measure(new Vector2D<float>(availableSize.X, float.MaxValue)).Y;
                blocks++;
            }

            if (blocks > 1) height += blockSpacing * (blocks - 1);

            caret?.Measure(availableSize);
            foreach (SelectionControl box in highlights)
                box.Measure(availableSize);

            DesiredSize = new Vector2D<float>(availableSize.X, height);
            isMeasureDirty = false;
            return DesiredSize;
        }

        public override void Arrange(LayoutRect finalRect)
        {
            arrangedRect = finalRect;
            WriteArrangedTransform(finalRect);


            ClipRect = parent is VulkanControl parentControl
                ? (clipOutOfBounds ? LayoutRect.Intersect(finalRect, parentControl.ClipRect) : parentControl.ClipRect)
                : finalRect;

            float y = finalRect.y;
            foreach (Entity child in children)
            {
                if (child is not Block block) continue;

                block.Arrange(new LayoutRect(finalRect.x, y, finalRect.width, block.DesiredSize.Y));
                y += block.DesiredSize.Y + blockSpacing;
            }

            // after the blocks, so every run's arrangedRect is this frame's
            ArrangeSelection();

            if (caret != null && caretRun != null)
            {
                CaretGeometry geometry = caretRun.CaretAt(caretRun.cursorPosition);
                LayoutRect inner = caretRun.arrangedRect.Shrink(caretRun.padding);

                caret.Arrange(new LayoutRect(inner.x + geometry.x, inner.y + geometry.top,
                    CaretControl.Width, geometry.height));
            }

            isArrangeDirty = false;
        }

        #region ---- caret navigation ----
        // The caret's rect in the space the blocks are arranged in.
        internal bool CaretPoint(out float x, out float y, out float height)
        {
            x = y = height = 0f;
            if (caretRun == null) return false;

            CaretGeometry geometry = caretRun.CaretAt(caretRun.cursorPosition);
            LayoutRect inner = caretRun.arrangedRect.Shrink(caretRun.padding);

            x = inner.x + geometry.x;
            y = inner.y + geometry.top;
            height = geometry.height;
            return true;
        }

        // Nearest caret slot to a point. The candidate is a line of a run rather than a run, because
        // runs share visual lines — a line closer in y always wins and x only breaks ties within a
        // band, which is what makes one primitive answer up/down, line start/end and page moves.
        // bandMin/bandMax restrict which lines may answer: up and down need the line the caret is
        // already on excluded, or the gap between blocks makes it the nearest one to a point just
        // outside it and the caret never leaves.
        internal bool CaretAtPoint(float x, float y, out TextControl run, out int offset,
            float bandMin = float.NegativeInfinity, float bandMax = float.PositiveInfinity)
        {
            run = null;
            offset = 0;

            TextControl best = null;
            int bestLine = 0;
            float bestY = float.MaxValue;
            float bestX = float.MaxValue;

            foreach (Entity child in children)
            {
                if (child is not Block block) continue;

                foreach (Entity blockChild in block.children)
                {
                    if (blockChild is not TextControl text || text.Layout == null) continue;

                    LayoutRect inner = text.arrangedRect.Shrink(text.padding);

                    for (int l = 0; l < text.Layout.lines.Count; l++)
                    {
                        TextLine line = text.Layout.lines[l];
                        float top = inner.y + line.top;
                        float left = inner.x + (l == 0 ? text.firstLineOffset : 0f);

                        if (top < bandMin - bandTolerance || top + line.height > bandMax + bandTolerance)
                            continue;

                        float dy = Distance(y, top, top + line.height);
                        float dx = Distance(x, left, left + line.width);
                        if (dy > bestY || (dy == bestY && dx >= bestX)) continue;

                        bestY = dy;
                        bestX = dx;
                        best = text;
                        bestLine = l;
                    }
                }
            }

            if (best == null) return false;

            LayoutRect bestInner = best.arrangedRect.Shrink(best.padding);
            run = best;
            offset = best.OffsetAt(x - bestInner.x, best.Layout.lines[bestLine].top + 1f);
            return true;
        }

        // A point that landed on no run at all. Below every block there is no line worth resolving
        // against, so the document's end stands in; anywhere else the nearest slot does.
        internal bool CaretOffText(float x, float y, out TextControl run, out int offset)
        {
            List<Block> blocks = Blocks();
            if (blocks.Count > 0 && y > blocks[^1].arrangedRect.Bottom)
            {
                List<TextControl> runs = OrderedRuns();
                if (runs.Count > 0)
                {
                    run = runs[^1];
                    offset = Length(run);
                    return true;
                }
            }

            return CaretAtPoint(x, y, out run, out offset);
        }

        // Reading order. A run has no idea where it sits in the document, so ordering two caret
        // slots means looking both runs up in here and comparing (index, offset).
        internal List<TextControl> OrderedRuns()
        {
            List<TextControl> runs = new List<TextControl>();

            foreach (Entity child in children)
            {
                if (child is not Block block) continue;

                foreach (Entity blockChild in block.children)
                    if (blockChild is TextControl text) runs.Add(text);
            }
            return runs;
        }

        // Runs in document order, so a caret can step out of one and into its neighbour.
        internal TextControl AdjacentRun(TextControl from, int direction)
        {
            List<TextControl> runs = OrderedRuns();

            int index = runs.IndexOf(from);
            if (index < 0) return null;

            int adjacent = index + direction;
            return adjacent >= 0 && adjacent < runs.Count ? runs[adjacent] : null;
        }

        internal static Block BlockOf(TextControl run) => run?.parent as Block;

        // A line above the caret ends exactly where the caret's line begins; the slack is for the
        // float arithmetic that got them both there.
        private const float bandTolerance = 0.5f;

        private static float Distance(float value, float low, float high) =>
            value < low ? low - value : (value > high ? value - high : 0f);
        #endregion

        #region ---- selection ----
        private enum CharClass { Space, Word, Symbol }

        private static CharClass ClassOf(char c) =>
            char.IsWhiteSpace(c) ? CharClass.Space
            : char.IsLetterOrDigit(c) || c == '_' ? CharClass.Word
            : CharClass.Symbol;

        // The run of one character class around the caret, crossing runs inside the block.
        public void SelectWord()
        {
            if (caretRun == null) return;

            CaretSlot focus = Normalize(Focus);
            CaretSlot ahead = focus, behind = focus;

            CharClass? right = StepForward(ref ahead, out char after) ? ClassOf(after) : null;
            CharClass? left = StepBack(ref behind, out char before) ? ClassOf(before) : null;

            // either edge of a word takes the word, not the space beside it
            CharClass? picked = right == CharClass.Word || left == CharClass.Word
                ? CharClass.Word : right ?? left;
            if (picked == null) return;

            CharClass target = picked.Value;

            CaretSlot start = focus;
            while (true)
            {
                CaretSlot step = start;
                if (!StepBack(ref step, out char c) || ClassOf(c) != target) break;
                start = step;
            }

            CaretSlot end = focus;
            while (true)
            {
                CaretSlot step = end;
                if (!StepForward(ref step, out char c) || ClassOf(c) != target) break;
                end = step;
            }

            anchor = Normalize(start);
            SetCaret(end.run, end.offset, true);
        }

        // One character each way, stepping over run boundaries but never out of the block.
        private bool StepForward(ref CaretSlot slot, out char c)
        {
            CaretSlot at = slot;
            while (at.offset >= Length(at.run))
            {
                TextControl next = AdjacentRun(at.run, 1);
                if (next == null || BlockOf(next) != BlockOf(at.run)) { c = '\0'; return false; }
                at = new CaretSlot(next, 0);
            }

            c = TextOf(at.run)[at.offset];
            slot = new CaretSlot(at.run, at.offset + 1);
            return true;
        }

        private bool StepBack(ref CaretSlot slot, out char c)
        {
            CaretSlot at = slot;
            while (at.offset == 0)
            {
                TextControl previous = AdjacentRun(at.run, -1);
                if (previous == null || BlockOf(previous) != BlockOf(at.run)) { c = '\0'; return false; }
                at = new CaretSlot(previous, Length(previous));
            }

            c = TextOf(at.run)[at.offset - 1];
            slot = new CaretSlot(at.run, at.offset - 1);
            return true;
        }

        // The first run's start to the last run's end, whatever the caret was doing.
        public void SelectAll()
        {
            List<TextControl> runs = OrderedRuns();
            if (runs.Count == 0) return;

            TextControl last = runs[^1];
            anchor = Normalize(new CaretSlot(runs[0], 0));
            SetCaret(last, Length(last), true);
        }

        // Boxes for the range anchor -> caret, one per visual line it covers. Everything unused is
        // arranged to nothing rather than destroyed — a drag would otherwise create and free
        // controls every tick, each one a pool allocation and a full paint-order permute.
        private void ArrangeSelection()
        {
            int used = 0;

            if (OrderedSelection(out CaretSlot from, out CaretSlot to))
            {
                List<TextControl> runs = OrderedRuns();
                int first = runs.IndexOf(from.run);
                int last = runs.IndexOf(to.run);

                for (int i = first; i <= last; i++)
                    used = HighlightRun(runs[i],
                        i == first ? from.offset : 0,
                        i == last ? to.offset : Length(runs[i]),
                        used);
            }

            for (int i = used; i < highlights.Count; i++)
                highlights[i].Arrange(new LayoutRect(arrangedRect.x, arrangedRect.y, 0f, 0f));
        }

        public bool HasSelection => caretRun != null && !anchor.Equals(Focus);

        // The two ends in reading order, since a drag can run backwards.
        private bool OrderedSelection(out CaretSlot from, out CaretSlot to)
        {
            from = to = default;
            if (!HasSelection) return false;

            List<TextControl> runs = OrderedRuns();
            int anchorIndex = runs.IndexOf(anchor.run);
            int caretIndex = runs.IndexOf(caretRun);
            if (anchorIndex < 0 || caretIndex < 0) return false;

            bool forward = anchorIndex < caretIndex
                || (anchorIndex == caretIndex && anchor.offset <= caretRun.cursorPosition);

            from = forward ? anchor : Focus;
            to = forward ? Focus : anchor;
            return true;
        }

        // The x span comes from CaretAt, the same function that places the caret — so the highlight
        // cannot drift from the caret drawn inside it.
        private int HighlightRun(TextControl run, int from, int to, int used)
        {
            if (run.Layout == null || to <= from) return used;

            LayoutRect inner = run.arrangedRect.Shrink(run.padding);

            foreach (TextLine line in run.Layout.lines)
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
                float lineLeft = ReferenceEquals(line, run.Layout.lines[0]) ? run.firstLineOffset : 0f;
                float left = start == lineStart ? lineLeft : run.CaretAt(start).x;
                float right = end == lineEnd ? lineLeft + line.width : run.CaretAt(end).x;

                Highlight(used++).Arrange(new LayoutRect(
                    inner.x + left, inner.y + line.top, right - left, line.height));
            }

            return used;
        }

        // Highlights live at the head of the child list so they draw behind the blocks — paint order
        // is the tree's DFS order, which is why the caret, added last, draws over the text.
        private SelectionControl Highlight(int index)
        {
            while (highlights.Count <= index)
            {
                SelectionControl box = new SelectionControl();
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
            if (!OrderedSelection(out CaretSlot from, out CaretSlot to)) return false;

            DeleteRange(from, to);
            return true;
        }

        // The head run keeps its prefix and the caret, the tail run keeps its suffix, and everything
        // the range crossed whole is destroyed.
        private void DeleteRange(CaretSlot from, CaretSlot to)
        {
            if (from.run == to.run)
            {
                DocumentAddress cut = AddressOf(from.run, from.offset);
                string removed = TextOf(from.run).Substring(from.offset, to.offset - from.offset);

                from.run.text = TextOf(from.run).Remove(from.offset, to.offset - from.offset);
                undo?.Push(new RunTextEdit(this, cut, removed, false));

                SetCaret(from.run, from.offset);
                return;
            }

            List<TextControl> runs = OrderedRuns();
            int first = runs.IndexOf(from.run);
            int last = runs.IndexOf(to.run);

            // captured before anything is destroyed, and before the addresses shift
            DocumentAddress fromAddress = AddressOf(from.run, from.offset);
            DocumentAddress toAddress = AddressOf(to.run, to.offset);
            DocumentFragment fragment = CaptureFragment(from, to, runs, first, last);

            from.run.text = TextOf(from.run)[..from.offset];
            to.run.text = TextOf(to.run)[to.offset..];

            for (int i = first + 1; i < last; i++)
                runs[i].Destroy();

            Block head = BlockOf(from.run);
            Block tail = BlockOf(to.run);
            if (head != tail) MergeIntoHead(head, tail);

            // the head run holds the caret whether or not it kept anything, so the tail run only
            // survives while it still has text
            fragment.tailRunDestroyed = Length(to.run) == 0;
            if (fragment.tailRunDestroyed) to.run.Destroy();

            head.InvalidateLayout();
            undo?.Push(new DeleteRangeEdit(this, fromAddress, toAddress, fragment));

            SetCaret(from.run, from.offset);
        }

        // Everything the range covers, as data: the head run's cut suffix, then whole runs, then
        // the tail run's cut prefix, split into one snapshot per block the range touched.
        private DocumentFragment CaptureFragment(CaretSlot from, CaretSlot to, List<TextControl> runs,
            int first, int last)
        {
            DocumentFragment fragment = new DocumentFragment();

            Block owner = null;
            BlockSnapshot current = null;

            for (int i = first; i <= last; i++)
            {
                Block block = BlockOf(runs[i]);
                if (current == null || block != owner)
                {
                    owner = block;
                    current = new BlockSnapshot { stylingType = StylingOf(block) };
                    fragment.blocks.Add(current);
                }

                string text = i == first ? TextOf(runs[i])[from.offset..]
                            : i == last ? TextOf(runs[i])[..to.offset]
                            : TextOf(runs[i]);

                current.runs.Add(RunSnapshot.Of(runs[i], text));
            }

            return fragment;
        }

        // The tail block's surviving runs move into the head block, and every block the range
        // crossed goes with the tail.
        private void MergeIntoHead(Block head, Block tail)
        {
            foreach (Entity child in tail.children.ToArray())
            {
                if (child is not TextControl run) continue;

                tail.children.Remove(run);
                head.AddChild(run);
            }

            List<Block> blocks = Blocks();
            int end = blocks.IndexOf(tail);
            for (int i = blocks.IndexOf(head) + 1; i <= end; i++)
                RemoveBlock(blocks[i]);

            head.ApplyLayout(document.layout);
        }

        // Splits the caret's block in two, the second carrying everything from the caret on.
        public void SplitBlock()
        {
            DeleteSelection();

            if (caretRun == null) return;
            SplitBlockAt(AddressOf(caretRun, caretRun.cursorPosition));
        }

        // The split itself, addressed rather than read off the caret, so redo can replay it.
        internal void SplitBlockAt(DocumentAddress at)
        {
            if (!Resolve(at, out TextControl target, out int offset)) return;
            if (target is not TextRun run || BlockOf(run) is not ContentBlock block) return;

            ContentBlock tail = new ContentBlock { stylingType = block.stylingType };
            TextRun carried = run.Clone();
            carried.text = TextOf(run)[offset..];
            run.text = TextOf(run)[..offset];
            tail.AddChild(carried);

            bool past = false;
            foreach (Entity child in block.children.ToArray())
            {
                if (child == run) { past = true; continue; }
                if (!past || child is not TextControl following) continue;

                block.children.Remove(following);
                tail.AddChild(following);
            }

            // the carried run is worth keeping only while it holds text or is all the block has
            bool carriedDropped = Length(carried) == 0 && tail.children.Count > 1;
            if (carriedDropped) carried.Destroy();

            InsertBlockAfter(block, tail);
            tail.ApplyLayout(document.layout);
            block.InvalidateLayout();

            undo?.Push(new SplitEdit(this, at, carriedDropped));

            SetCaret(tail.RunAt(0), 0);
        }

        // Records the insert before the write, because the address is read off the caret the write
        // is about to advance.
        internal void TypeChar(TextControl run, char c)
        {
            if (run == null) return;

            undo?.Push(new RunTextEdit(this, AddressOf(run, run.cursorPosition), c.ToString(), true));
            run.WriteChar(c);
            caret?.Focus();
        }

        // Blocks sit between the highlight boxes at the head of the child list and the caret at its
        // end, so a new one goes in beside its neighbour rather than at either end.
        private void InsertBlockAfter(Block after, Block block)
        {
            children.Insert(children.IndexOf(after) + 1, block);
            block.parent = this;
            MarkTreeOrderDirty();
            InvalidateLayout();

            document.blocks.Insert(document.blocks.IndexOf(after) + 1, block);
        }

        // Destroy detaches from the child list itself; the model list is the other place it lives.
        private void RemoveBlock(Block block)
        {
            document.blocks.Remove(block);
            block.Destroy();
        }

        private List<Block> Blocks()
        {
            List<Block> blocks = new List<Block>();
            foreach (Entity child in children)
                if (child is Block block) blocks.Add(block);

            return blocks;
        }
        #endregion

        #region ---- addressing ----
        internal DocumentAddress AddressOf(TextControl run, int offset)
        {
            Block owner = BlockOf(run);
            return new DocumentAddress(Blocks().IndexOf(owner), IndexOfRun(owner, run), offset);
        }

        internal bool Resolve(DocumentAddress address, out TextControl run, out int offset)
        {
            run = null;
            offset = address.offset;

            List<Block> blocks = Blocks();
            if (address.block < 0 || address.block >= blocks.Count) return false;

            run = RunIn(blocks[address.block], address.run);
            if (run == null) return false;

            offset = Math.Clamp(address.offset, 0, Length(run));
            return true;
        }

        private void CaretTo(DocumentAddress address)
        {
            if (Resolve(address, out TextControl run, out int offset))
                SetCaret(run, offset);
        }

        private static int IndexOfRun(Block block, TextControl run)
        {
            if (block == null) return -1;

            int index = 0;
            foreach (Entity child in block.children)
            {
                if (child == run) return index;
                if (child is TextControl) index++;
            }
            return -1;
        }

        private static TextControl RunIn(Block block, int index)
        {
            int i = 0;
            foreach (Entity child in block.children)
                if (child is TextControl run && i++ == index) return run;

            return null;
        }

        private static List<TextControl> RunsFrom(Block block, int index)
        {
            List<TextControl> runs = new List<TextControl>();

            int i = 0;
            foreach (Entity child in block.children)
                if (child is TextControl run && i++ >= index) runs.Add(run);

            return runs;
        }

        private static TextStyleType StylingOf(Block block) =>
            block is ContentBlock content ? content.stylingType : TextStyleType.Text;
        #endregion

        #region ---- undo primitives ----
        internal void InsertRunText(DocumentAddress at, string insert)
        {
            if (!Resolve(at, out TextControl run, out int offset)) return;

            run.text = TextOf(run).Insert(offset, insert);
            SetCaret(run, offset + insert.Length);
        }

        internal void RemoveRunText(DocumentAddress at, int count)
        {
            if (!Resolve(at, out TextControl run, out int offset)) return;
            if (offset + count > Length(run)) return;

            run.text = TextOf(run).Remove(offset, count);
            SetCaret(run, offset);
        }

        // Redo for a range delete: the document is back in its pre-delete state, so the recorded
        // addresses resolve again and the forward primitive can simply run a second time.
        internal void DeleteBetween(DocumentAddress from, DocumentAddress to)
        {
            if (!Resolve(from, out TextControl fromRun, out int fromOffset)) return;
            if (!Resolve(to, out TextControl toRun, out int toOffset)) return;

            DeleteRange(new CaretSlot(fromRun, fromOffset), new CaretSlot(toRun, toOffset));
        }

        // The inverse of a range delete. The head run takes its cut text back, whole runs are
        // rebuilt after it, and when the range crossed blocks the survivors that were merged into
        // the head block move back out into a rebuilt tail block.
        internal void InsertFragment(DocumentAddress at, DocumentFragment fragment)
        {
            if (fragment.blocks.Count == 0) return;
            if (!Resolve(at, out TextControl headRun, out int offset)) return;

            Block headBlock = BlockOf(headRun);
            if (headBlock == null) return;

            BlockSnapshot first = fragment.blocks[0];
            bool spansBlocks = fragment.blocks.Count > 1;

            headRun.text = TextOf(headRun).Insert(offset, first.runs[0].text);

            int index = IndexOfRun(headBlock, headRun) + 1;
            int headEnd = spansBlocks ? first.runs.Count : first.runs.Count - 1;
            for (int i = 1; i < headEnd; i++)
                InsertRun(headBlock, index++, first.runs[i].Build());

            if (!spansBlocks)
            {
                RestoreTailRun(headBlock, index, first.runs[^1], fragment.tailRunDestroyed);
                headBlock.ApplyLayout(document.layout);
                headBlock.InvalidateLayout();
                CaretTo(at);
                return;
            }

            List<Block> rebuilt = new List<Block>();
            for (int b = 1; b < fragment.blocks.Count - 1; b++)
                rebuilt.Add(BuildBlock(fragment.blocks[b]));

            BlockSnapshot tailSnapshot = fragment.blocks[^1];
            ContentBlock tailBlock = new ContentBlock { stylingType = tailSnapshot.stylingType };
            for (int i = 0; i < tailSnapshot.runs.Count - 1; i++)
                tailBlock.AddChild(tailSnapshot.runs[i].Build());

            // everything after the insertion point in the head block was moved there by the merge
            List<TextControl> survivors = RunsFrom(headBlock, index);
            RunSnapshot tailRun = tailSnapshot.runs[^1];

            if (!fragment.tailRunDestroyed && survivors.Count > 0)
                survivors[0].text = tailRun.text + TextOf(survivors[0]);
            else
                tailBlock.AddChild(tailRun.Build());

            foreach (TextControl run in survivors)
            {
                headBlock.children.Remove(run);
                tailBlock.AddChild(run);
            }

            Block previous = headBlock;
            foreach (Block block in rebuilt)
            {
                InsertBlockAfter(previous, block);
                block.ApplyLayout(document.layout);
                previous = block;
            }

            InsertBlockAfter(previous, tailBlock);
            tailBlock.ApplyLayout(document.layout);

            headBlock.ApplyLayout(document.layout);
            headBlock.InvalidateLayout();
            CaretTo(at);
        }

        // The inverse of a split. joinFirstRun is false when the split dropped its clone, in which
        // case the next block's first run is one that moved and has to stay whole.
        internal void JoinBlockWithNext(DocumentAddress at, bool joinFirstRun)
        {
            List<Block> blocks = Blocks();
            if (at.block < 0 || at.block + 1 >= blocks.Count) return;

            Block head = blocks[at.block];
            Block tail = blocks[at.block + 1];
            TextControl target = RunIn(head, at.run);

            foreach (Entity child in tail.children.ToArray())
            {
                if (child is not TextControl run) continue;

                if (joinFirstRun && target != null)
                {
                    target.text = TextOf(target) + TextOf(run);
                    run.Destroy();
                    joinFirstRun = false;
                    continue;
                }

                tail.children.Remove(run);
                head.AddChild(run);
            }

            RemoveBlock(tail);
            head.ApplyLayout(document.layout);
            head.InvalidateLayout();
            CaretTo(at);
        }

        private static ContentBlock BuildBlock(BlockSnapshot snapshot)
        {
            ContentBlock block = new ContentBlock { stylingType = snapshot.stylingType };
            foreach (RunSnapshot run in snapshot.runs)
                block.AddChild(run.Build());

            return block;
        }

        // The tail run survived the delete unless the fragment says otherwise, in which case it is
        // still sitting where its cut prefix belongs.
        private void RestoreTailRun(Block block, int index, RunSnapshot tail, bool destroyed)
        {
            TextControl run = destroyed ? null : RunIn(block, index);

            if (run == null) InsertRun(block, index, tail.Build());
            else run.text = tail.text + TextOf(run);
        }

        // Blocks hold only runs, so the run index is the child index; the pool is shared, so
        // marking order dirty from here is the same call InsertBlockAfter makes.
        private void InsertRun(Block block, int index, TextRun run)
        {
            int slot = block.children.Count;

            int i = 0;
            for (int c = 0; c < block.children.Count; c++)
                if (block.children[c] is TextControl && i++ == index) { slot = c; break; }

            block.children.Insert(slot, run);
            run.parent = block;
            MarkTreeOrderDirty();
            block.InvalidateLayout();
        }
        #endregion
    }
}
