using ArctisAurora.Core.Registry;
using ArctisAurora.Core.UISystem;
using ArctisAurora.Core.UISystem.Controls.Text.Document;

namespace ArctisAurora.Core.UI
{
    // A run as a note file writes it: one styled slice of a block's text. It exists at load and save
    // only — in the tree a run is a StyleSpan on the block that holds it.
    [A_XSDType("NextRun", "UI")]
    public class NextRun
    {
        [A_XSDElementProperty("Text", "UI", "The slice of the block's text this run covers.")]
        public string text { get; set; } = string.Empty;

        [A_XSDElementProperty("Bold", "UI", "Run is bold.")]
        public bool bold { get; set; }

        [A_XSDElementProperty("Italic", "UI", "Run is italic.")]
        public bool italic { get; set; }

        [A_XSDElementProperty("Strikethrough", "UI", "Run is struck through.")]
        public bool strikethrough { get; set; }

        [A_XSDElementProperty("ColorHex", "UI", "Run's text color; absent takes the block's.")]
        public string colorHex { get; set; }

        // Resolves into ColorHex the way VulkanControl's does; a save writes the hex.
        [A_XSDElementProperty("ControlColor", "UI", "Run's text color by name.")]
        public ControlColor controlColor
        {
            get => field;
            set
            {
                field = value;
                colorHex = Control.EnumColorToHex(value);
            }
        }

        [A_XSDElementProperty("Gradient", "UI", "Name of a gradient ramped across the run in place of its color.")]
        public string gradient { get; set; }

        [A_XSDElementProperty("FontName", "UI", "Font family; absent takes the block's.")]
        public string fontName { get; set; }

        [A_XSDElementProperty("FontSize", "UI", "Type size in pixels; only written when the run chose one.")]
        public int fontSize { get; set; }

        [A_XSDElementProperty("FontSizeAuthored", "UI", "The run's FontSize was chosen, not taken from the styling scheme.")]
        public bool fontSizeAuthored { get; set; }

        [A_XSDElementProperty("StylingType", "UI", "Style this run takes; Inherit follows the block.")]
        public TextStyleType stylingType { get; set; } = TextStyleType.Inherit;

        public FontStyle Style =>
            bold ? (italic ? FontStyle.BoldItalic : FontStyle.Bold)
                 : italic ? FontStyle.Italic : FontStyle.Regular;
    }

    // One block of a note — a paragraph or a heading — as a single control: the whole block's string
    // with its runs as spans over it. Headings are not a subclass; a block names a styling type and
    // the styles scheme says what that looks like.
    public class NextBlockControl : TextRunControl
    {
        public TextStyleType stylingType = TextStyleType.Text;

        public NextBlockControl()
        {
            colorHex = "#2C2B26";
        }

        // Pressing a block focuses the editor above it, never the block itself.
        public override Control? ActiveContextTarget() => (parent as Control)?.ActiveContextTarget();

        // Copies the scheme's sizes onto the block and its spans. A span carrying an authored size
        // keeps it — the scheme only fills in sizes nobody chose.
        public void ApplyLayout(DocumentLayout layout)
        {
            lineHeight = layout.lineHeight;
            fontSize = layout.FontSizeFor(stylingType);

            for (int i = 0; i < spans.Count; i++)
            {
                StyleSpan span = spans[i];
                if (span.fontSizeAuthored) continue;

                span.fontSize = span.stylingType == TextStyleType.Inherit
                    ? 0 : layout.FontSizeFor(span.stylingType);
                spans[i] = span;
            }

            InvalidateLayout();
        }

        // Load: the run's text joins the block's string and its style becomes the next span.
        public void AppendRun(NextRun run)
        {
            string slice = run.text ?? string.Empty;

            spans.Add(new StyleSpan
            {
                count = slice.Length,
                style = run.Style,
                colorHex = run.colorHex,
                fontName = run.fontName,
                fontSize = run.fontSizeAuthored ? run.fontSize : 0,
                gradient = run.gradient,
                strikethrough = run.strikethrough,
                stylingType = run.stylingType,
                fontSizeAuthored = run.fontSizeAuthored
            });

            text += slice;
        }

        // Save: the spans cut back into runs. The last span absorbs whatever is left of the string,
        // so its count is read off the text rather than trusted.
        public List<NextRun> Runs()
        {
            string whole = text ?? string.Empty;
            List<NextRun> runs = new List<NextRun>(spans.Count);
            int start = 0;

            for (int i = 0; i < spans.Count; i++)
            {
                int count = i == spans.Count - 1
                    ? whole.Length - start
                    : Math.Clamp(spans[i].count, 0, whole.Length - start);
                if (count < 0) count = 0;

                StyleSpan span = spans[i];
                runs.Add(new NextRun
                {
                    text = whole.Substring(start, count),
                    bold = span.style == FontStyle.Bold || span.style == FontStyle.BoldItalic,
                    italic = span.style == FontStyle.Italic || span.style == FontStyle.BoldItalic,
                    strikethrough = span.strikethrough,
                    colorHex = span.colorHex,
                    gradient = span.gradient,
                    fontName = span.fontName,
                    fontSize = span.fontSizeAuthored ? span.fontSize : 0,
                    fontSizeAuthored = span.fontSizeAuthored,
                    stylingType = span.stylingType
                });
                start += count;
            }

            return runs;
        }
    }
}
