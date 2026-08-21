using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.Filing.Serialization;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Registry.Assets;
using ArctisAurora.Core.UISystem.Controls.Containers;
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

        private const float autoScrollRate = 0.25f;

        private DocumentControl? content;

        public DocumentEditorControl()
        {
            scrollDirection = ScrollDirection.Vertical;
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

        public void CollapseSelection() => content?.CollapseSelection();

        public bool DeleteSelection()
        {
            if (content == null || !content.DeleteSelection()) return false;
            MarkDirty();
            return true;
        }

        public TextControl CaretRun => content?.caretRun;

        public void LoadDocument(RichTextDocument document)
        {
            activeDocument = document;

            foreach (Entity child in children.ToArray())
                child.Destroy();
            children.Clear();

            content = new DocumentControl { blockSpacing = document.layout.blockSpacing, document = document };
            AddChild(content);

            foreach (Block block in document.blocks)
            {
                block.ApplyLayout(document.layout);
                content.AddChild(block);
            }
        }

        private Vector2D<float> PointerInWindow()
        {
            RenderWindow window = RenderWindow.Of(this);
            return window.ui.ToDesignSpace(window.mousePos);
        }

        // Places the caret and marks the hit run editable; shift keeps the anchor and extends.
        public override void ResolveOnClick(Vector2D<float> oldPos, Vector2D<float> delta)
        {
            TextControl run = RunUnder(UICollisionHandling.hovering);
            if (content != null)
            {
                // the live pointer, not oldPos, which lags the click by a frame
                Vector2D<float> mouse = PointerInWindow();

                if (run != null)
                {
                    LayoutRect inner = run.arrangedRect.Shrink(run.padding);

                    content.SetCaret(run, run.OffsetAt(mouse.X - inner.x, mouse.Y - inner.y), Extending);
                    StartDrag();
                }
                // a click past the text hits no run, so the geometry answers instead of the hit-test
                else if (content.CaretOffText(mouse.X, mouse.Y, out TextControl off, out int offset))
                {
                    content.SetCaret(off, offset, Extending);
                    StartDrag();
                }
            }

            base.ResolveOnClick(oldPos, delta);
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

            ScrollToCaret();
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

        private void ScrollToCaret()
        {
            if (content.CaretPoint(out float x, out float y, out float height))
                ScrollIntoView(new LayoutRect(x, y, CaretControl.Width, height));
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

            if (!content.HasSelection) MoveCaret(move, true);
            if (content.DeleteSelection()) MarkDirty();
            ScrollToCaret();
        }

        // No ScrollToCaret: the new block has no arranged rect until the next layout pass, and a
        // zero rect reads as "above the viewport" and would scroll the note to the top.
        public void SplitBlock()
        {
            if (content == null) return;
            content.SplitBlock();
            MarkDirty();
        }
        #endregion

        // Nearest TextControl at or above the hit control.
        private static TextControl RunUnder(VulkanControl hit)
        {
            for (VulkanControl control = hit; control != null; control = control.parent as VulkanControl)
                if (control is TextControl run) return run;

            return null;
        }
    }
}
