namespace ArctisAurora.Core.UISystem.Controls.Text.Document
{
    // One open note: the document the editor is showing and the file it came from. There is no
    // second copy to edit — the control tree the editor displays is the model — so a revert is a
    // reload rather than a discarded clone.
    public class DocumentEditSession
    {
        public RichTextDocument document { get; }
        public string path { get; }

        // Edited since the last write. Read by the close paths, which must not prompt over a note
        // that was only ever looked at.
        public bool isDirty { get; private set; }

        public DocumentEditSession(RichTextDocument document, string path)
        {
            this.document = document;
            this.path = path;
        }

        public void MarkDirty() => isDirty = true;

        public void Save()
        {
            document.Save(path);
            isDirty = false;
        }
    }
}
