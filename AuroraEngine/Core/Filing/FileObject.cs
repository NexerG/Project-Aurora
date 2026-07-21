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

        public List<FileObject> children;

        [SetsRequiredMembers]
        public FileObject(string path)
        {
            //this.type = type;
            this.path = path;

            FileAttributes attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.Directory) == FileAttributes.Directory)
            {
                this.type = FileType.Directory;
                children = new List<FileObject>();
                TreeBranch(path, this);
            }
            else
            {
                this.type = FileType.File;
            }
        }

        private void TreeBranch(string path, FileObject fileObject)
        {
            string[] directories = Directory.GetDirectories(path);
            foreach (string directory in directories)
            {
                FileObject child = new FileObject(directory);
                fileObject.children.Add(child);
                child.TreeBranch(directory, child);
            }

            string[] files = Directory.GetFiles(path);
            foreach (string file in files)
            {
                FileObject child = new FileObject(file);
                fileObject.children.Add(child);
            }
        }
    }
}
