using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.Editing;
using ArctisAurora.Core.Filing.Serialization;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Registry.Assets;
using ArctisAurora.Core.UISystem.Controls.Containers;
using ArctisAurora.Core.UISystem.Controls.Text.Document.Edits;
using ArctisAurora.EngineWork;
using ArctisAurora.EngineWork.Registry;
using ArctisAurora.EngineWork.Rendering;
using Silk.NET.Maths;

namespace ArctisAurora.Core.UISystem.Controls.Text.Document
{
    // The view over a RichTextDocument: a scrolling viewport over the document's own block controls.
    [A_XSDType("DocumentEditor", "UI")]
    public class DocumentEditorControl : ScrollableControl
    {
        public RichTextDocument activeDocument { get; private set; } = null!;
        public DocumentEditSession? session { get; private set; }

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

        private DocumentControl? content;

        // honoured at the end of Arrange, once every run has this frame's rect
        private bool scrollToCaretPending;

        public DocumentEditorControl()
        {
            scrollDirection = ScrollDirection.Vertical;
            overscroll = 0.5f;
            maskAsset = AssetRegistries.GetAsset<TextureAsset>("invisible");
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
            RichTextDocument document = RichTextDocument.ParseXML(path);

            session = new DocumentEditSession(document, path);
            LoadDocument(document);
        }

        public void Save() => session?.Save();

        // An edited note that has never been named. Nothing derives a name from the file, so this
        // stays true until someone answers the prompt.
        public bool needsNaming => session != null && session.isDirty && session.document.name == null;

        // Writes the note, asking for a name first when it has never been named. onSaved runs once it
        // is on disk, onDiscarded if the note was left unwritten on purpose, onCancelled if the
        // answer was abandoned. Passing no onDiscarded leaves the prompt without that button.
        public void SaveNamed(Action onSaved = null, Action onDiscarded = null, Action onCancelled = null)
        {
            if (session == null) { onSaved?.Invoke(); return; }

            if (!needsNaming)
            {
                session.Save();
                onSaved?.Invoke();
                return;
            }

            NoteNameWindow.Ask(RenderWindow.Of(this), Path.GetFileNameWithoutExtension(session.path),
                name =>
                {
                    session.document.name = name;
                    session.Save();
                    onSaved?.Invoke();
                },
                onDiscarded,
                onCancelled);
        }

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

        public void CollapseSelection() => content?.CollapseSelection();

        public void SelectAll() => content?.SelectAll();

        public bool DeleteSelection()
        {
            if (content == null || !content.DeleteSelection()) return false;
            MarkDirty();
            return true;
        }

        public TextControl CaretRun => content?.caretRun;

        // What a toggle reads its current state from, and what a toolbar reflects.
        public CaretStyle? StyleSource => content?.StyleSource;

        public TextStyleType CaretBlockStyling => content?.CaretBlockStyling ?? TextStyleType.Text;

        public void ApplyStyle(StyleDelta delta)
        {
            if (content == null) return;

            using (BeginStep("Formatting"))
                if (content.ApplyStyle(delta)) MarkDirty();
        }

        // Nothing is written, so there is no step and no dirty note until the next character.
        public void ArmStyle(StyleDelta delta) => content?.ArmStyle(delta);

        // For a control that must take the active context before it can be used: it captures the
        // range on the way in and hands it back here, rather than asking what is selected once the
        // note no longer holds the caret.
        public bool SelectedRange(out DocumentAddress from, out DocumentAddress to)
        {
            from = to = default;
            return content != null && content.SelectedRange(out from, out to);
        }

        public void ApplyStyleTo(DocumentAddress from, DocumentAddress to, StyleDelta delta)
        {
            if (content == null) return;

            using (BeginStep("Formatting"))
                if (content.ApplyStyleTo(from, to, delta)) MarkDirty();
        }

        public void FocusCaret() => content?.FocusCaret();

