using ArctisAurora.Core.Filing.Serialization;
using ArctisAurora.Core.Registry;
using System.Xml.Linq;

namespace ArctisAurora.Core.Filing
{
    [A_XSDType("FileKind", "UI")]
    public enum FileKind
    {
        Note,
        Markdown,
        PlainText,
        Tex,
        Xml,
        Sheet,
        Planner,
        Graph
    }

    [A_XSDType("FileType", "UI")]
    public record FileType
    {
        [A_XSDElementProperty("Suffix", "UI", "End of the file name this type matches, such as .sheet.axml.")]
        public string suffix = "";

        [A_XSDElementProperty("Kind", "UI", "What the editor opens a file of this type as.")]
        public FileKind kind;

        [A_XSDElementProperty("Description", "UI", "Name the operating system shows for this type.")]
        public string description = "";

        [A_XSDElementProperty("Register", "UI", "Whether the application registers itself with the operating system for this type.")]
        public bool register;

        [A_XSDElementProperty("Mime", "UI", "MIME type the operating system knows this type by, such as text/markdown.")]
        public string mime = "";
    }

    [A_XSDType("FileTypes", "UI", typeof(FileType), Description = "Root container for file types")]
    public class FileTypeMap { }

    // The file types the editor opens, matched by the longest suffix.
    public static class FileTypes
    {
        public static readonly List<FileType> all = new List<FileType>();

        [A_XSDActionDependency("FileTypes.Load", "Bootstrap")]
        public static bool Load()
        {
            all.Clear();
            foreach (XElement element in DataFiles.LoadXml(Paths.Doc("FileTypes.filetypes.axml")).Elements())
                all.Add(new FileType
                {
                    suffix = (string?)element.Attribute("Suffix") ?? "",
                    kind = Enum.Parse<FileKind>((string?)element.Attribute("Kind") ?? ""),
                    description = (string?)element.Attribute("Description") ?? "",
                    register = (bool?)element.Attribute("Register") ?? false,
                    mime = (string?)element.Attribute("Mime") ?? ""
                });
            all.Sort((a, b) => b.suffix.Length.CompareTo(a.suffix.Length));
            return true;
        }

        public static FileType? Of(string? path) =>
            string.IsNullOrEmpty(path) ? null : all.Find(type => path.EndsWith(type.suffix, StringComparison.OrdinalIgnoreCase));

        public static FileKind? KindOf(string? path) => Of(path)?.kind;
    }
}
