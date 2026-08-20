using System.Diagnostics.CodeAnalysis;

namespace ArctisAurora.Core.Filing
{
    public class FileObject
    {
        public enum FileType
        {
            File,
            Directory
        }

        public enum Icon
        {
            Folder,
            File,
            Image,
            Video,
            Audio,
            Document,
            Archive,
            Other
        }

        public Icon icon;
        required public FileType type;
        required public string path;

        // place in the tree
        public string name;
        public FileObject parent;

        private static readonly List<FileObject> noChildren = new List<FileObject>();
        private List<FileObject> children;

        // Listed on first access, so a folder nobody opened is never read from disk.
        public List<FileObject> Children
        {
            get
            {
                if (type == FileType.File) return noChildren;
                if (children == null) Load();
                return children;
            }
        }

        [SetsRequiredMembers]
        public FileObject(string path)
        {
            this.path = path;
            name = Path.GetFileName(path);

            FileAttributes attributes = File.GetAttributes(path);
            type = (attributes & FileAttributes.Directory) == FileAttributes.Directory
                ? FileType.Directory
                : FileType.File;
        }

        // Drops the listing so the next access re-reads the folder.
        public void Refresh() => children = null;

        private void Load()
        {
            children = new List<FileObject>();

            string[] directories = Directory.GetDirectories(path);
            Array.Sort(directories, StringComparer.OrdinalIgnoreCase);
            foreach (string directory in directories)
                children.Add(new FileObject(directory) { parent = this });

            string[] files = Directory.GetFiles(path);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            foreach (string file in files)
                children.Add(new FileObject(file) { parent = this });
        }
    }
}
