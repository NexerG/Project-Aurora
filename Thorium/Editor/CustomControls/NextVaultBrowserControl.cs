using ArctisAurora.Core.Filing;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.UI;
using System.Xml.Linq;

namespace Thorium.Editor.CustomControls
{
    // Lists the vault as an openable tree. Renaming lands on disk; opening a note waits on the
    // document editor reaching the new stack.
    [A_XSDType("NextVaultBrowser", "UI")]
    public class NextVaultBrowserControl : NextFileTreeControl
    {
        public NextVaultBrowserControl()
        {
            Rebuild();
        }

        protected override string RootPath =>
            KnownVaults.Resolve(SettingsRegistry.Get<ThoriumSettings>().vault.path);

        protected override bool Accepts(FileObject file) =>
            file.path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase);

        protected override string DisplayName(FileObject file) =>
            file.type == FileObject.FileType.Directory
                ? file.name
                : Path.GetFileNameWithoutExtension(file.path);

        protected override void Activate(FileObject file) { }

        protected override void Rename(FileObject file, string newName) => RenameNote(file.path, newName);

        // The name lives in the file and in the Name written inside it.
        private void RenameNote(string path, string newName)
        {
            string name = newName?.Trim();
            if (string.IsNullOrEmpty(name) || name == Path.GetFileNameWithoutExtension(path)) return;

            string target = FreePath(Path.GetDirectoryName(path)!, name);
            File.Move(path, target);
            WriteName(target, name);

            Rebuild();
        }

        // "Name", then "Name 2", "Name 3" — a name already taken is never written over.
        private static string FreePath(string folder, string baseName)
        {
            string path = Path.Combine(folder, baseName + ".xml");
            for (int i = 2; File.Exists(path); i++)
                path = Path.Combine(folder, $"{baseName} {i}.xml");

            return path;
        }

        private static void WriteName(string path, string name)
        {
            XDocument xml = XDocument.Load(path);
            xml.Root!.SetAttributeValue("Name", name);
            xml.Save(path);
        }
    }
}
