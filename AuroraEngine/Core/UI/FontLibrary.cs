using ArctisAurora.Core.Diagnostics;
using ArctisAurora.Core.Filing;
using ArctisAurora.Core.Filing.Serialization;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Registry.Assets;
using ArctisAurora.Core.Testing;
using ArctisAurora.EngineWork;
using ArctisAurora.EngineWork.Registry;
using System.Xml.Linq;

namespace ArctisAurora.Core.UI
{
    // The fonts a picker offers: the registry's, and installed families imported on demand into the user's data.
    public static class FontLibrary
    {
        private static readonly LogChannel Log = LogChannel.For("Assets");

        private const int glyphSize = 64;
        private static readonly XNamespace ns = "http://arctisaurora/AuroraAssetRegistryTypes";

        private static string ImportsPath => Path.Combine(Paths.USER_DATA, "XML", "Imports", "UserFonts.imports.axml");
        private static string AssetsPath => Path.Combine(Paths.USER_DATA, "XML", "Assets", "UserFonts.assets.axml");

        private static Dictionary<string, FontAsset> Fonts =>
            AssetRegistries.GetRegistryByValueType<string, FontAsset>(typeof(FontAsset));

        public static bool importing { get; private set; }

        [A_XSDActionDependency("Fonts.StartCatalog", "Bootstrap")]
        internal static bool StartCatalog()
        {
            if (!TestRunner.active) Task.Run(LoadCatalog);
            return true;
        }

        // Scans the installed fonts and bakes their previews, then registers the previews on the main thread.
        internal static void LoadCatalog()
        {
            try
            {
                FontPreviews.Bake(FontCatalog.Scan(), AssetImporter.UserFonts);
                Engine.Post(RegisterPreviews);
            }
            catch (Exception e)
            {
                Log.Error($"font catalog failed: {e.Message}");
            }
        }

        internal static void RegisterPreviews()
        {
            FontAsset previews = new FontAsset();
            previews.Load(FontPreviews.AssetName, "Fonts/" + FontPreviews.AssetName);
            Fonts[FontPreviews.AssetName] = previews;
        }

        #region ---- imported ----
        // One asset per family, the one with the most faces winning.
        public static List<(string asset, string family)> Imported()
        {
            Dictionary<string, (string asset, FontAsset font)> best = new Dictionary<string, (string, FontAsset)>(StringComparer.OrdinalIgnoreCase);
            foreach ((string name, FontAsset font) in Fonts)
            {
                if (name == FontPreviews.AssetName) continue;

                string family = font.family.Length > 0 ? font.family : name;
                if (best.TryGetValue(family, out (string asset, FontAsset font) held))
                {
                    int faces = font.atlasMetaData.styleCount;
                    int heldFaces = held.font.atlasMetaData.styleCount;
                    if (faces < heldFaces || (faces == heldFaces && held.asset == "default")) continue;
                }
                best[family] = (name, font);
            }
            return best.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase).Select(p => (p.Value.asset, p.Key)).ToList();
        }

        // A family's name in the preview font's codepoints, once the previews are registered.
        public static string? PreviewOf(string family) =>
            Fonts.ContainsKey(FontPreviews.AssetName) ? FontPreviews.TextFor(family) : null;

        public static string FamilyOf(string asset) =>
            Fonts.TryGetValue(asset, out FontAsset? font) && font.family.Length > 0 ? font.family : asset;

        public static string? AssetFor(string family)
        {
            foreach ((string asset, string name) in Imported())
                if (string.Equals(name, family, StringComparison.OrdinalIgnoreCase)) return asset;
            return null;
        }
        #endregion

