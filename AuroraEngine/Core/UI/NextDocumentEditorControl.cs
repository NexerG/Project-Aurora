using ArctisAurora.Core.Editing;
using ArctisAurora.Core.Filing.Serialization;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.UISystem.Controls.Text;
using Silk.NET.Maths;

namespace ArctisAurora.Core.UI
{
    // One open note: the scroll viewport, the document under it, and the session behind that.
    [A_XSDType("NextDocumentEditor", "UI")]
    public class NextDocumentEditorControl : NextScrollableControl, IContext
    {
        public NextRichTextDocument activeDocument { get; private set; } = null!;
        public NextDocumentEditSession session { get; private set; }

        [A_XSDElementProperty("CaretColorHex", "UI", "Color of the insertion caret.")]
        public string caretColorHex
        {
            get => field;
            set { field = value; if (content != null) content.caretColorHex = value; }
        } = "#FFFFFF";

        [A_XSDElementProperty("SelectionColorHex", "UI", "Ground of the selection highlight behind the text.")]
        public string selectionColorHex
        {
            get => field;
            set { field = value; if (content != null) content.selectionColorHex = value; }
        } = "#264F78";

        private const float autoScrollRate = 0.25f;

        private NextDocumentControl content;

        // honoured at the end of Arrange, once every block has this frame's lines
        private bool scrollToCaretPending;

        public NextDocumentEditorControl()
        {
            scrollDirection = ScrollDirection.Vertical;
            overscroll = 0.5f;
            alpha = 0f;
        }

        [A_XSDElementProperty("Source", "UI", "Engine-XML note file to load into the editor.")]
        public string source
        {
            get => field;
            set
            {
                field = value;
                if (!string.IsNullOrEmpty(value))
                    LoadPath(value);
            }
        }

        // Normalized, because the session's path is what identifies an open note: a Source authored
        // relative to the documents folder resolves through Path.Combine, which leaves the ".."
        // segments in, and would not string-match the same file reached from a folder listing.
        public void LoadPath(string nameOrPath)
        {
            string path = Path.GetFullPath(Path.IsPathRooted(nameOrPath) ? nameOrPath : Paths.Doc(nameOrPath));
            NextRichTextDocument document = NextRichTextDocument.ParseXML(path);

            session = new NextDocumentEditSession(document, path);
            LoadDocument(document);
        }

        public void LoadDocument(NextRichTextDocument document)
        {
            activeDocument = document;

            // Only the content: the scroll thumbs are children too, and the fields holding them are
            // never rebuilt.
            content?.Destroy();

            content = new NextDocumentControl
            {
                blockSpacing = document.layout.blockSpacing,
                document = document,
                caretColorHex = caretColorHex,
                selectionColorHex = selectionColorHex,
                undo = session?.undo,
                alpha = 0f
            };
            AddChild(content);

            foreach (NextBlockControl block in document.blocks)
            {
                block.ApplyLayout(document.layout);
                content.AddChild(block);
            }
        }

        public void Save() => session?.Save();

        #region ---- history ----
        // Every path that changes the document ends here, so the close paths can tell an edited note
        // from one that was only opened.
        public void MarkDirty() => session?.MarkDirty();

        // One user action's worth of edits. A note with no session has no history, and the default
        // scope discards what is pushed into it.
        public EditScope BeginStep(string label) => session != null ? session.undo.Begin(label) : default;

        public void Undo()
        {
            if (session == null || !session.undo.Undo()) return;

            MarkDirty();
            RequestScrollToCaret();
        }

        public void Redo()
        {
            if (session == null || !session.undo.Redo()) return;

            MarkDirty();
            RequestScrollToCaret();
        }
        #endregion

        #region ---- selection ----
        public void CollapseSelection() => content?.CollapseSelection();

        public void SelectAll() => content?.SelectAll();

        public bool DeleteSelection()
        {
            if (content == null || !content.DeleteSelection()) return false;

            MarkDirty();
            return true;
        }

        public NextBlockControl CaretBlock => content?.caretBlock;

        // Two clicks take the word, three the visual line.
        internal void SelectLine()
        {
            MoveCaret(CaretMove.LineStart);
            MoveCaret(CaretMove.LineEnd, true);
        }

        internal void BeginSelectionDrag() => StartDrag();

        // Held-button drag: the caret follows the pointer, the anchor stays where the press landed.
        public override void OnDrag(PointerEvent e)
        {
            base.OnDrag(e);
            if (content == null) return;

            AutoScroll(e.point);

            if (content.CaretOffText(e.point, out NextBlockControl block, out int offset))
                content.SetCaret(block, offset, true);
        }

        // Dragging past the viewport edge scrolls, so a selection can run off-screen. The caret
        // resolves against the geometry this frame still has and catches up on the next tick.
        private void AutoScroll(Vector2D<float> point)
        {
            LayoutRect inner = arrangedRect.Shrink(arrange.padding);

            float overshoot = point.Y < inner.y ? point.Y - inner.y
                            : point.Y > inner.Bottom ? point.Y - inner.Bottom
                            : 0f;
            if (overshoot == 0f) return;

            Vector2D<float> offset = GetScrollOffset();
            SetScrollOffset(new Vector2D<float>(offset.X, offset.Y + overshoot * autoScrollRate));
        }
        #endregion

        #region ---- caret movement ----
        public void MoveCaret(CaretMove move, bool extend = false)
        {
            if (content?.caretBlock == null) return;

            if (move == CaretMove.Left) MoveLeft(extend);
            else if (move == CaretMove.Right) MoveRight(extend);
            else MoveToPoint(move, extend);

            RequestScrollToCaret();
        }

