using ArctisAurora.Core.Editing;

namespace ArctisAurora.Core.UISystem.Controls.Text.Document.Edits
{
    // Text written into or cut out of a single run, changing no structure — typing, and any delete
    // that stayed inside one run.
    public sealed class RunTextEdit : IEditRecord
    {
        private readonly DocumentControl document;
        private readonly DocumentAddress at;
        private readonly string text;
        private readonly bool inserted;

        public RunTextEdit(DocumentControl document, DocumentAddress at, string text, bool inserted)
        {
            this.document = document;
            this.at = at;
            this.text = text;
            this.inserted = inserted;
        }

        public void Undo()
        {
            if (inserted) document.RemoveRunText(at, text.Length);
            else document.InsertRunText(at, text);
        }

        public void Redo()
        {
            if (inserted) document.InsertRunText(at, text);
            else document.RemoveRunText(at, text.Length);
        }
    }
}