        public void SetBlockStyling(TextStyleType type)
        {
            if (content == null) return;

            using (BeginStep("Paragraph style"))
                if (content.SetBlockStyling(type)) MarkDirty();
        }

        public void LoadDocument(RichTextDocument document)
        {
            activeDocument = document;

            foreach (Entity child in children.ToArray())
                child.Destroy();
            children.Clear();

            content = new DocumentControl
            {
                blockSpacing = document.layout.blockSpacing,
                document = document,
                caretColorHex = caretColorHex,
                selectionColorHex = selectionColorHex
            };
            content.undo = session?.undo;
            AddChild(content);

            foreach (Block block in document.blocks)
            {
                block.ApplyLayout(document.layout);
                content.AddChild(block);
            }
        }

        // The scroll every editing path asks for happens here, because this is the first moment the
        // caret's run has a rect for the layout it now lives in.
        //
        // Both branches after ScrollIntoView are load-bearing, and this method must never exit with
        // isArrangeDirty set. InvalidateArrange walks up until it meets a control already marked
        // dirty and registers nothing when it does — and from in here every ancestor is still
        // mid-Arrange — so a flag left set makes the editor permanently dirty, and every later
        // invalidate from it or any run below it registers nothing and is silently dropped.
        //
        // Re-arranging costs a pass over the document, so it is paid only when the offset moved;
        // ScrollIntoView invalidates whether or not it scrolled, hence the reset on the other side.
        public override void Arrange(LayoutRect finalRect)
        {
            base.Arrange(finalRect);

            if (!scrollToCaretPending || content == null) return;
            scrollToCaretPending = false;

            if (!content.CaretPoint(out float x, out float y, out float height)) return;

            Vector2D<float> before = GetScrollOffset();
            ScrollIntoView(new LayoutRect(x, y, CaretControl.Width, height));

            if (GetScrollOffset() != before) base.Arrange(finalRect);
            else isArrangeDirty = false;
        }

        private Vector2D<float> PointerInWindow()
        {
            RenderWindow window = RenderWindow.Of(this);
            return window.ui.ToDesignSpace(window.mousePos);
        }

        // Places the caret and marks the run editable; shift keeps the anchor and extends. The
        // geometry answers which slot was clicked, never the hit-test.
        public override void ResolveOnClick(Vector2D<float> oldPos, Vector2D<float> delta)
        {
            if (content != null)
            {
                // the live pointer, not oldPos, which lags the click by a frame
                Vector2D<float> mouse = PointerInWindow();

                if (content.CaretOffText(mouse.X, mouse.Y, out TextControl run, out int offset))
                {
                    content.SetCaret(run, offset, Extending);
                    StartDrag();
                }
            }

            base.ResolveOnClick(oldPos, delta);
        }

        // Two clicks take the word, three the visual line.
        public override void ResolveOnMultiClick(int count)
        {
            if (content != null)
            {
                if (count == 2) content.SelectWord();
                else if (count == 3)
                {
                    MoveCaret(CaretMove.LineStart);
                    MoveCaret(CaretMove.LineEnd, true);
                }
            }

            base.ResolveOnMultiClick(count);
        }

        // Held-button drag: the focus follows the mouse, the anchor stays where the press landed.
        public override void ResolveDrag(Vector2D<float> lastPos, Vector2D<float> delta)
        {
            if (content != null)
            {
                // the live pointer, not lastPos, which lags by a frame
                Vector2D<float> mouse = PointerInWindow();
                AutoScroll(mouse);

                if (content.CaretOffText(mouse.X, mouse.Y, out TextControl run, out int offset))
                    content.SetCaret(run, offset, true);
            }

            base.ResolveDrag(lastPos, delta);
        }

