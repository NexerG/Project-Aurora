namespace ArctisAurora.Core.UISystem.Controls.Text.Document
{
    // One open note: the document the editor is showing and the file it came from. There is no
    // second copy to edit — the control tree the editor displays is the model — so a revert is a
    // reload rather than a discarded clone.
    public class DocumentEditSession
    {
        public RichTextDocument document { get; }
        public string path { get; }

        public DocumentEditSession(RichTextDocument document, string path)
        {
            this.document = document;
            this.path = path;
        }

        public void Save() => document.Save(path);
    }
}
