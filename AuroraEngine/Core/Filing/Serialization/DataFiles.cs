using ArctisAurora.Core.Filing.Arx;
using System.Xml.Linq;

namespace ArctisAurora.Core.Filing.Serialization
{
    // Reads a data path whether it names a file on disk or an entry inside a mounted archive.
    public static class DataFiles
    {
        public static bool Exists(string path) =>
            Locate(path, out ArxArchive archive, out string entry) ? archive.Contains(entry) : File.Exists(path);

        public static Stream Open(string path) =>
            Locate(path, out ArxArchive archive, out string entry) ? new MemoryStream(archive.ReadBytes(entry), false) : File.OpenRead(path);

        public static XElement LoadXml(string path) =>
            Locate(path, out ArxArchive archive, out string entry) ? archive.ReadXml(entry) : XElement.Load(path);

        // A shader's SPIR-V, from the archive when one is mounted, else relative to the exe's folder.
        public static byte[] ReadShader(string fileName)
        {
            string logical = fileName.Replace('\\', '/');
            while (logical.StartsWith("../")) logical = logical[3..];
            if (VirtualFileSystem.TryResolveFile(logical, out string resolved) && Locate(resolved, out ArxArchive archive, out string entry))
                return archive.ReadBytes(entry);
            return File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, fileName));
        }

        private static bool Locate(string path, out ArxArchive archive, out string entry)
        {
            if (Path.IsPathRooted(path)) return ArxMount.TryLocate(Path.GetFullPath(path), out archive, out entry);
            archive = null!;
            entry = string.Empty;
            return false;
        }
    }
}