        // Dragging past the viewport edge scrolls, so a selection can run off-screen. The caret
        // resolves against the geometry this frame still has and catches up on the next tick.
        private void AutoScroll(Vector2D<float> mouse)
        {
            LayoutRect inner = arrangedRect.Shrink(padding);

            float overshoot = mouse.Y < inner.y ? mouse.Y - inner.y
                            : mouse.Y > inner.Bottom ? mouse.Y - inner.Bottom
                            : 0f;
            if (overshoot == 0f) return;

            Vector2D<float> offset = GetScrollOffset();
            SetScrollOffset(new Vector2D<float>(offset.X, offset.Y + overshoot * autoScrollRate));
        }

        private static bool Extending => InputHandler.instance.IsModifierDown(InputModifier.Extend);

        #region ---- caret movement ----
        public void MoveCaret(CaretMove move, bool extend = false)
        {
            if (content?.caretRun == null) return;

            if (move == CaretMove.Left) MoveLeft(extend);
            else if (move == CaretMove.Right) MoveRight(extend);
            else MoveToPoint(move, extend);

            RequestScrollToCaret();
        }

        // A run boundary inside a block is one caret slot, not two: the previous run's end and the
        // next run's start resolve to the same point, so a step across it lands past the duplicate.
        // Rightwards that rule is SetCaret's normalization; leftwards it has to be done here,
        // because normalizing only ever moves a slot forwards.
        private void MoveLeft(bool extend)
        {
            TextControl run = content.caretRun;

            if (run.cursorPosition > 0)
            {
                content.SetCaret(run, run.cursorPosition - 1, extend);
                return;
            }

            TextControl previous = content.AdjacentRun(run, -1);
            if (previous == null) return;

            int end = (previous.text ?? string.Empty).Length;
            bool sharesSlot = DocumentControl.BlockOf(previous) == DocumentControl.BlockOf(run);
            content.SetCaret(previous, sharesSlot ? end - 1 : end, extend);
        }

        private void MoveRight(bool extend)
        {
            TextControl run = content.caretRun;
            int length = (run.text ?? string.Empty).Length;

            if (run.cursorPosition >= length)
            {
                TextControl following = content.AdjacentRun(run, 1);
                if (following != null) content.SetCaret(following, 0, extend);
                return;
            }

            content.SetCaret(run, run.cursorPosition + 1, extend);
        }

        // Up/down, line start/end and page moves are all "resolve this point", because a visual line
        // spans runs — the run holding the line's start is not the one the caret is in.
        private void MoveToPoint(CaretMove move, bool extend)
        {
            if (!content.CaretPoint(out float x, out float y, out float height)) return;

            LayoutRect inner = content.arrangedRect.Shrink(content.padding);
            float page = arrangedRect.Shrink(padding).height;

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
            // is not enough: blocks are spaced apart, so the current line stays the nearest band to
            // a point one pixel off it and the caret never crosses a block boundary.
            float bandMin = move == CaretMove.Down ? y + height : float.NegativeInfinity;
            float bandMax = move == CaretMove.Up ? y : float.PositiveInfinity;

            if (content.CaretAtPoint(targetX, targetY, out TextControl run, out int offset, bandMin, bandMax))
                content.SetCaret(run, offset, extend);
        }

        // Deferred to the next Arrange, never done here: a block created this tick has no arranged
        // rect yet, and ScrollIntoView reads a zero rect as "above the viewport" and jumps to the
        // top of the note. Typing has the weaker form of it — a character that wraps moves the
        // caret onto a visual line the current layout does not hold.
        private void RequestScrollToCaret()
        {
            scrollToCaretPending = true;
            InvalidateArrange();
        }
        #endregion

        #region ---- editing ----
        public void Backspace() => DeleteOver(CaretMove.Left);

        public void Delete() => DeleteOver(CaretMove.Right);

        // Without a selection the caret makes one a character wide, so deleting past a run or block
        // boundary follows the same rules the arrow keys already resolve.
        private void DeleteOver(CaretMove move)
        {
            if (content?.caretRun == null) return;

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

        // One character, recorded against the run it lands in.
        public void TypeChar(TextControl run, char c)
        {
            content?.TypeChar(run, c);
            RequestScrollToCaret();
        }
        #endregion
    }
}
