using ArctisAurora.Core.Filing;

namespace ArctisAurora.Core.UISystem.Controls.Containers
{
    // A browser whose folders open in place: a folder row toggles, and only an open folder puts its
    // contents on the list.
    public abstract class FileTreeControl : FileBrowserControl
    {
        // expander captions
        private const string closedPrefix = ">";
        private const string openPrefix = "v";

        // full paths of the open folders, so a rebuild leaves the tree as it was
        private readonly HashSet<string> expanded = new HashSet<string>();

        protected override void PopulateRows() => AddEntries(root, 0);

        private void AddEntries(FileObject folder, int depth)
        {
            foreach (FileObject child in folder.Children)
            {
                if (child.type == FileObject.FileType.Directory)
                {
                    bool isOpen = expanded.Contains(child.path);
                    AddRow(child, depth, isOpen ? openPrefix : closedPrefix, () => Toggle(child.path));
                    if (isOpen) AddEntries(child, depth + 1);
                    continue;
                }

                if (!Accepts(child)) continue;
                AddRow(child, depth, string.Empty, () => Activate(child));
            }
        }

        private void Toggle(string path)
        {
            if (!expanded.Remove(path)) expanded.Add(path);
            Rebuild();
        }
    }
}
