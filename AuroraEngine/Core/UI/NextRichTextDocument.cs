using ArctisAurora.Core.Editing;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.UISystem.Controls.Text.Document;

namespace ArctisAurora.Core.UI
{
    // The note model, and the on-disk format. The blocks are the controls the document view shows —
    // there is no second copy to edit. Layout and styles are the outgoing stack's types, shared
    // rather than copied; landing 6d decides where they live.
    [A_XSDType("NextDocument", "UI")]
    public class NextRichTextDocument
    {
        public List<NextBlockControl> blocks = new List<NextBlockControl>();

        // Absent until the note is named. Nothing derives it from the file, so a note that has never
        // been named reads as unnamed however many times it is saved.
        [A_XSDElementProperty("Name", "UI", "Display name of the note. Absent means the file name stands in.")]
        public string? name;

        [A_XSDElementProperty("DocumentLayout", "UI", "Layout parameters for this note.")]
        public DocumentLayout layout = new DocumentLayout();

        public static NextRichTextDocument ParseXML(string path) => NextDocumentXml.Load(path);

        public void Save(string path) => NextDocumentXml.Save(this, path);
    }

    // One open note: the document the editor is showing and the file it came from.
    public class NextDocumentEditSession
    {
        public NextRichTextDocument document { get; }
        public string path { get; private set; }

        // History is per open note, so undo in one tab cannot reach the note in another.
        public UndoStack undo { get; } = new UndoStack();

        // Edited since the last write. Read by the close paths, which must not prompt over a note
        // that was only ever looked at.
        public bool isDirty { get; private set; }

        public NextDocumentEditSession(NextRichTextDocument document, string path)
        {
            this.document = document;
            this.path = path;
        }

        public void MarkDirty() => isDirty = true;

        // Follows the file after it is renamed on disk, so the next save writes where the note is now.
        public void Repath(string newPath) => path = newPath;

        public void Save()
        {
            document.Save(path);
            isDirty = false;
        }
    }
}
