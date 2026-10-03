namespace ArctisAurora.Core.UI
{
    // An editor a tab holds, as the tab, session and rename code see it.
    public interface IFileEditor
    {
        string? path { get; }

        bool isDirty { get; }

        void Save();

        // Follows a rename on disk.
        void Repath(string newPath, string name);

        SessionTab ViewState();

        void RestoreView(SessionTab view);
    }
}
