using ArctisAurora.Core.Filing.Serialization;
using System.Text;
using System.Xml.Linq;
using static ArctisAurora.Core.Filing.AuroraFont;

namespace ArctisAurora.Core.Filing
{
    // One installed family's faces; a face the family lacks is null.
    public sealed class InstalledFamily
    {
        public string name = string.Empty;
        public string regular = string.Empty;
        public int regularFace;
        public string? bold;
        public string? italic;
        public string? boldItalic;
    }

    // The fonts installed on this machine, grouped into families by their own name tables.
    public static class FontCatalog
    {
        private static readonly Diagnostics.LogChannel Log = Diagnostics.LogChannel.For("Assets");

        private static readonly string[] extensions = { ".ttf", ".otf", ".ttc" };

        // one face of one file as the cache keeps it; an empty family is a face that cannot be baked
        private readonly record struct Face(string path, int index, long modified, string family, bool bold, bool italic);

        private static IReadOnlyList<InstalledFamily>? families;

        // Null until a scan has finished.
        public static IReadOnlyList<InstalledFamily>? Families => Volatile.Read(ref families);

        private static string CachePath => Path.Combine(Paths.USER_DATA, "Fonts", "FontCatalog.catalog.axml");

        public static InstalledFamily? Find(string name) =>
            Families?.FirstOrDefault(f => string.Equals(f.name, name, StringComparison.OrdinalIgnoreCase));

        // Reads every installed face's names, re-reading only the files changed since the cache.
        public static IReadOnlyList<InstalledFamily> Scan()
        {
            Dictionary<(string, int), Face> cached = ReadCache();
            List<Face> faces = new List<Face>();
            foreach (string file in FontFiles())
            {
                long modified = File.GetLastWriteTimeUtc(file).Ticks;
                if (cached.TryGetValue((file, 0), out Face first) && first.modified == modified)
                {
                    for (int i = 0; cached.TryGetValue((file, i), out Face face); i++) faces.Add(face);
                    continue;
                }
                faces.AddRange(ReadFile(file, modified));
            }
            WriteCache(faces);

            List<InstalledFamily> grouped = Group(faces);
            Volatile.Write(ref families, grouped);
            Log.Info($"font catalog: {grouped.Count} families from {faces.Count} faces.");
            return grouped;
        }

        // A face's family and style, or null when it is not an outline font the importer can bake.
        public static (string family, bool bold, bool italic)? ReadFace(string path, int face)
        {
            try
            {
                using BinaryReader reader = new BinaryReader(File.OpenRead(path));
                return ReadNames(reader, ReadTables(reader, face));
            }
            catch (Exception)
            {
                return null;
            }
        }

        // A face's table directory; a .ttc's offsets are from the start of the file.
        internal static TableEntry[] ReadTables(BinaryReader reader, int face)
        {
            reader.BaseStream.Position = AssetImporter.FaceOffset(reader, face) + 4;
            int count = AssetImporter.ReadUInt16BE(reader);
            reader.BaseStream.Position += 6;

            TableEntry[] tables = new TableEntry[count];
            for (int i = 0; i < count; i++)
            {
                tables[i] = new TableEntry
                {
                    name = Encoding.ASCII.GetString(reader.ReadBytes(4)),
                    checksum = AssetImporter.ReadUInt32BE(reader),
                    offset = AssetImporter.ReadUInt32BE(reader),
                    length = AssetImporter.ReadUInt32BE(reader)
                };
            }
            return tables;
        }

        #region ---- reading ----
        private static List<Face> ReadFile(string file, long modified)
        {
            List<Face> faces = new List<Face>();
            try
            {
                using BinaryReader reader = new BinaryReader(File.OpenRead(file));
                int count = 1;
                if (Encoding.ASCII.GetString(reader.ReadBytes(4)) == "ttcf")
                {
                    reader.BaseStream.Position = 8;
                    count = (int)AssetImporter.ReadUInt32BE(reader);
                }

                for (int i = 0; i < count; i++)
                {
                    (string family, bool bold, bool italic)? names = ReadNames(reader, ReadTables(reader, i));
                    faces.Add(names is { } n
                        ? new Face(file, i, modified, n.family, n.bold, n.italic)
                        : new Face(file, i, modified, string.Empty, false, false));
                }
            }
            catch (Exception e)
            {
                Log.Debug($"font catalog: '{file}' unreadable ({e.Message}).");
            }

            if (faces.Count == 0) faces.Add(new Face(file, 0, modified, string.Empty, false, false));
            return faces;
        }

        private static (string family, bool bold, bool italic)? ReadNames(BinaryReader reader, TableEntry[] tables)
        {
            bool Has(string tag) => tables.Any(t => t.name == tag);
            bool outlines = (Has("glyf") && Has("loca")) || Has("CFF ");
            if (!outlines || !Has("head") || !Has("hhea") || !Has("hmtx") || !Has("maxp") || !Has("cmap") || !Has("name"))
                return null;

            string? family = FamilyName(reader, tables.First(t => t.name == "name"));
            if (string.IsNullOrWhiteSpace(family)) return null;

            bool bold;
            bool italic;
            TableEntry os2 = tables.FirstOrDefault(t => t.name == "OS/2");
            if (os2.name != null)
            {
                reader.BaseStream.Position = os2.offset + 62;
                ushort selection = AssetImporter.ReadUInt16BE(reader);
                bold = (selection & 0x20) != 0;
                italic = (selection & 0x01) != 0;
            }
            else
            {
                reader.BaseStream.Position = tables.First(t => t.name == "head").offset + 44;
                ushort macStyle = AssetImporter.ReadUInt16BE(reader);
                bold = (macStyle & 0x01) != 0;
                italic = (macStyle & 0x02) != 0;
            }
            return (family.Trim(), bold, italic);
        }

