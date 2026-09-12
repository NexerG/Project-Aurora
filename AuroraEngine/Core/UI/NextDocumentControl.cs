using ArctisAurora.Core.ECS.EngineEntity;
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

        private NextCaretControl caret;

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
            if (run is NextBlockControl block) SetCaret(block, index, Extending);
        }

        // A press that landed on the document itself — the gap between blocks, or past the last one.
        public override bool OnPointerPress(PointerEvent e)
        {
            if (CaretOffText(e.point, out NextBlockControl block, out int offset))
                SetCaret(block, offset, Extending);

            return true;
        }

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
