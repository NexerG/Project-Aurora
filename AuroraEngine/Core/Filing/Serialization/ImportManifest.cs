using ArctisAurora.Core.Registry;

namespace ArctisAurora.Core.Filing.Serialization
{
    [A_XSDType("Charset", "AssetRegistry")]
    public class Charset
    {
        [A_XSDElementProperty("Name", "AssetRegistry")]
        public string name { get; set; } = string.Empty;

        [A_XSDElementProperty("Chars", "AssetRegistry")]
        public string chars { get; set; } = string.Empty;
    }

    [A_XSDType("FontImport", "AssetRegistry")]
    public class FontImport
    {
        [A_XSDElementProperty("Source", "AssetRegistry")]
        public string source { get; set; } = string.Empty;

        [A_XSDElementProperty("Charset", "AssetRegistry")]
        public string charset { get; set; } = string.Empty;

        [A_XSDElementProperty("GlyphSize", "AssetRegistry")]
        public int glyphSize { get; set; } = 64;
    }

    [A_XSDType("ImportSet", "AssetRegistry")]
    public class ImportSet
    {
        [A_XSDElementProperty("Charset", "AssetRegistry")]
        public List<Charset> charsets { get; set; } = new List<Charset>();

        [A_XSDElementProperty("FontImport", "AssetRegistry")]
        public List<FontImport> fonts { get; set; } = new List<FontImport>();
    }

    // Written beside a font's cooked output; a bake is skipped when it still matches the declaration.
    public class FontImportStamp
    {
        public string source = string.Empty;
        public string sourceHash = string.Empty;
        public string charset = string.Empty;
        public int glyphSize;
        public int importerVersion;

        public bool Matches(FontImportStamp other) =>
            source == other.source
            && sourceHash == other.sourceHash
            && charset == other.charset
            && glyphSize == other.glyphSize
            && importerVersion == other.importerVersion;
    }
}
