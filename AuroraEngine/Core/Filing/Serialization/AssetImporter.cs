using ArctisAurora.Core.Registry;
using ArctisAurora.Core.UISystem;
using ArctisAurora.EngineWork;
using System.Security.Cryptography;
using System.Xml.Linq;
using static ArctisAurora.Core.UISystem.AuroraFont;
using AuroraFont = ArctisAurora.Core.UISystem.AuroraFont;

namespace ArctisAurora.Core.Filing.Serialization
{
    public unsafe static class AssetImporter
    {
        // Bump to invalidate every stamp and force a re-bake.
        private const int importerVersion = 1;

        [A_XSDActionDependency("AssetImporter.RunImports", "Bootstrap")]
        public static bool RunImports()
        {
            if (!Engine.isDebug) return true;

            List<ImportSet> sets = new List<ImportSet>();
            foreach (string file in VirtualFileSystem.EnumerateAll("XML/Imports", "*.xml"))
                sets.Add(ParseImportSet(file));

            Dictionary<string, string> charsets = new Dictionary<string, string>();
            foreach (ImportSet set in sets)
                foreach (Charset charset in set.charsets)
                    charsets[charset.name] = charset.chars;

            foreach (ImportSet set in sets)
                foreach (FontImport font in set.fonts)
                    ImportFontIfStale(font, charsets);

            return true;
        }

        private static ImportSet ParseImportSet(string path)
        {
            ImportSet set = new ImportSet();
            XElement root = XElement.Load(path);
            XNamespace ns = root.GetDefaultNamespace();

            foreach (XElement element in root.Elements(ns + "Charset"))
            {
                Charset charset = new Charset();
                XmlReflection.ApplyAttributes(element, charset);
                set.charsets.Add(charset);
            }
            foreach (XElement element in root.Elements(ns + "FontImport"))
            {
                FontImport font = new FontImport();
                XmlReflection.ApplyAttributes(element, font);
                set.fonts.Add(font);
            }
            return set;
        }

        // Machine-wide fonts first, then the per-user font folder.
        internal static string ResolveSystemFont(string fileName)
        {
            string machine = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), fileName);
            if (File.Exists(machine)) return machine;

            string user = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "Windows", "Fonts", fileName);
            if (File.Exists(user)) return user;

            return null;
        }

        private static void ImportFontIfStale(FontImport font, Dictionary<string, string> charsets)
        {
            string sourcePath = ResolveSystemFont(font.source);
            if (sourcePath == null)
            {
                Console.WriteLine($"Font import '{font.source}': no such font installed, skipped.");
                return;
            }
            if (!charsets.TryGetValue(font.charset, out string chars))
            {
                Console.WriteLine($"Font import '{font.source}': unknown charset '{font.charset}', skipped.");
                return;
            }

            string baseName = Path.GetFileNameWithoutExtension(font.source);
            FontImportStamp wanted = new FontImportStamp()
            {
                source = font.source,
                sourceHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(sourcePath))),
                charset = chars,
                glyphSize = font.glyphSize,
                importerVersion = importerVersion
            };

            if (IsUpToDate(baseName, wanted)) return;

            Console.WriteLine($"Font import '{font.source}': baking {chars.Length} glyphs at {font.glyphSize}px...");
            ClearStamp(baseName);
            ImportFont(chars, font.source, font.glyphSize, Paths.FONTS);
            WriteStamp(baseName, wanted);
        }

        // Drops the stamp ahead of a bake.
        private static void ClearStamp(string baseName)
        {
            string path = Path.Combine(Paths.FONTS, baseName, baseName + ".import.xml");
            if (File.Exists(path)) File.Delete(path);
        }

        // A stamp only counts when the files it describes are actually present.
        private static bool IsUpToDate(string baseName, FontImportStamp wanted)
        {
            if (!File.Exists(Paths.Font(baseName, baseName + ".agd"))) return false;
            if (!File.Exists(Paths.Font(baseName, baseName + "_atlas.png"))) return false;

            string stampPath = Paths.Font(baseName, baseName + ".import.xml");
            if (!File.Exists(stampPath)) return false;

            XElement root = XElement.Load(stampPath);
            FontImportStamp found = new FontImportStamp()
            {
                source = (string)root.Attribute("Source") ?? string.Empty,
                sourceHash = (string)root.Attribute("SourceHash") ?? string.Empty,
                charset = (string)root.Attribute("Charset") ?? string.Empty,
                glyphSize = (int?)root.Attribute("GlyphSize") ?? 0,
                importerVersion = (int?)root.Attribute("ImporterVersion") ?? 0
            };
            return found.Matches(wanted);
        }

        private static void WriteStamp(string baseName, FontImportStamp stamp)
        {
            string dir = Path.Combine(Paths.FONTS, baseName);
            Directory.CreateDirectory(dir);

            new XElement("FontImportStamp",
                new XAttribute("Source", stamp.source),
                new XAttribute("SourceHash", stamp.sourceHash),
                new XAttribute("Charset", stamp.charset),
                new XAttribute("GlyphSize", stamp.glyphSize),
                new XAttribute("ImporterVersion", stamp.importerVersion))
                .Save(Path.Combine(dir, baseName + ".import.xml"));
        }

        public static void ImportFont(string characters, string fontName, int glyphSize, string outputRoot)
        {
            var fs = new FileStream(ResolveSystemFont(fontName), FileMode.Open, FileAccess.Read);
            var reader = new BinaryReader(fs);

            AuroraFont font = new AuroraFont();

            font.fontMeta = new FontMeta
            {
                version = ReadUInt32BE(reader),  // Big-endian
                tableCount = ReadUInt16BE(reader)
            };

            // Skip other fields (searchRange, entrySelector, rangeShift)
            fs.Position += 6;
            font.tableEntries = new TableEntry[font.fontMeta.tableCount];
            for (int i = 0; i < font.fontMeta.tableCount; i++)
            {
                font.tableEntries[i] = new TableEntry
                {
                    name = new string(reader.ReadChars(4)),
                    checksum = ReadUInt32BE(reader),
                    offset = ReadUInt32BE(reader),
                    length = ReadUInt32BE(reader)
                };
            }

            font.textData = new TextData
            {
                characterCount = characters.Length,
                characters = characters.ToCharArray()
            };

            //font.headTableInfo = new HeadTableInfo();
            //reader.BaseStream.Position = font.tableEntries.First(t => t.name == "head").offset + 50;
            //font.headTableInfo.indexToLocFormat = ReadUInt16BE(reader); // 0 = uint16, 1 = uint32

            string baseName = fontName.Split('.')[0];
            Directory.CreateDirectory(Path.Combine(outputRoot, baseName));
            string path = Path.Combine(outputRoot, baseName, baseName + ".afm");

            Serializer.SerializeAttributed(font, path);
            reader.Dispose();
            reader.Close();
            fs.Dispose();
            fs.Close();

            AuroraFont f = new AuroraFont();
            Serializer.DeserializeAttributed(path, ref f);
            //f.Deserialize(path);
            AuroraFont.GenerateGlyphAtlas(f, fontName, glyphSize, outputRoot);
        }

        internal static short ReadInt16BE(BinaryReader reader) =>
            BitConverter.ToInt16(reader.ReadBytes(2).Reverse().ToArray(), 0);

        internal static ushort ReadUInt16BE(BinaryReader reader) =>
            BitConverter.ToUInt16(reader.ReadBytes(2).Reverse().ToArray(), 0);

        internal static uint ReadUInt32BE(BinaryReader reader) =>
            BitConverter.ToUInt32(reader.ReadBytes(4).Reverse().ToArray(), 0);

    }
}
