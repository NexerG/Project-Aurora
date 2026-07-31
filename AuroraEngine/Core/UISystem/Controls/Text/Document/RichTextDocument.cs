using ArctisAurora.Core.Filing.Serialization;
using ArctisAurora.Core.Registry;

namespace ArctisAurora.Core.UISystem.Controls.Text.Document
{
    // How one heading level is styled. There is no fixed number of levels — however many of these a
    // layout carries is how many levels exist, so adding a seventh is data, not a code change.
    [A_XSDType("HeadingStyle", "UI")]
    public class HeadingStyle
    {
        [A_XSDElementProperty("Level", "UI", "Heading level this style applies to.")]
        public int level { get; set; } = 1;

        [A_XSDElementProperty("FontSize", "UI", "Heading text size in pixels.")]
        public int fontSize { get; set; } = 18;

        public HeadingStyle Clone() => new HeadingStyle { level = level, fontSize = fontSize };
    }

    // Layout parameters for one document: line height and text sizing now, the home for content
    // width on viewport resize and the paged/pageless mode as those land. A class rather than a
    // struct because it owns a list — copying it by value would hand a working copy the original's
    // styles to mutate.
    [A_XSDType("DocumentLayout", "UI")]
    public class DocumentLayout
    {
        // Editor-wide defaults, read from Data/XML/Documents/DocumentStyles.xml. The VFS resolves the
        // application's copy ahead of the engine's, so an app restyles every note it opens without
        // touching engine data. Loaded once, on first use.
        private static DocumentLayout defaults;
        public static DocumentLayout Defaults =>
            defaults ??= DocumentXml.LoadLayout(Paths.Doc("DocumentStyles.xml"));

        // Line box height as a multiple of font size, the way CSS line-height works — so a line is
        // as tall as the styles on it, never as tall as the particular letters that landed there.
        // 1.5 matches Obsidian's --line-height-normal.
        [A_XSDElementProperty("LineHeight", "UI", "Line box height as a multiple of the font size.")]
        public float lineHeight { get; set; } = 1.5f;

        [A_XSDElementProperty("ParagraphFontSize", "UI", "Body text size in pixels.")]
        public int paragraphFontSize { get; set; } = 18;

        // Empty means inherit: a note that declares no styles of its own uses the editor's. Declaring
        // even one replaces the whole set, so a note's heading scheme is read as written rather than
        // merged level-by-level with defaults it cannot see.
        [A_XSDElementProperty("HeadingStyle", "UI", "Per-level heading styles; empty inherits the editor's.")]
        public List<HeadingStyle> headingStyles = new List<HeadingStyle>();

        // A block's size is a property of its role, not of the runs inside it — TextRun.fontSize is
        // unused until per-run sizing lands. Resolved here rather than in the view so the layout
        // cache and the controls drawn from it cannot disagree about how big a heading is.
        public int FontSizeFor(Block block)
        {
            if (block is not HeadingBlock heading) return paragraphFontSize;

            List<HeadingStyle> styles = headingStyles.Count > 0 ? headingStyles : Defaults.headingStyles;
            if (styles.Count == 0) return paragraphFontSize;

            // Nearest defined level, so a heading deeper than the scheme defines renders as its
            // smallest heading instead of collapsing to body text.
            HeadingStyle nearest = styles[0];
            foreach (HeadingStyle style in styles)
            {
                if (style.level == heading.level) return style.fontSize;
                if (Math.Abs(style.level - heading.level) < Math.Abs(nearest.level - heading.level))
                    nearest = style;
            }
            return nearest.fontSize;
        }

        public DocumentLayout Clone()
        {
            DocumentLayout copy = new DocumentLayout
            {
                lineHeight = lineHeight,
                paragraphFontSize = paragraphFontSize
            };
            foreach (HeadingStyle style in headingStyles)
                copy.headingStyles.Add(style.Clone());
            return copy;
        }
    }

    // The note document model — the source of truth, and the on-disk format (serialized as engine
    // XML, not markdown). The control tree is a view synced from this; editing mutates a working
    // copy of it (see DocumentEditSession). XML load/save is added in P1.
    [A_XSDType("Document", "UI", allowedChildren: typeof(Block))]
    public class RichTextDocument : IXMLParser<RichTextDocument>
    {
        public List<Block> blocks = new List<Block>();

        // Named after its type, because DocumentXml names a nested member element after the type it
        // wrote — two members of the same complex type would need the writer to carry the member
        // name instead.
        [A_XSDElementProperty("DocumentLayout", "UI", "Layout parameters for this note.")]
        public DocumentLayout layout = new DocumentLayout();

        // Load from an engine-XML note file. Implements the engine's IXMLParser<T> contract (same as
        // VulkanControl / InputHandler); the string is a file path, resolved by the caller (vault).
        public static RichTextDocument ParseXML(string path) => DocumentXml.Load(path);

        public void Save(string path) => DocumentXml.Save(this, path);

        // Deep copy — used to make the isolated working copy the editor edits before a save.
        public RichTextDocument Clone()
        {
            RichTextDocument copy = new RichTextDocument();
            copy.layout = layout.Clone();
            foreach (Block block in blocks)
                copy.blocks.Add(block.Clone());
            return copy;
        }
    }
}
