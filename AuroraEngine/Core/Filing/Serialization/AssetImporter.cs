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
        private static readonly Diagnostics.LogChannel Log = Diagnostics.LogChannel.For("Assets");

        // Bump to invalidate every stamp and force a re-bake.
        private const int importerVersion = 2;

        [A_XSDActionDependency("AssetImporter.RunImports", "Bootstrap")]
        public static bool RunImports()
        {
            if (!Engine.isDebug) return true;

            List<ImportSet> sets = new List<ImportSet>();
            foreach (string file in VirtualFileSystem.EnumerateAll("XML/Imports", "*.imports.xml"))
                sets.Add(ParseImportSet(file));

            Dictionary<string, string> charsets = new Dictionary<string, string>();
            foreach (ImportSet set in sets)
                foreach (Charset charset in set.charsets)
                    charsets[charset.name] = charset.chars;

            foreach (ImportSet set in sets)
                foreach (FontImport font in set.fonts)
                    ImportFontIfStale(font, charsets);

            foreach (ImportSet set in sets)
                foreach (IconImport icons in set.icons)
                    ImportIconsIfStale(icons);

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
            foreach (XElement element in root.Elements(ns + "IconImport"))
            {
                IconImport icons = new IconImport();
                XmlReflection.ApplyAttributes(element, icons);
                set.icons.Add(icons);
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
                Log.Warn($"font import '{font.source}': no such font installed, skipped.");
                return;
            }
            if (!charsets.TryGetValue(font.charset, out string chars))
            {
                Log.Warn($"font import '{font.source}': unknown charset '{font.charset}', skipped.");
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

            XElement found = ReadStamp(Paths.Font, baseName, baseName + ".agd", baseName + "_atlas.png");
            if (found != null && wanted.Matches(new FontImportStamp()
            {
                source = (string)found.Attribute("Source") ?? string.Empty,
                sourceHash = (string)found.Attribute("SourceHash") ?? string.Empty,
                charset = (string)found.Attribute("Charset") ?? string.Empty,
                glyphSize = (int?)found.Attribute("GlyphSize") ?? 0,
                importerVersion = (int?)found.Attribute("ImporterVersion") ?? 0
            })) return;

            Log.Info($"font import '{font.source}': baking {chars.Length} glyphs at {font.glyphSize}px...");
            ClearStamp(Paths.FONTS, baseName);
            ImportFont(chars, font.source, font.glyphSize, Paths.FONTS);
            WriteStamp(Paths.FONTS, baseName, new XElement("FontImportStamp",
                new XAttribute("Source", wanted.source),
                new XAttribute("SourceHash", wanted.sourceHash),
                new XAttribute("Charset", wanted.charset),
                new XAttribute("GlyphSize", wanted.glyphSize),
                new XAttribute("ImporterVersion", wanted.importerVersion)));
        }

        private static void ImportIconsIfStale(IconImport icons)
        {
            string[] files = VirtualFileSystem.EnumerateAll(icons.source, "*.svg")
                .OrderBy(Path.GetFileName, StringComparer.Ordinal).ToArray();
            if (files.Length == 0)
            {
                Log.Warn($"icon import '{icons.name}': no .svg under '{icons.source}', skipped.");
                return;
            }

            IconImportStamp wanted = new IconImportStamp()
            {
                source = icons.source,
                sourceHash = HashFiles(files),
                iconSize = icons.iconSize,
                importerVersion = importerVersion
            };

            XElement found = ReadStamp(Paths.Icon, icons.name, icons.name + ".aid", icons.name + "_atlas.png");
            if (found != null && wanted.Matches(new IconImportStamp()
            {
                source = (string)found.Attribute("Source") ?? string.Empty,
                sourceHash = (string)found.Attribute("SourceHash") ?? string.Empty,
                iconSize = (int?)found.Attribute("IconSize") ?? 0,
                importerVersion = (int?)found.Attribute("ImporterVersion") ?? 0
            })) return;

            Log.Info($"icon import '{icons.name}': baking {files.Length} icons at {icons.iconSize}px...");
            ClearStamp(Paths.ICONS, icons.name);
            IconSet.GenerateIconAtlas(icons.name, files, icons.iconSize, Paths.ICONS);
            WriteStamp(Paths.ICONS, icons.name, new XElement("IconImportStamp",
                new XAttribute("Source", wanted.source),
                new XAttribute("SourceHash", wanted.sourceHash),
                new XAttribute("IconSize", wanted.iconSize),
                new XAttribute("ImporterVersion", wanted.importerVersion)));
        }

        // Names and contents both, so renaming an icon invalidates the bake the same as editing one.
        private static string HashFiles(string[] files)
        {
            using MemoryStream digest = new MemoryStream();
            foreach (string file in files)
            {
                byte[] name = System.Text.Encoding.UTF8.GetBytes(Path.GetFileName(file));
                digest.Write(name, 0, name.Length);
                digest.Write(SHA256.HashData(File.ReadAllBytes(file)));
            }
            return Convert.ToHexString(SHA256.HashData(digest.ToArray()));
        }

        // Drops the stamp ahead of a bake.
        private static void ClearStamp(string root, string name)
        {
            string path = Path.Combine(root, name, name + ".import.xml");
            if (File.Exists(path)) File.Delete(path);
        }

        // A stamp only counts when the files it describes are actually present.
        private static XElement ReadStamp(Func<string, string, string> resolve, string name, params string[] outputs)
        {
            foreach (string output in outputs)
                if (!File.Exists(resolve(name, output))) return null;

            string stampPath = resolve(name, name + ".import.xml");
            return File.Exists(stampPath) ? XElement.Load(stampPath) : null;
        }

        private static void WriteStamp(string root, string name, XElement stamp)
        {
            string dir = Path.Combine(root, name);
            Directory.CreateDirectory(dir);
            stamp.Save(Path.Combine(dir, name + ".import.xml"));
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
