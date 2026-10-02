using System.Numerics;

namespace ArctisAurora.Core.UI
{
    // One formula's source in a popup under it: previewed in the note as it is typed, one undo record on commit.
    internal sealed class FormulaPopup
    {
        // popup sizing
        private const float width = 320f;
        private const float rowHeight = 26f;
        private const float gap = 4f;

        private readonly DocumentEditorControl editor;
        private readonly DocumentControl document;
        private readonly DocumentAddress at;
        private readonly string before;
        private readonly bool placed;
        private readonly TextBoxControl box = new TextBoxControl();
        private bool done;

        private FormulaPopup(DocumentEditorControl editor, DocumentControl document, DocumentAddress at, bool placed)
        {
            this.editor = editor;
            this.document = document;
            this.at = at;
            this.placed = placed;
            before = document.Resolve(at, out BlockControl block, out int index)
                ? DocumentControl.StoredMath(block, index).mathSource ?? string.Empty
                : string.Empty;
        }

        public static void Open(DocumentEditorControl editor, DocumentControl document, DocumentAddress at, bool placed) =>
            new FormulaPopup(editor, document, at, placed).Show();

        private void Show()
        {
            box.text = before;
            box.preferredHeight = rowHeight;
            box.fontSize = 14;
            box.role = PaletteRole.Field;
            box.onEdited = source => document.SetMath(at, source);
            box.onCommit = source => Finish(source, true);
            box.onBlur = () => Finish(box.text, false);
            box.onCancel = Cancel;

            LayoutRect anchor = document.MathAnchor(at);
            ContextMenus.Open(new List<ContextMenuEntry> { new ContextMenuContent(box) }, editor,
                new Vector2(anchor.x, anchor.Bottom + gap), width);
            UIEngine.SetActiveControl(box);
            box.Focus();
        }

        // Records what the preview already shows; an empty new formula goes away unrecorded.
        private void Finish(string source, bool refocus)
        {
            if (done) return;
            done = true;
            ContextMenus.Close();

            if (placed && source.Length == 0)
                document.RemovePlaced(at);
            else if (placed)
            {
                using (editor.BeginStep("Insert formula"))
                    document.RecordPlaced(at);
                editor.MarkDirty();
            }
            else if (source != before)
            {
                using (editor.BeginStep("Edit formula"))
                    document.RecordMath(at, before, source);
                editor.MarkDirty();
            }

            if (refocus) editor.FocusCaret();
        }

        private void Cancel()
        {
            if (done) return;
            done = true;
            ContextMenus.Close();

            if (placed) document.RemovePlaced(at);
            else document.SetMath(at, before);
            editor.FocusCaret();
        }
    }
}
