using ArctisAurora.Core.ECS.EngineEntity;
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
        protected Block()
        {
            BubbleAll();
        }

        public abstract Block Clone();

        // Copies the document's line settings down onto the runs.
        public abstract void ApplyLayout(DocumentLayout layout);
    }

    // A block whose content is a flow of inline runs, held as its children. Headings are not a
    // separate class — a block names a styling type and the styles file says what that looks like.
    [A_XSDType("Block", "UI", allowedChildren: typeof(TextInputControl))]
    public class ContentBlock : Block
    {
        [A_XSDElementProperty("StylingType", "UI", "Style this block's text takes from the styles file.")]
        public TextStyleType stylingType { get; set; } = TextStyleType.Text;

        // The index-th run among the children.
        public TextRun RunAt(int index)
        {
            int i = 0;
            foreach (Entity child in children)
                if (child is TextRun run && i++ == index) return run;

            return null;
        }

        protected void CloneInlinesInto(ContentBlock copy)
        {
            foreach (Entity child in children)
                if (child is TextRun inline) copy.AddChild(inline.Clone());
        }

        public override Block Clone()
        {
            ContentBlock copy = new ContentBlock { stylingType = stylingType };
            CloneInlinesInto(copy);
            return copy;
        }

        // A run's own styling type wins over the block's; Inherit means it has none.
        public override void ApplyLayout(DocumentLayout layout)
        {
            foreach (Entity child in children)
            {
                if (child is not TextRun run) continue;

                TextStyleType type = run.stylingType == TextStyleType.Inherit ? stylingType : run.stylingType;
                run.lineHeight = layout.lineHeight;
                run.fontSize = layout.FontSizeFor(type);
            }
        }
    }
}
