using ArctisAurora.Core.Filing.Serialization;
using ArctisAurora.Core.Registry;
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