        #region ---- import ----
        // Bakes an installed family behind the wait window, then registers it; done gets its asset name.
        public static void Import(InstalledFamily family, Action<string> done)
        {
            if (importing) return;
            if (AssetFor(family.name) is string existing)
            {
                done(existing);
                return;
            }

            importing = true;
            FontImport import = new FontImport
            {
                source = family.regular,
                face = family.regularFace,
                bold = family.bold ?? string.Empty,
                italic = family.italic ?? string.Empty,
                boldItalic = family.boldItalic ?? string.Empty,
                charset = Packs(),
                glyphSize = glyphSize,
                covered = true
            };

            FontImportWindow.Show(Engine.primary, $"Importing {family.name}, please wait");
            Task.Run(() => AssetImporter.ImportFontIfStale(import, AssetImporter.Charsets(), AssetImporter.UserFonts)).ContinueWith(task =>
            {
                bool baked = task.IsCompletedSuccessfully && task.Result;
                string? error = task.Exception?.InnerException?.Message;
                Engine.Post(() =>
                {
                    importing = false;
                    if (!baked)
                    {
                        Log.Warn($"font import '{family.name}' failed{(error == null ? "" : ": " + error)}.");
                        FontImportWindow.Fail($"{family.name} could not be imported");
                        return;
                    }
                    FontImportWindow.Hide();
                    done(Register(family, import));
                });
            });
        }

        // Records the import in the user's sets and loads it into the registry.
        private static string Register(InstalledFamily family, FontImport import)
        {
            string folder = Path.GetFileNameWithoutExtension(import.source);
            string slug = string.Join('-', family.name.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
            string asset = slug;
            for (int n = 2; Fonts.ContainsKey(asset); n++) asset = $"{slug}-{n}";

            XElement imports = LoadOrNew(ImportsPath, "ImportSet");
            imports.Elements(ns + "FontImport").Where(e => (string?)e.Attribute("Source") == import.source).Remove();
            XElement element = new XElement(ns + "FontImport",
                new XAttribute("Source", import.source),
                new XAttribute("Face", import.face));
            if (import.bold.Length > 0) element.Add(new XAttribute("Bold", import.bold));
            if (import.italic.Length > 0) element.Add(new XAttribute("Italic", import.italic));
            if (import.boldItalic.Length > 0) element.Add(new XAttribute("BoldItalic", import.boldItalic));
            element.Add(new XAttribute("Charset", import.charset), new XAttribute("GlyphSize", import.glyphSize), new XAttribute("Covered", true));
            imports.Add(element);
            Save(imports, ImportsPath);

            XElement assets = LoadOrNew(AssetsPath, "AssetManifest");
            assets.Add(new XElement(ns + "Asset", new XAttribute("Type", "FontAsset"), new XAttribute("Name", asset), new XAttribute("Source", "Fonts/" + folder)));
            Save(assets, AssetsPath);

            FontAsset font = new FontAsset();
            font.Load(asset, "Fonts/" + folder);
            Fonts[asset] = font;
            Log.Info($"font '{family.name}' imported as '{asset}'.");
            return asset;
        }

        private static XElement LoadOrNew(string path, string root) =>
            File.Exists(path) ? XElement.Load(path) : new XElement(ns + root);

        private static void Save(XElement root, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            root.Save(path);
        }
        #endregion

        #region ---- language packs ----
        private static string Packs() => SettingsRegistry.Get<FontSettings>().languages.packs;

        // Re-bakes every user font with the current packs behind the wait window; the bakes load on the next start.
        [A_XSDActionDependency("Fonts.Rebake", "Settings", "Re-bakes imported fonts with the language packs")]
        internal static void Rebake()
        {
            if (importing || !File.Exists(ImportsPath)) return;

            XElement imports = XElement.Load(ImportsPath);
            List<FontImport> fonts = new List<FontImport>();
            foreach (XElement element in imports.Elements(ns + "FontImport"))
            {
                element.SetAttributeValue("Charset", Packs());
                FontImport font = new FontImport();
                XmlReflection.ApplyAttributes(element, font);
                fonts.Add(font);
            }
            Save(imports, ImportsPath);
            if (fonts.Count == 0) return;

            importing = true;
            FontImportWindow.Show(Engine.primary, "Importing language packs, please wait");
            Task.Run(() =>
            {
                Dictionary<string, string> charsets = AssetImporter.Charsets();
                foreach (FontImport font in fonts)
                    AssetImporter.ImportFontIfStale(font, charsets, AssetImporter.UserFonts);
            }).ContinueWith(task => Engine.Post(() =>
            {
                importing = false;
                if (!task.IsCompletedSuccessfully)
                {
                    Log.Warn($"language pack re-bake failed: {task.Exception?.InnerException?.Message}");
                    FontImportWindow.Fail("The language packs could not be imported");
                    return;
                }
                FontImportWindow.Hide();
                Log.Info($"language packs re-baked; they load on the next start.");
            }));
        }
        #endregion
    }
}
