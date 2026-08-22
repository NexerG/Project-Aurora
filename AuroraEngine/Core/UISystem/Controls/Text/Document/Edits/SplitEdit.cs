using ArctisAurora.Core.Editing;

namespace ArctisAurora.Core.UISystem.Controls.Text.Document.Edits
{
    // A block split in two. carriedRunDropped is recorded from the branch the split took: when the
    // clone survived, undo has to join it back onto the run it came from, and when it was dropped
    // the next block's first run is a moved run that must be left whole.
    public sealed class SplitEdit : IEditRecord
    {
        private readonly DocumentControl document;
        private readonly DocumentAddress at;
        private readonly bool carriedRunDropped;

        public SplitEdit(DocumentControl document, DocumentAddress at, bool carriedRunDropped)
        {
            this.document = document;
            this.at = at;
            this.carriedRunDropped = carriedRunDropped;
        }

        public void Undo() => document.JoinBlockWithNext(at, !carriedRunDropped);

        public void Redo() => document.SplitBlockAt(at);
    }
}
