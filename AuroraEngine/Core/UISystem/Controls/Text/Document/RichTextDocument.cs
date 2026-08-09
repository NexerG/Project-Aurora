using ArctisAurora.Core.Registry;

namespace ArctisAurora.Core.UISystem.Controls.Text.Document
{
    // Which style entry a piece of text takes. A run says Inherit and follows its block; a block that
    // says nothing is body text. Members without an entry in the styles file are legal — a heading
    // past the end of the scheme falls back to the last heading the scheme does define.
    [A_XSDType("TextStyleType", "UI")]
    public enum TextStyleType
    {
        Inherit,
        Text,
        Heading1,
        Heading2,
        Heading3,
        Heading4,
        Heading5,
        Heading6,
        Comment,
        Code,
        Quote
    }

    // How one styling type is rendered. However many of these a layout carries is the whole scheme,
    // so adding a level is data, not a code change.
    [A_XSDType("TextStyle", "UI")]
    public class TextStyle
    {
        [A_XSDElementProperty("Type", "UI", "Styling type this entry applies to.")]
        public TextStyleType type { get; set; } = TextStyleType.Text;

        [A_XSDElementProperty("FontSize", "UI", "Text size in pixels.")]
        public int fontSize { get; set; } = 18;

        public TextStyle Clone() => new TextStyle { type = type, fontSize = fontSize };
    }

    // Layout parameters for one document: line height and text sizing now, the home for content
    // width on viewport resize and the paged/pageless mode as those land. A class rather than a
    // struct because it owns a list — copying it by value would hand a working copy the original's
    // styles to mutate.
    [A_XSDType("DocumentLayout", "UI")]
    public class DocumentLayout
    {
        // Editor-wide defaults, owned by the settings registry.
        public static DocumentLayout Defaults => SettingsRegistry.Get<DocumentSettings>().layout;

        // Line box height as a multiple of font size, the way CSS line-height works — so a line is
        // as tall as the styles on it, never as tall as the particular letters that landed there.
        // 1.5 matches Obsidian's --line-height-normal.
        [A_XSDElementProperty("LineHeight", "UI", "Line box height as a multiple of the font size.")]
        public float lineHeight { get; set; } = 1.5f;

        // Used when neither the note nor the editor scheme names a size for the type asked for.
        private const int fallbackFontSize = 18;

        // Vertical gap between blocks. Lives here rather than on the view because the layout cache
        // stacks blocks by it too — a number the two disagreed on would put every cached block top
        // further out of step with the drawn one the further down the document you scrolled.
        [A_XSDElementProperty("BlockSpacing", "UI", "Vertical gap between blocks in pixels.")]
        public float blockSpacing { get; set; } = 8f;

        // Empty means inherit: a note that declares no styles of its own uses the editor's. Declaring
        // even one replaces the whole set, so a note's heading scheme is read as written rather than
        // merged level-by-level with defaults it cannot see.
        [A_XSDElementProperty("TextStyle", "UI", "Per-type text styles; empty inherits the editor's.")]
        public List<TextStyle> textStyles = new List<TextStyle>();

        // Resolved here rather than in the view so the measurer and the controls drawn from it cannot
        // disagree about how big a heading is.
        public int FontSizeFor(TextStyleType type)
        {
            if (type == TextStyleType.Inherit) type = TextStyleType.Text;

            List<TextStyle> styles = textStyles.Count > 0 ? textStyles : Defaults.textStyles;

            foreach (TextStyle style in styles)
                if (style.type == type) return style.fontSize;

            // A heading deeper than the scheme defines takes the last heading it does define, rather
            // than collapsing to body text.
            if (IsHeading(type))
                for (int i = styles.Count - 1; i >= 0; i--)
                    if (IsHeading(styles[i].type)) return styles[i].fontSize;

            return fallbackFontSize;
        }

        private static bool IsHeading(TextStyleType type) =>
            type >= TextStyleType.Heading1 && type <= TextStyleType.Heading6;

        public DocumentLayout Clone()
        {
            DocumentLayout copy = new DocumentLayout
            {
                lineHeight = lineHeight,
                blockSpacing = blockSpacing
            };
            foreach (TextStyle style in textStyles)
                copy.textStyles.Add(style.Clone());
            return copy;
        }
    }

    // Editor-wide document defaults as a settings group, so the styles scheme cascades across mounts
    // and saves the same way every other group does.
    [A_XSDType("DocumentSettings", "Settings")]
    public class DocumentSettings : ISettingsGroup
    {
        [A_XSDElementProperty("DocumentLayout", "Settings", "Editor-wide layout and text styles.")]
        public DocumentLayout layout { get; set; } = new DocumentLayout();
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
