using ArctisAurora.Core.Filing.Serialization;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Registry.Assets;
using ArctisAurora.Core.UISystem.Controls.Containers;
using ArctisAurora.Core.UISystem.Controls.Text.Editing;
using ArctisAurora.EngineWork.Registry;
// WinForms is enabled in this project; alias the engine control to avoid the
// System.Windows.Forms.ScrollableControl clash.
using ScrollableControl = ArctisAurora.Core.UISystem.Controls.Containers.ScrollableControl;

namespace ArctisAurora.Core.UISystem.Controls.Text.Document
{
    // The view over a RichTextDocument. A scrollable viewport whose single child is a vertical
    // StackPanel of one control per block; each ContentBlock becomes a TextBlockControl that flows
    // one TextInputControl per run (TextInputControl reused purely as a styled run renderer).
    //
    // P2 is read-only: it builds the control tree once from a document. Editing (working copy, caret,
    // input) arrives in P3; rebuild-on-edit must also remove stale run/glyph controls from the
    // "Controls" render group (the deferred-cleanup TODO already noted in TextControl).
    [A_XSDType("DocumentEditor", "UI")]
    public class DocumentEditorControl : ScrollableControl
    {
        // Paragraph body size; headings scale up by level.
        public int paragraphFontSize = 18;

        public RichTextDocument activeDocument { get; private set; }

        public DocumentEditorControl()
        {
            scrollDirection = ScrollDirection.Vertical;
            maskAsset = AssetRegistries.GetAsset<TextureAsset>("invisible");
        }

        // Engine-XML note to load. Path is resolved through the VFS (Paths.Doc) when relative, or used
        // as-is when rooted (the vault passes absolute paths in P5).
        [A_XSDElementProperty("Source", "UI", "Engine-XML note file to load into the editor.")]
        public string source
        {
            get => field;
            set
            {
                field = value;
                if (!string.IsNullOrEmpty(value))
                    LoadPath(value);
            }
        }

        public void LoadPath(string nameOrPath)
        {
            string path = Path.IsPathRooted(nameOrPath) ? nameOrPath : Paths.Doc(nameOrPath);
            LoadDocument(RichTextDocument.ParseXML(path));
        }

        public void LoadDocument(RichTextDocument document)
        {
            activeDocument = document;

            StackPanelControl stack = new StackPanelControl
            {
                orientation = StackPanelControl.Orientation.Vertical,
                Spacing = 8f,
                horizontalAlignment = HorizontalAlignment.Stretch
            };
            stack.maskAsset = AssetRegistries.GetAsset<TextureAsset>("invisible");

            foreach (Block block in document.blocks)
            {
                VulkanControl blockControl = BuildBlock(block);
                if (blockControl != null)
                    stack.AddChild(blockControl);
            }

            SetContent(stack);
        }

        // ScrollableControl holds a single child; replace it (clearing any previous content).
        private void SetContent(VulkanControl content)
        {
            children.Clear();
            AddChild(content);
        }

        private VulkanControl BuildBlock(Block block)
        {
            if (block is not ContentBlock content)
                return null;

            int fontSize = block is HeadingBlock heading ? HeadingFontSize(heading.level) : paragraphFontSize;

            TextBlockControl textBlock = new TextBlockControl
            {
                horizontalAlignment = HorizontalAlignment.Stretch
            };

            foreach (TextRun run in content.inlines)
            {
                TextInputControl runControl = new TextInputControl
                {
                    fontSize = fontSize,
                    bold = run.bold,
                    italic = run.italic,
                    strikethrough = run.strikethrough,
                    runColorHex = run.runColorHex,
                    fontName = run.fontName
                };
                runControl.text = run.text; // builds glyph children at the set font size
                textBlock.AddChild(runControl);
            }

            return textBlock;
        }

        private int HeadingFontSize(int level) => level switch
        {
            1 => 34,
            2 => 28,
            3 => 23,
            4 => 20,
            5 => 18,
            _ => 16
        };
    }
}
