using ArctisAurora.Core.Editing;

namespace ArctisAurora.Core.UISystem.Controls.Text.Document.Edits
{
    // A delete that crossed a run or block boundary. Redo replays the forward primitive rather than
    // inverting the inverse, so only one direction is hand-written.
    public sealed class DeleteRangeEdit : IEditRecord
    {
        private readonly DocumentControl document;
        private readonly DocumentAddress from;
        private readonly DocumentAddress to;
        private readonly DocumentFragment fragment;

        public DeleteRangeEdit(DocumentControl document, DocumentAddress from, DocumentAddress to,
            DocumentFragment fragment)
        {
            this.document = document;
            this.from = from;
            this.to = to;
            this.fragment = fragment;
        }

        public void Undo() => document.InsertFragment(from, fragment);

        public void Redo() => document.DeleteBetween(from, to);
    }
}
