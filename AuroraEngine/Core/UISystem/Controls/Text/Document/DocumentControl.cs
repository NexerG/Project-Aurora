using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.Registry.Assets;
using ArctisAurora.Core.UISystem.Controls.Containers;
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
        internal RichTextDocument document;

        // caret target
        private CaretControl caret;
        public TextControl caretRun { get; private set; }

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
                from.run.text = TextOf(from.run).Remove(from.offset, to.offset - from.offset);
                SetCaret(from.run, from.offset);
                return;
            }

            List<TextControl> runs = OrderedRuns();
            int first = runs.IndexOf(from.run);
            int last = runs.IndexOf(to.run);

            from.run.text = TextOf(from.run)[..from.offset];
            to.run.text = TextOf(to.run)[to.offset..];

            for (int i = first + 1; i < last; i++)
                runs[i].Destroy();

            Block head = BlockOf(from.run);
            Block tail = BlockOf(to.run);
            if (head != tail) MergeIntoHead(head, tail);

            // the head run holds the caret whether or not it kept anything, so the tail run only
            // survives while it still has text
            if (Length(to.run) == 0) to.run.Destroy();

            head.InvalidateLayout();
            SetCaret(from.run, from.offset);
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

            if (caretRun is not TextRun run || BlockOf(run) is not ContentBlock block) return;

            ContentBlock tail = new ContentBlock { stylingType = block.stylingType };
            TextRun carried = run.Clone();
            carried.text = TextOf(run)[run.cursorPosition..];
            run.text = TextOf(run)[..run.cursorPosition];
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
            if (Length(carried) == 0 && tail.children.Count > 1) carried.Destroy();

            InsertBlockAfter(block, tail);
            tail.ApplyLayout(document.layout);
            block.InvalidateLayout();

            SetCaret(tail.RunAt(0), 0);
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
    }
}
