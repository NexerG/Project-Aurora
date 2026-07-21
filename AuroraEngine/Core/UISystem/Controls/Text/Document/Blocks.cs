using ArctisAurora.Core.Registry;
using ArctisAurora.Core.UISystem.Controls.Text.Editing;

namespace ArctisAurora.Core.UISystem.Controls.Text.Document
{
    // Block-level content: the top-level structural units of a document (a line/paragraph, a heading,
    // later lists/quotes/code). A block is a control — a TextBlockControl (a PanelControl derivative
    // that flows inline runs) — so the whole document is one VulkanControl tree, laid out by the same
    // engine UI layout as UI.xml. Abstract, no [A_XSDType], so only concrete blocks are emitted as XML
    // elements; this base is the AllowedChildren target the document scans for.
    public abstract class Block : TextBlockControl
    {
        public abstract Block Clone();
    }

    // A block whose content is a flow of inline runs. Paragraph and Heading share this; the only
    // difference today is how the view sizes them.
    public abstract class ContentBlock : Block
    {
        public List<TextRun> inlines = new List<TextRun>();

        protected void CloneInlinesInto(ContentBlock copy)
        {
            foreach (TextRun inline in inlines)
                copy.inlines.Add(inline.Clone());
        }
    }

    [A_XSDType("Paragraph", "UI", allowedChildren: typeof(TextInputControl))]
    public class ParagraphBlock : ContentBlock
    {
        public override Block Clone()
        {
            ParagraphBlock copy = new ParagraphBlock();
            CloneInlinesInto(copy);
            return copy;
        }
    }

    [A_XSDType("Heading", "UI", allowedChildren: typeof(TextInputControl))]
    public class HeadingBlock : ContentBlock
    {
        [A_XSDElementProperty("Level", "UI", "Heading level 1-6.")]
        public int level { get; set; } = 1;

        public override Block Clone()
        {
            HeadingBlock copy = new HeadingBlock { level = level };
            CloneInlinesInto(copy);
            return copy;
        }
    }
}
