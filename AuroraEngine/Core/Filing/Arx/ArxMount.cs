using ArctisAurora.Core.Filing.Serialization;
using System.IO.Enumeration;

namespace ArctisAurora.Core.Filing.Arx
{
    // An archive as a data mount; its entries resolve to paths under the .arx file as if it were a folder.
    public sealed class ArxMount : IDataMount
    {
        private static readonly List<ArxMount> _mounted = new List<ArxMount>();

        public readonly ArxArchive archive;
        private readonly string _prefix;

        public ArxMount(ArxArchive archive)
        {
            this.archive = archive;
            _prefix = archive.file + Path.DirectorySeparatorChar;
            _mounted.Add(this);
        }

        public bool FileExists(string relativePath) => archive.Contains(relativePath);

        public bool DirExists(string relativeDir)
        {
            string dir = ArxWriter.Normalize(relativeDir).TrimEnd('/') + "/";
            return archive.Paths.Any(p => p.StartsWith(dir, StringComparison.OrdinalIgnoreCase));
        }

        public Stream Open(string relativePath) => new MemoryStream(archive.ReadBytes(relativePath), false);

        public string GetFullPath(string relativePath) => _prefix + ArxWriter.Normalize(relativePath).Replace('/', Path.DirectorySeparatorChar);

        public IEnumerable<string> Enumerate(string relativeDir, string pattern)
        {
            string dir = ArxWriter.Normalize(relativeDir).TrimEnd('/');
            foreach (string path in archive.Paths)
            {
                int slash = path.LastIndexOf('/');
                string parent = slash < 0 ? string.Empty : path[..slash];
                if (string.Equals(parent, dir, StringComparison.OrdinalIgnoreCase) &&
                    FileSystemName.MatchesSimpleExpression(pattern, path[(slash + 1)..]))
                    yield return GetFullPath(path);
            }
        }

        // The mounted archive a full path points into, and the entry it names.
        public static bool TryLocate(string fullPath, out ArxArchive archive, out string entry)
        {
            foreach (ArxMount mount in _mounted)
            {
                if (fullPath.StartsWith(mount._prefix, StringComparison.OrdinalIgnoreCase))
                {
                    archive = mount.archive;
                    entry = ArxWriter.Normalize(fullPath[mount._prefix.Length..]);
                    return true;
                }
            }
            archive = null!;
            entry = string.Empty;
            return false;
        }
    }
}
