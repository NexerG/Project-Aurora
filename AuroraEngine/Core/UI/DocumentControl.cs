using ArctisAurora.Core.Diagnostics;
using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.Editing;
using ArctisAurora.Core.Filing;
using ArctisAurora.EngineWork;
using System.Numerics;
using System.Text;

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
        public readonly bool? underline;
        public readonly string colorHex;
        public readonly string highlightHex;
        public readonly int? fontSize;

        public StyleDelta(bool? bold = null, bool? italic = null, bool? strikethrough = null,
            string colorHex = null, int? fontSize = null, bool? underline = null, string highlightHex = null)
        {
            this.bold = bold;
            this.italic = italic;
            this.strikethrough = strikethrough;
            this.underline = underline;
            this.colorHex = colorHex;
            this.highlightHex = highlightHex;
            this.fontSize = fontSize;
        }

        // Everything a span carries, as a delta that would give another span the same look.
        public static StyleDelta Of(StyleSpan span) => new StyleDelta(
            span.IsBold, span.IsItalic, span.strikethrough, span.colorHex ?? string.Empty,
            span.fontSizeAuthored ? span.fontSize : null, span.underline, span.highlightHex ?? string.Empty);

        public void Apply(ref StyleSpan span)
        {
            bool isBold = bold ?? span.IsBold;
            bool isItalic = italic ?? span.IsItalic;
            span.style = isBold ? (isItalic ? FontStyle.BoldItalic : FontStyle.Bold)
                                : isItalic ? FontStyle.Italic : FontStyle.Regular;

            if (strikethrough.HasValue) span.strikethrough = strikethrough.Value;
            if (underline.HasValue) span.underline = underline.Value;
            if (colorHex != null) span.colorHex = colorHex.Length == 0 ? null : colorHex;
            if (highlightHex != null) span.highlightHex = highlightHex.Length == 0 ? null : highlightHex;
            if (fontSize.HasValue)
            {
                span.fontSizeAuthored = true;
                span.fontSize = fontSize.Value;
            }
        }

        // This delta with another laid over it; the newer one wins wherever it speaks.
        public StyleDelta With(StyleDelta over) => new StyleDelta(
            over.bold ?? bold, over.italic ?? italic, over.strikethrough ?? strikethrough,
            over.colorHex ?? colorHex, over.fontSize ?? fontSize, over.underline ?? underline,
            over.highlightHex ?? highlightHex);

        // Whether applying this would move anything on the span.
        public bool Changes(StyleSpan span) =>
            (bold.HasValue && bold != span.IsBold)
            || (italic.HasValue && italic != span.IsItalic)
            || (strikethrough.HasValue && strikethrough != span.strikethrough)
            || (underline.HasValue && underline != span.underline)
            || (colorHex != null && (colorHex.Length == 0 ? null : colorHex) != span.colorHex)
            || (highlightHex != null && (highlightHex.Length == 0 ? null : highlightHex) != span.highlightHex)
            || (fontSize.HasValue && fontSize != span.fontSize);
    }

    // The style the next character will take: the span the caret sits in, with an armed change laid
    // over it. Resolved against the block, so an unset span member reads as what it draws as.
    public readonly struct CaretStyle
    {
        public readonly bool bold;
        public readonly bool italic;
        public readonly bool strikethrough;
        public readonly bool underline;
        public readonly string colorHex;
        public readonly string highlightHex;
        public readonly int fontSize;

        public CaretStyle(BlockControl block, StyleSpan span, StyleDelta armed)
        {
            bold = armed.bold ?? span.IsBold;
            italic = armed.italic ?? span.IsItalic;
            strikethrough = armed.strikethrough ?? span.strikethrough;
            underline = armed.underline ?? span.underline;
            colorHex = armed.colorHex == null ? span.colorHex : armed.colorHex.Length == 0 ? null : armed.colorHex;
            highlightHex = armed.highlightHex == null ? span.highlightHex : armed.highlightHex.Length == 0 ? null : armed.highlightHex;
            fontSize = armed.fontSize ?? (span.fontSize > 0 ? span.fontSize : block.fontSize);
        }
    }

    // Each page's text area, repeating every stride down the document.
    public readonly struct PageBands
    {
        public readonly float top;
        public readonly float height;
        public readonly float stride;

        internal const float tolerance = 0.5f;

        public PageBands(float top, float height, float stride)
        {
            this.top = top;
            this.height = height;
            this.stride = stride;
        }

        // Page a y falls on, counting the margins and gap below a text area as its page.
        public int PageOf(float y) => Math.Max(0, (int)MathF.Floor((y - top) / stride));

        // A span that would cross its text area's bottom moves to the next page's top.
        public float Push(float y, float spanHeight)
        {
            float bandTop = top + PageOf(y) * stride;
            if (y < bandTop) return bandTop;

            float bandEnd = bandTop + height;
            if (y + spanHeight <= bandEnd + tolerance) return y;
            if (spanHeight > height && y < bandEnd) return y;
            return bandTop + stride;
        }
    }

    // The note's content area: blocks stacked top to bottom, with the caret over them.
    public class DocumentControl : ContainerControl, IGlyphPressTarget
    {
        public float blockSpacing;

        // page format, assigned by the editor before the first measure
        public PageLayout page = new PageLayout();

        // document zoom, assigned by the editor; 1 is 100%
        public float zoom = 1f;

        // page panels at the head of children, and where the last paginate put each block
        private readonly List<PanelControl> pages = new List<PanelControl>();
        private readonly List<float> blockTops = new List<float>();
        private readonly List<float> blockHeights = new List<float>();
        private readonly List<Control> blockControls = new List<Control>();
        private Vector2 measuredPaper;
        private PageBands paginatedBands;
        private int pageCount;
        private float pageHeight;

        // A control above the first block on the first page, assigned by the editor.
        public Control? header
        {
            get => field;
            set
            {
                if (field != null) field.Destroy();
                field = value;
                if (value != null) AddChild(value);
            }
        }
        private float headerHeight;

        // caret and highlight paint, assigned by the editor before either is built
        public string? caretColorHex;
        public string? selectionColorHex;

        // the model these blocks came from; the file is written from its block list
        internal RichTextDocument document = null!;

        // the open note's history, assigned by the editor; null until a session exists
        internal UndoStack undo;

        private CaretControl caret;
        private readonly List<PanelControl> highlights = new List<PanelControl>();
        private readonly List<BlockControl> selectedBlocks = new List<BlockControl>();

        public BlockControl caretBlock { get; private set; }
        public int caretOffset { get; private set; }

        // Where a selection started; equal to the caret means nothing is selected.
        private CaretSlot anchor;

        // a style change with nothing to apply it to, spent on the next character typed
        private StyleDelta pending;

        // a selection picked up by a press, and where it would land
        internal bool textDragging { get; private set; }
        internal bool textDragHovered;
        private CaretSlot textDragPress;
        private CaretControl dropCaret;
        private CaretSlot? dropSlot;

        // list numbers and markers are stale; resolved at the next measure
        private bool listsDirty = true;

        // the last copy with its formatting, for a paste of the same text
        private static string? copiedText;
        private static DocumentFragment? copiedFragment;

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
                caret = new CaretControl { hitTestable = false };
                caret.PaintOr(caretColorHex, PaletteRole.Ink);
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

            PressAt(block, index);
        }

        // A press that landed on the document itself — the gap between blocks, or past the last one.
        public override bool OnPointerPress(PointerEvent e)
        {
            if (CaretOffText(e.point, out BlockControl block, out int offset))
                PressAt(block, offset);

            return true;
        }

        // A press inside the selection picks it up; anywhere else places the caret.
        private void PressAt(BlockControl block, int offset)
        {
            DisarmStyle();
            if (!Extending && InSelection(block, offset) && BeginTextDrag(new CaretSlot(block, offset))) return;

            SetCaret(block, offset, Extending);
            Editor?.BeginSelectionDrag();
        }

        // Two clicks take the word, three the visual line.
        public override bool OnPointerTap(PointerEvent e)
        {
            if (e.tapCount >= 2) DisarmStyle();
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

            foreach (BlockControl candidate in Blocks())
            {
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
            List<BlockControl> blocks = Blocks();
            return blocks.Count > 0 ? blocks[^1] : null;
        }

        private static float Distance(float value, float low, float high) =>
            value < low ? low - value : (value > high ? value - high : 0f);
        #endregion

        #region ---- selection ----
        public bool HasSelection => caretBlock != null && !anchor.Equals(Focus);

        // The run of one character class around the caret, inside the block.
        public void SelectWord()
        {
            if (caretBlock == null) return;

            string s = caretBlock.text ?? string.Empty;
            TextInputActions.CharClass? right = caretOffset < s.Length ? TextInputActions.ClassOf(s[caretOffset]) : null;
            TextInputActions.CharClass? left = caretOffset > 0 ? TextInputActions.ClassOf(s[caretOffset - 1]) : null;

            // either edge of a word takes the word, not the space beside it
            TextInputActions.CharClass? picked = right == TextInputActions.CharClass.Word || left == TextInputActions.CharClass.Word
                ? TextInputActions.CharClass.Word : right ?? left;
            if (picked == null) return;

            int start = caretOffset;
            while (start > 0 && TextInputActions.ClassOf(s[start - 1]) == picked) start--;

            int end = caretOffset;
            while (end < s.Length && TextInputActions.ClassOf(s[end]) == picked) end++;

            anchor = new CaretSlot(caretBlock, start);
            SetCaret(caretBlock, end, true);
        }

        // The first block's start to the last block's end, whatever the caret was doing.
        public void SelectAll()
        {
            List<BlockControl> blocks = Blocks();
            if (blocks.Count == 0) return;

            DisarmStyle();
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

        // Puts a selection back by address; equal ends leave only a caret.
        internal void Select(DocumentAddress from, DocumentAddress to)
        {
            if (!Resolve(from, out BlockControl anchorBlock, out int anchorOffset)) return;
            if (!Resolve(to, out BlockControl block, out int offset)) return;

            anchor = new CaretSlot(anchorBlock, anchorOffset);
            SetCaret(block, offset, true);
        }

        internal bool InSelection(BlockControl block, int offset)
        {
            if (!OrderedSelection(out DocumentAddress from, out DocumentAddress to)) return false;

            DocumentAddress at = AddressOf(block, offset);
            return !Before(at, from) && !Before(to, at);
        }

        private static bool Before(DocumentAddress a, DocumentAddress b) =>
            a.block < b.block || (a.block == b.block && a.offset < b.offset);

        // The selection as data, when it sits in one container and so can be lifted out.
        internal DocumentFragment? SelectedFragment()
        {
            if (!OrderedSelection(out DocumentAddress from, out DocumentAddress to)) return null;
            return OneContainer(from.block, to.block) ? CaptureFragment(from, to) : null;
        }

        // Puts the selection on the clipboard as plain text, keeping its formatting for our own paste.
        internal bool CopySelection()
        {
            if (!OrderedSelection(out DocumentAddress from, out DocumentAddress to)) return false;

            DocumentFragment fragment = CaptureFragment(from, to);
            string text = string.Join(Environment.NewLine, fragment.blocks.Select(block => block.text));

            copiedText = text;
            copiedFragment = fragment;
            ClipboardText.Set(text);
            return true;
        }

        // Boxes for the selected range, one per visual line it covers. Everything unused is arranged
        // to nothing rather than destroyed — a drag would otherwise create and free controls every
        // tick, each one a pool allocation and a full paint-order permute.
        private void ArrangeSelection()
        {
            int used = 0;

            foreach (BlockControl marked in selectedBlocks)
                marked.selectedFrom = marked.selectedTo = -1;
            selectedBlocks.Clear();

            if (OrderedSelection(out DocumentAddress from, out DocumentAddress to))
            {
                List<BlockControl> blocks = Blocks();

                for (int i = from.block; i <= to.block && i < blocks.Count; i++)
                {
                    BlockControl block = blocks[i];
                    block.selectedFrom = i == from.block ? from.offset : 0;
                    block.selectedTo = i == to.block ? to.offset : block.Length;
                    selectedBlocks.Add(block);

                    used = HighlightBlock(block, block.selectedFrom, block.selectedTo, used);
                }
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
            ScrollableControl? viewport = TableViewport(block);

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

                LayoutRect box = new LayoutRect(origin.X + left, origin.Y + line.top, right - left, line.height);
                if (viewport != null) box = LayoutRect.Intersect(box, viewport.arrangedRect);
                if (box.width <= 0f) continue;

                Highlight(used++).Arrange(box);
            }

            return used;
        }

        // Highlights live at the head of the child list so they draw behind the blocks — paint order
        // is the tree's DFS order, which is why the caret, added last, draws over the text.
        private PanelControl Highlight(int index)
        {
            while (highlights.Count <= index)
            {
                PanelControl box = new PanelControl { hitTestable = false };
                box.PaintOr(selectionColorHex, PaletteRole.Line);
                box.parent = this;
                children.Insert(pages.Count + highlights.Count, box);
                highlights.Add(box);
                MarkTreeOrderDirty();
                box.Measure(arrange.measuredOffer);
            }
            return highlights[index];
        }
        #endregion

        #region ---- editing ----
        // Removes the selected range; false when nothing was selected.
        public bool DeleteSelection(bool restoreSelection = true)
        {
            if (!OrderedSelection(out DocumentAddress from, out DocumentAddress to)) return false;
            if (!OneContainer(from.block, to.block)) return false;

            DocumentAddress anchorAt = AddressOf(anchor.block, anchor.offset);
            DocumentAddress caretAt = restoreSelection ? AddressOf(caretBlock, caretOffset) : anchorAt;

            DocumentFragment fragment = CaptureFragment(from, to);
            undo?.Push(new DeleteRangeEdit(this, from, to, fragment, anchorAt, caretAt));
            DeleteBetween(from, to);
            KeepDeletedStyle(fragment);
            return true;
        }

        // What is typed next looks like the first character deleted; a style chosen outright still wins.
        private void KeepDeletedStyle(DocumentFragment fragment)
        {
            foreach (BlockSnapshot block in fragment.blocks)
                foreach (StyleSpan span in block.spans)
                {
                    if (span.count == 0) continue;

                    StyleDelta kept = StyleDelta.Of(span);
                    if (kept.Changes(caretBlock.StyleAt(caretOffset))) pending = kept.With(pending);
                    return;
                }
        }

        internal void DisarmStyle() => pending = default;

        // Replaces the selection with text; the caret ends after it.
        internal bool PasteText(string text)
        {
            if (HasSelection && !DeleteSelection()) return false;
            if (caretBlock == null) return false;

            DocumentFragment fragment = text == copiedText && copiedFragment != null
                ? copiedFragment
                : FragmentFromText(text, caretBlock, caretOffset);

            Insert(AddressOf(caretBlock, caretOffset), ForDestination(fragment, caretBlock));
            DisarmStyle();
            return true;
        }

        // Puts a fragment in at a slot and leaves it selected — a drop from another note.
        internal void InsertAt(CaretSlot slot, DocumentFragment fragment)
        {
            DocumentAddress from = AddressOf(slot.block, slot.offset);
            DocumentAddress to = Insert(from, ForDestination(fragment, slot.block));
            Select(from, to);
            DisarmStyle();
        }

        // Moves or copies the selection to a slot, selected there; false for a slot inside it.
        internal bool DropSelection(CaretSlot slot, bool copy)
        {
            if (!OrderedSelection(out DocumentAddress from, out DocumentAddress to)) return false;
            if (InSelection(slot.block, slot.offset)) return false;

            DocumentFragment fragment = SelectedFragment();
            if (fragment == null) return false;

            DocumentAddress at = AddressOf(slot.block, slot.offset);
            if (!copy)
            {
                DeleteSelection();
                if (Before(to, at))
                    at = at.block == to.block
                        ? new DocumentAddress(from.block, from.offset + at.offset - to.offset)
                        : new DocumentAddress(at.block - (to.block - from.block), at.offset);
            }

            if (!Resolve(at, out BlockControl block, out _)) return false;
            DocumentAddress end = Insert(at, ForDestination(fragment, block));
            Select(at, end);
            DisarmStyle();
            return true;
        }

        // Records and performs an insert; returns where it ends.
        private DocumentAddress Insert(DocumentAddress from, DocumentFragment fragment)
        {
            DocumentAddress to = fragment.blocks.Count == 1
                ? new DocumentAddress(from.block, from.offset + fragment.blocks[0].text.Length)
                : new DocumentAddress(from.block + fragment.blocks.Count - 1, fragment.blocks[^1].text.Length);

            undo?.Push(new InsertRangeEdit(this, from, to, fragment));
            InsertBetween(from, to, fragment);
            return to;
        }

        // Plain text as paragraphs, each in the style and block kind at the caret.
        private static DocumentFragment FragmentFromText(string text, BlockControl at, int offset)
        {
            StyleSpan style = at.StyleAt(offset);
            DocumentFragment fragment = new DocumentFragment();

            foreach (string line in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                StringBuilder clean = new StringBuilder(line.Length);
                foreach (char c in line)
                    if (c == '\t') clean.Append(' ');
                    else if (!char.IsControl(c)) clean.Append(c);

                BlockSnapshot block = new BlockSnapshot
                {
                    stylingType = at.stylingType,
                    listKind = at.listKind,
                    listLevel = at.listLevel,
                    listMarker = at.listMarker,
                    text = clean.ToString()
                };

                StyleSpan span = style;
                span.count = block.text.Length;
                block.spans.Add(span);
                fragment.blocks.Add(block);
            }

            return fragment;
        }

        // The last block of a multi-block fragment takes the destination block's kind.
        private static DocumentFragment ForDestination(DocumentFragment fragment, BlockControl destination)
        {
            if (fragment.blocks.Count < 2) return fragment;

            BlockSnapshot last = fragment.blocks[^1];
            BlockSnapshot landed = new BlockSnapshot
            {
                stylingType = destination.stylingType,
                listKind = destination.listKind,
                listLevel = destination.listLevel,
                listMarker = destination.listMarker,
                isChecked = destination.isChecked,
                text = last.text
            };
            landed.spans.AddRange(last.spans);

            DocumentFragment reshaped = new DocumentFragment();
            reshaped.blocks.AddRange(fragment.blocks.Take(fragment.blocks.Count - 1));
            reshaped.blocks.Add(landed);
            return reshaped;
        }

        // Splits the caret's block in two, the second carrying everything from the caret on.
        public void SplitBlock()
        {
            DeleteSelection();

            if (caretBlock == null) return;
            if (caretBlock.Length == 0 && caretBlock.listKind != ListKind.None)
            {
                if (caretBlock.listLevel > 0) ShiftListLevel(-1);
                else ClearListAtCaret();
                return;
            }
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

            if (c == ' ' && TypeListPrefix(block, at.block, at.offset + 1))
            {
                pending = armed;
                return;
            }

            if (!armed.Changes(block.StyleAt(at.offset + 1))) return;

            ApplyStyleTo(at, new DocumentAddress(at.block, at.offset + 1), armed);
            CollapseSelection();
        }

        // Blocks sit between the highlight boxes at the head of the child list and the caret at its
        // end, so a new one goes in beside its neighbour rather than at either end.
        private void InsertBlockAfter(BlockControl after, BlockControl block)
        {
            if (after.parent is StackPanelControl cell)
            {
                cell.children.Insert(cell.children.IndexOf(after) + 1, block);
                block.parent = cell;
                MarkTreeOrderDirty();
                cell.InvalidateLayout();
                ListsChanged();
                return;
            }

            children.Insert(children.IndexOf(after) + 1, block);
            block.parent = this;
            MarkTreeOrderDirty();
            InvalidateLayout();
            ListsChanged();

            document.blocks.Insert(document.blocks.IndexOf(after) + 1, block);
        }

        // Destroy detaches from the child list itself; the model list is the other place it lives.
        private void RemoveBlock(BlockControl block)
        {
            document.blocks.Remove(block);
            block.Destroy();
            ListsChanged();
        }

        // Every block in reading order, a table's cells included.
        internal List<BlockControl> Blocks()
        {
            List<BlockControl> blocks = new List<BlockControl>();
            foreach (Entity child in children)
            {
                if (child is BlockControl block) blocks.Add(block);
                else if (TableIn(child) is TableControl table) table.AppendBlocks(blocks);
            }

            return blocks;
        }

        // The table a child of the document holds, when it is a table's viewport.
        private static TableControl? TableIn(Entity child)
        {
            if (child is not ScrollableControl viewport) return null;

            foreach (Entity entry in viewport.children)
                if (entry is TableControl table) return table;

            return null;
        }

        // The viewport of the table a block sits in; null for a block of the note itself.
        internal static ScrollableControl? TableViewport(BlockControl? block) =>
            block?.parent?.parent is TableControl table ? table.parent as ScrollableControl : null;

        // Whether every block from first to last shares one parent — the note, or one cell.
        private bool OneContainer(int first, int last)
        {
            List<BlockControl> blocks = Blocks();
            if (first < 0 || last >= blocks.Count) return false;

            for (int b = first + 1; b <= last; b++)
                if (blocks[b].parent != blocks[first].parent) return false;

            return true;
        }
        #endregion

        #region ---- text drag ----
        // Picks the selection up; the drop marker is what the drag carries.
        private bool BeginTextDrag(CaretSlot press)
        {
            if (SelectedFragment() == null) return false;

            EnsureDropCaret();
            textDragging = true;
            textDragHovered = false;
            textDragPress = press;
            dropCaret.RegisterOnDragStop(EndTextDrag);
            dropCaret.StartDrag();
            return true;
        }

        // A release nothing took, never over an editor, is a click at the press.
        private void EndTextDrag(bool accepted)
        {
            textDragging = false;
            HideDrop();

            if (!accepted && !textDragHovered) SetCaret(textDragPress.block, textDragPress.offset);
        }

        // The document whose selection a drag carries, or null for any other drag.
        internal static DocumentControl? TextDragSource(Control dragged) =>
            dragged is CaretControl { parent: DocumentControl { textDragging: true } source } ? source : null;

        internal void ShowDropAt(Vector2 point)
        {
            if (!CaretOffText(point, out BlockControl block, out int offset)) return;

            EnsureDropCaret();
            dropSlot = new CaretSlot(block, offset);
            dropCaret.Focus();
            InvalidateArrange();
        }

        internal void HideDrop()
        {
            if (dropSlot == null) return;

            dropSlot = null;
            dropCaret?.Blur();
            InvalidateArrange();
        }

        private void EnsureDropCaret()
        {
            if (dropCaret != null) return;

            dropCaret = new CaretControl { hitTestable = false };
            dropCaret.PaintOr(caretColorHex, PaletteRole.Ink);
            dropCaret.Blur();
            AddChild(dropCaret);
        }
        #endregion

        #region ---- lists ----
        // "- " at a block's start makes a bullet; "[ ] " or "[x] " at a bullet's start makes a task.
        private bool TypeListPrefix(BlockControl block, int index, int typedEnd)
        {
            if (block.stylingType == TextStyleType.Code || block.parent != this) return false;

            string head = (block.text ?? string.Empty)[..typedEnd];
            ListKind kind;
            ListMarker? marker = null;
            if (block.listKind == ListKind.None && head == "- ") kind = ListKind.Bullet;
            else if (block.listKind == ListKind.None && numberPrefix.IsMatch(head))
            {
                kind = ListKind.Bullet;
                marker = ListMarker.Decimal;
            }
            else if (block.listKind == ListKind.Bullet && (head == "[ ] " || head == "[x] ")) kind = ListKind.Task;
            else return false;

            DocumentAddress start = new DocumentAddress(index, 0);
            undo?.Push(new TextEdit(this, start, head, false));
            RemoveText(start, head.Length);

            SetBlockList(index, b =>
            {
                b.listKind = kind;
                b.isChecked = head == "[x] ";
                if (kind == ListKind.Bullet) b.listMarker = marker;
            });
            return true;
        }

        // "1. " or "1) ", the prefix that starts a numbered list
        private static readonly System.Text.RegularExpressions.Regex numberPrefix =
            new System.Text.RegularExpressions.Regex(@"^\d{1,9}[.)] $");

        // Gives every item at the caret's level of the caret's list one marker, as one step.
        internal bool SetListMarker(ListMarker marker)
        {
            if (caretBlock == null || caretBlock.listKind != ListKind.Bullet) return false;

            List<BlockControl> blocks = Blocks();
            BlockControl caretItem = caretBlock;
            int level = caretItem.listLevel;
            bool SameList(BlockControl b) =>
                b.parent == caretItem.parent && b.listKind == ListKind.Bullet && b.listLevel >= level;

            int first = blocks.IndexOf(caretItem);
            int last = first;
            while (first > 0 && SameList(blocks[first - 1])) first--;
            while (last + 1 < blocks.Count && SameList(blocks[last + 1])) last++;

            List<BlockSnapshot> before = SnapshotBlocks(first, last);
            for (int b = first; b <= last; b++)
                if (blocks[b].listLevel == level) blocks[b].listMarker = marker;

            undo?.Push(new BlockStateEdit(this, first, before, SnapshotBlocks(first, last)));
            ListsChanged();
            return true;
        }

        internal void ListsChanged()
        {
            listsDirty = true;
            InvalidateLayout();
        }

        // Numbers every list item and hands it the marker it shows. A plain block ends every list above
        // it; a task ends the numbering at its own level and deeper; a marker change at a level starts
        // a new list there; a container starts afresh.
        private void RenumberLists()
        {
            listsDirty = false;
            List<(int count, ListMarker marker)> counters = new List<(int, ListMarker)>();
            object container = null;

            foreach (BlockControl block in Blocks())
            {
                if (block.parent != container)
                {
                    counters.Clear();
                    container = block.parent;
                }
                if (block.listKind == ListKind.None)
                {
                    counters.Clear();
                    continue;
                }

                int level = block.listLevel;
                if (counters.Count > level + 1) counters.RemoveRange(level + 1, counters.Count - level - 1);
                if (block.listKind == ListKind.Task)
                {
                    if (counters.Count > level) counters.RemoveRange(level, counters.Count - level);
                    continue;
                }

                ListMarker marker = block.listMarker ?? document.layout.MarkerFor(level);
                while (counters.Count <= level) counters.Add((0, marker));
                int number = counters[level].marker == marker ? counters[level].count + 1 : 1;
                counters[level] = (number, marker);
                block.ShowMarker(marker, number);
            }
        }

        // Turns the caret's list item back into a plain block, when the caret sits at its start.
        internal bool ClearListAtCaret()
        {
            if (caretBlock == null || HasSelection || caretOffset != 0 || caretBlock.listKind == ListKind.None)
                return false;

            SetBlockList(Blocks().IndexOf(caretBlock), b =>
            {
                b.listKind = ListKind.None;
                b.listLevel = 0;
                b.listMarker = null;
                b.isChecked = false;
            });
            return true;
        }

        // Nests or un-nests the list items the range touches, never deeper than one past the item above.
        internal bool ShiftListLevel(int delta)
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

            List<BlockControl> blocks = Blocks();
            List<BlockSnapshot> before = SnapshotBlocks(first, last);
            bool changed = false;

            for (int b = first; b <= last && b < blocks.Count; b++)
            {
                BlockControl block = blocks[b];
                if (block.listKind == ListKind.None) continue;

                int deepest = b > 0 && blocks[b - 1].listKind != ListKind.None ? blocks[b - 1].listLevel + 1 : 0;
                int level = delta > 0 ? Math.Min(block.listLevel + 1, deepest) : block.listLevel - 1;
                if (level < 0 || (delta > 0 && level <= block.listLevel)) continue;

                block.listLevel = level;
                block.ApplyLayout(document.layout);
                changed = true;
            }

            if (changed)
            {
                undo?.Push(new BlockStateEdit(this, first, before, SnapshotBlocks(first, last)));
                ListsChanged();
            }
            return changed;
        }

        // One block's list state rewritten as one undoable record.
        internal void SetBlockList(int index, Action<BlockControl> change)
        {
            List<BlockControl> blocks = Blocks();
            if (index < 0 || index >= blocks.Count) return;

            List<BlockSnapshot> before = SnapshotBlocks(index, index);
            change(blocks[index]);
            blocks[index].ApplyLayout(document.layout);
            undo?.Push(new BlockStateEdit(this, index, before, SnapshotBlocks(index, index)));
            ListsChanged();
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
            ListsChanged();
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

        // A fragment put in forwards, for a paste or a drop and their redo; the caret ends after it.
        internal void InsertBetween(DocumentAddress from, DocumentAddress to, DocumentFragment fragment)
        {
            InsertFragment(from, fragment);
            CaretTo(to);
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

        #region ---- pages ----
        // Places every block down the pages and returns the document's height.
        private float Paginate(Vector2 paper, int from, int to)
        {
            float top = Mm(page.marginTop);
            float bottom = Mm(page.marginBottom);
            bool paged = page.mode == PageMode.Paged;
            PageBands bands = new PageBands(top, paged ? paper.Y - top - bottom : float.PositiveInfinity, paper.Y + page.gap * zoom);
            if (bands.top != paginatedBands.top || bands.height != paginatedBands.height || bands.stride != paginatedBands.stride)
            {
                from = 0;
                to = int.MaxValue;
            }
            paginatedBands = bands;

            float y = from == 0 ? top + headerHeight : blockTops[from - 1] + blockHeights[from - 1];
            int index = 0;
            bool settled = false;
            foreach (Entity child in children)
            {
                BlockControl block = child as BlockControl;
                TableControl table = block == null ? TableIn(child) : null;
                if (block == null && table == null) continue;
                if (index < from)
                {
                    index++;
                    continue;
                }

                if (index > 0) y += blockSpacing * zoom;

                Control item = block ?? (Control)child;
                float blockTop = y;
                if (table != null) blockTop = bands.Push(y, table.FirstRowHeight);
                else if (block.Lines is { Count: > 0 } lines) blockTop = bands.Push(y, lines[0].height);

                if (index > to && blockTops[index] == blockTop)
                {
                    settled = true;
                    break;
                }

                float height = table != null ? table.Paginate(blockTop, bands) : block.Paginate(blockTop, bands);
                if (index < blockControls.Count)
                {
                    blockTops[index] = blockTop;
                    blockHeights[index] = height;
                    blockControls[index] = item;
                }
                else
                {
                    blockTops.Add(blockTop);
                    blockHeights.Add(height);
                    blockControls.Add(item);
                }
                y = blockTop + height;
                index++;
            }

            if (settled) y = blockTops[^1] + blockHeights[^1];
            else if (index < blockControls.Count)
            {
                blockTops.RemoveRange(index, blockTops.Count - index);
                blockHeights.RemoveRange(index, blockHeights.Count - index);
                blockControls.RemoveRange(index, blockControls.Count - index);
            }

            if (!paged)
            {
                pageCount = 1;
                pageHeight = MathF.Max(paper.Y, y + bottom);
                return pageHeight;
            }

            pageCount = bands.PageOf(y - PageBands.tolerance) + 1;
            pageHeight = paper.Y;
            return pageCount * paper.Y + (pageCount - 1) * page.gap * zoom;
        }

        private static bool Remeasured(Control control) => ((ArrangeFlags)control.arrange.flags & ArrangeFlags.Remeasured) != 0;

        // Millimetres on the page to design pixels at the current zoom.
        private float Mm(float mm) => mm * PageLayout.PxPerMm * zoom;

        // Page panels live at the head of the child list, behind the highlights and the text.
        private void EnsurePages()
        {
            while (pages.Count < pageCount)
            {
                PanelControl sheet = new PanelControl
                {
                    hitTestable = false,
                    edgeRole = PaletteRole.Line,
                    edgeThickness = new Thickness(1f)
                };
                sheet.PaintOr(null, PaletteRole.Surface);
                sheet.parent = this;
                children.Insert(pages.Count, sheet);
                pages.Add(sheet);
                MarkTreeOrderDirty();
            }
        }

        private void ArrangePages(float x, float y, float width)
        {
            for (int i = 0; i < pages.Count; i++)
                pages[i].Arrange(i < pageCount
                    ? new LayoutRect(x, y + i * (pageHeight + page.gap * zoom), width, pageHeight)
                    : new LayoutRect(x, y, 0f, 0f));
        }
        #endregion

        #region ---- layout ----
        protected override Vector2 MeasureCore(Vector2 availableSize)
        {
            if (listsDirty) RenumberLists();

            Vector2 paper = page.SizePx() * zoom;
            if (isMeasureDirty || paper != measuredPaper || LayoutEngine.NoSkip)
            {
                measuredPaper = paper;
                float textWidth = MathF.Max(0f, paper.X - Mm(page.marginLeft + page.marginRight));

                Profiling.Zone.Start("Document.MeasureBlocks");
                int from = -1;
                int to = -1;
                int count = 0;
                foreach (Entity child in children)
                {
                    Control item;
                    if (child is BlockControl block)
                    {
                        block.SetZoom(zoom);
                        item = block;
                    }
                    else if (TableIn(child) is TableControl table)
                    {
                        table.SetZoom(zoom);
                        item = (Control)child;
                    }
                    else continue;

                    item.Measure(new Vector2(textWidth, float.MaxValue));
                    if (Remeasured(item) || count >= blockControls.Count || blockControls[count] != item)
                    {
                        if (from < 0) from = count;
                        to = count;
                    }
                    count++;
                }
                if (from < 0) from = count;
                Profiling.Zone.End("Document.MeasureBlocks");

                float headerBefore = headerHeight;
                headerHeight = header?.Measure(new Vector2(textWidth, float.MaxValue)).Y ?? 0f;
                if (headerHeight != headerBefore) from = 0;

                Profiling.Zone.Start("Document.Paginate");
                float height = Paginate(paper, from, to);
                Profiling.Zone.End("Document.Paginate");

                EnsurePages();
                arrange.desired = new Vector2(paper.X, height);
            }

            caret?.Measure(availableSize);
            foreach (PanelControl box in highlights)
                box.Measure(availableSize);
            foreach (PanelControl sheet in pages)
                sheet.Measure(availableSize);

            SetFlag(ArrangeFlags.MeasureDirty, false);
            return arrange.desired;
        }

        protected override void ArrangeCore(LayoutRect finalRect)
        {
            WriteArranged(finalRect);

            LayoutRect inner = finalRect.Shrink(arrange.padding);
            Vector2 paper = page.SizePx() * zoom;
            float x = inner.x + MathF.Max(0f, (inner.width - paper.X) * 0.5f);
            float textX = x + Mm(page.marginLeft);
            float textWidth = MathF.Max(0f, paper.X - Mm(page.marginLeft + page.marginRight));

            ArrangePages(x, inner.y, paper.X);
            header?.Arrange(new LayoutRect(textX, inner.y + Mm(page.marginTop), textWidth, headerHeight));

            Profiling.Zone.Start("Document.ArrangeBlocks");
            int index = 0;
            foreach (Entity child in children)
            {
                if ((child is not BlockControl && TableIn(child) == null) || index >= blockTops.Count) continue;

                ((Control)child).Arrange(new LayoutRect(textX, inner.y + blockTops[index], textWidth, blockHeights[index]));
                index++;
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
            if (caret != null) ArrangeCaretAt(caret, caretBlock, caretOffset);
            if (dropCaret != null) ArrangeCaretAt(dropCaret, dropSlot?.block, dropSlot?.offset ?? 0);
        }

        private static void ArrangeCaretAt(CaretControl marker, BlockControl block, int offset)
        {
            if (block == null)
            {
                marker.Arrange(LayoutRect.Empty);
                return;
            }

            CaretGeometry geometry = block.CaretAt(offset);
            Vector2 origin = block.TextOrigin;

            LayoutRect rect = new LayoutRect(origin.X + geometry.x, origin.Y + geometry.top,
                CaretControl.Width, geometry.height);
            if (TableViewport(block) is ScrollableControl viewport && !viewport.arrangedRect.Overlaps(rect))
                rect = LayoutRect.Empty;

            marker.Arrange(rect);
        }

        // Walks only the pages and blocks the clip touches, in child order.
        internal override int CollectChildren(float z)
        {
            LayoutRect clip = arrange.clip;
            float top = arrange.arranged.Shrink(arrange.padding).y;
            float from = clip.y - top;
            float to = clip.Bottom - top;
            int walked = 0;

            float stride = pageHeight + page.gap * zoom;
            int sheets = Math.Min(pageCount, pages.Count);
            for (int i = Math.Max(0, (int)(from / stride)); i < sheets && i * stride < to; i++)
                walked += UIEngine.Collect(pages[i], z);

            foreach (PanelControl box in highlights)
                walked += UIEngine.Collect(box, z);
            if (header != null) walked += UIEngine.Collect(header, z);

            int low = 0;
            int high = blockControls.Count;
            while (low < high)
            {
                int mid = (low + high) / 2;
                if (blockTops[mid] + blockHeights[mid] <= from) low = mid + 1;
                else high = mid;
            }
            for (int i = low; i < blockControls.Count && blockTops[i] < to; i++)
                walked += UIEngine.Collect(blockControls[i], z);

            if (caret != null) walked += UIEngine.Collect(caret, z);
            if (dropCaret != null) walked += UIEngine.Collect(dropCaret, z);
            return walked;
        }
        #endregion
    }
}