        private void MoveLeft(bool extend)
        {
            if (content.caretOffset > 0)
            {
                content.SetCaret(content.caretBlock, content.caretOffset - 1, extend);
                return;
            }

            NextBlockControl previous = content.AdjacentBlock(content.caretBlock, -1);
            if (previous != null) content.SetCaret(previous, previous.Length, extend);
        }

        private void MoveRight(bool extend)
        {
            if (content.caretOffset < content.caretBlock.Length)
            {
                content.SetCaret(content.caretBlock, content.caretOffset + 1, extend);
                return;
            }

            NextBlockControl next = content.AdjacentBlock(content.caretBlock, 1);
            if (next != null) content.SetCaret(next, 0, extend);
        }

        // Up/down, line start/end and page moves are all "resolve this point", because a visual line
        // is a line of the block rather than of the caret.
        private void MoveToPoint(CaretMove move, bool extend)
        {
            if (!content.CaretPoint(out float x, out float y, out float height)) return;

            LayoutRect inner = content.arrangedRect.Shrink(content.arrange.padding);
            float page = arrangedRect.Shrink(arrange.padding).height;

            float targetX = move switch
            {
                CaretMove.LineStart => inner.x,
                CaretMove.LineEnd => inner.x + inner.width,
                _ => x
            };

            float targetY = move switch
            {
                CaretMove.Up => y,
                CaretMove.Down => y + height,
                CaretMove.PageUp => y - page,
                CaretMove.PageDown => y + page,
                _ => y + height * 0.5f
            };

            // Up and down have to exclude the line the caret is already on. Probing just outside it
            // is not enough: blocks are spaced apart, so the current line stays the nearest band to a
            // point one pixel off it and the caret never crosses a block boundary.
            float bandMin = move == CaretMove.Down ? y + height : float.NegativeInfinity;
            float bandMax = move == CaretMove.Up ? y : float.PositiveInfinity;

            if (content.CaretAtPoint(targetX, targetY, out NextBlockControl block, out int offset, bandMin, bandMax))
                content.SetCaret(block, offset, extend);
        }
        #endregion

        #region ---- editing ----
        public void Backspace() => DeleteOver(CaretMove.Left);

        public void Delete() => DeleteOver(CaretMove.Right);

        // Without a selection the caret makes one a character wide, so deleting past a block boundary
        // follows the same rules the arrow keys already resolve.
        private void DeleteOver(CaretMove move)
        {
            if (content?.caretBlock == null) return;

            using (BeginStep(move == CaretMove.Left ? "Backspace" : "Delete"))
            {
                if (!content.HasSelection) MoveCaret(move, true);
                if (content.DeleteSelection()) MarkDirty();
            }

            RequestScrollToCaret();
        }

        public void SplitBlock()
        {
            if (content == null) return;

            using (BeginStep("New paragraph"))
                content.SplitBlock();

            MarkDirty();
            RequestScrollToCaret();
        }

        // One character, recorded against the block it lands in.
        public void TypeChar(char c)
        {
            content?.TypeChar(c);
            RequestScrollToCaret();
        }
        #endregion

        // Edited, and never given a name. The naming prompt is a window, so it waits for 6c2.
        public bool needsNaming => session != null && session.isDirty && session.document.name == null;

        public void FocusCaret()
        {
            if (content == null) return;

            UIEngine.SetActiveControl(this);
            content.FocusCaret();
        }

        // The scroll every editing path asks for happens here, because this is the first moment the
        // caret's block has the layout it now lives in.
        //
        // Both branches after ScrollIntoView are load-bearing, and this method must never exit with
        // the arrange flag set. InvalidateArrange bails the moment it meets a control already dirty
        // — and from in here every ancestor is still mid-Arrange — so a flag left set makes the
        // editor permanently dirty and every later invalidate from it is silently dropped.
        public override void Arrange(LayoutRect finalRect)
        {
            base.Arrange(finalRect);

            if (!scrollToCaretPending || content == null) return;
            scrollToCaretPending = false;

            if (!content.CaretPoint(out float x, out float y, out float height)) return;

            Vector2D<float> before = GetScrollOffset();
            ScrollIntoView(new LayoutRect(x, y, NextCaretControl.Width, height));

            if (GetScrollOffset() != before) base.Arrange(finalRect);
            else SetFlag(ArrangeFlags.ArrangeDirty, false);
        }

        // Deferred to the next Arrange, never done here: a block created this tick has no arranged
        // rect yet, and ScrollIntoView reads a zero rect as "above the viewport" and jumps to the
        // top of the note.
        internal void RequestScrollToCaret()
        {
            scrollToCaretPending = true;
            InvalidateArrange();
        }

        // The gutter and the editor's own padding, which the content does not cover.
        public override bool OnPointerPress(PointerEvent e)
        {
            if (content == null) return false;

            if (content.CaretOffText(e.point, out NextBlockControl block, out int offset))
                content.SetCaret(block, offset, NextDocumentControl.Extending);

            return true;
        }

        #region ---- focus ----
        public void OnContextAdded(string context)
        {
            if (context == "NextActiveControl") content?.FocusCaret();
        }

        // Raised only when the context went somewhere outside the editor.
        public void OnContextRemoved(string context)
        {
            if (context != "NextActiveControl") return;

            for (Control control = UIEngine.activeControl; control != null; control = control.parent as Control)
                if (ReferenceEquals(control, this)) return;

            content?.Blur();
        }
        #endregion
    }
}