        // Name ID 1, preferring the Windows US English record.
        private static string? FamilyName(BinaryReader reader, TableEntry name)
        {
            reader.BaseStream.Position = name.offset + 2;
            int count = AssetImporter.ReadUInt16BE(reader);
            uint strings = name.offset + AssetImporter.ReadUInt16BE(reader);

            int bestScore = 0;
            (ushort platform, ushort length, ushort offset) best = default;
            for (int i = 0; i < count; i++)
            {
                ushort platform = AssetImporter.ReadUInt16BE(reader);
                ushort encoding = AssetImporter.ReadUInt16BE(reader);
                ushort language = AssetImporter.ReadUInt16BE(reader);
                ushort id = AssetImporter.ReadUInt16BE(reader);
                ushort length = AssetImporter.ReadUInt16BE(reader);
                ushort offset = AssetImporter.ReadUInt16BE(reader);
                if (id != 1) continue;

                int score = platform == 3 && language == 0x409 ? 3
                          : platform == 3 || platform == 0 ? 2
                          : platform == 1 && encoding == 0 && language == 0 ? 1
                          : 0;
                if (score <= bestScore) continue;
                bestScore = score;
                best = (platform, length, offset);
            }
            if (bestScore == 0) return null;

            reader.BaseStream.Position = strings + best.offset;
            byte[] bytes = reader.ReadBytes(best.length);
            return best.platform == 1 ? Encoding.Latin1.GetString(bytes) : Encoding.BigEndianUnicode.GetString(bytes);
        }
        #endregion

        #region ---- grouping ----
        // Faces into families by name ID 1, which holds at most a regular, bold, italic and bold italic.
        private static List<InstalledFamily> Group(List<Face> faces)
        {
            Dictionary<string, InstalledFamily> byName = new Dictionary<string, InstalledFamily>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, (string path, int index)> regulars = new Dictionary<string, (string, int)>(StringComparer.OrdinalIgnoreCase);
            foreach (Face face in faces)
            {
                if (face.family.Length == 0) continue;
                if (!byName.TryGetValue(face.family, out InstalledFamily? family))
                    byName[face.family] = family = new InstalledFamily { name = face.family };

                if (!face.bold && !face.italic)
                {
                    regulars.TryAdd(face.family, (face.path, face.index));
                    continue;
                }

                // the importer reads a styled face from the start of its file
                if (face.index != 0) continue;
                if (face.bold && face.italic) family.boldItalic ??= face.path;
                else if (face.bold) family.bold ??= face.path;
                else family.italic ??= face.path;
            }

            List<InstalledFamily> result = new List<InstalledFamily>();
            foreach (InstalledFamily family in byName.Values)
            {
                if (regulars.TryGetValue(family.name, out (string path, int index) regular))
                {
                    family.regular = regular.path;
                    family.regularFace = regular.index;
                }
                else if (family.bold != null) { family.regular = family.bold; family.bold = null; }
                else if (family.italic != null) { family.regular = family.italic; family.italic = null; }
                else if (family.boldItalic != null) { family.regular = family.boldItalic; family.boldItalic = null; }
                else continue;
                result.Add(family);
            }
            result.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
            return result;
        }

        private static List<string> FontFiles()
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            List<string> folders = new List<string>();
            if (OperatingSystem.IsWindows())
            {
                folders.Add(Environment.GetFolderPath(Environment.SpecialFolder.Fonts));
                folders.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Fonts"));
            }
            else if (OperatingSystem.IsMacOS())
            {
                folders.Add("/System/Library/Fonts");
                folders.Add("/Library/Fonts");
                folders.Add(Path.Combine(home, "Library", "Fonts"));
            }
            else
            {
                folders.Add("/usr/share/fonts");
                folders.Add("/usr/local/share/fonts");
                folders.Add(Path.Combine(home, ".local", "share", "fonts"));
                folders.Add(Path.Combine(home, ".fonts"));
            }
            folders.Add(Path.GetFullPath(Path.Combine(Paths.DATA, "..", "..", "_Build", "FontSources")));

            EnumerationOptions options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
            SortedSet<string> files = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string folder in folders)
            {
                if (folder.Length == 0 || !Directory.Exists(folder)) continue;
                foreach (string file in Directory.EnumerateFiles(folder, "*", options))
                    if (extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                        files.Add(Path.GetFullPath(file));
            }
            return files.ToList();
        }
        #endregion

        #region ---- cache ----
        private static Dictionary<(string, int), Face> ReadCache()
        {
            Dictionary<(string, int), Face> faces = new Dictionary<(string, int), Face>();
            if (!File.Exists(CachePath)) return faces;
            try
            {
                foreach (XElement e in XElement.Load(CachePath).Elements("Face"))
                {
                    Face face = new Face((string)e.Attribute("Path")!, (int)e.Attribute("Index")!, (long)e.Attribute("Modified")!,
                        (string?)e.Attribute("Family") ?? string.Empty, (bool?)e.Attribute("Bold") ?? false, (bool?)e.Attribute("Italic") ?? false);
                    faces[(face.path, face.index)] = face;
                }
            }
            catch (Exception e)
            {
                Log.Warn($"font catalog cache unreadable, rescanning: {e.Message}");
                faces.Clear();
            }
            return faces;
        }

        private static void WriteCache(List<Face> faces)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
            new XElement("FontCatalog", faces.Select(f => new XElement("Face",
                new XAttribute("Path", f.path),
                new XAttribute("Index", f.index),
                new XAttribute("Modified", f.modified),
                new XAttribute("Family", f.family),
                new XAttribute("Bold", f.bold),
                new XAttribute("Italic", f.italic)))).Save(CachePath);
        }
        #endregion
    }
}
