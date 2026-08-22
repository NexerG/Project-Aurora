using ArctisAurora.Core.UISystem.Controls.Text.Editing;

namespace ArctisAurora.Core.UISystem.Controls.Text.Document.Edits
{
    // A run as plain data. The fields are the ones TextRun.Clone carries, minus the ones
    // ContentBlock.ApplyLayout recomputes from the block it lands in.
    public sealed class RunSnapshot
    {
        public string text;
        public bool bold;
        public bool italic;
        public bool strikethrough;
        public string controlColorHex;
        public string gradient;
        public string fontName;
        public int fontSize;
        public TextStyleType stylingType;

        public static RunSnapshot Of(TextControl run, string text)
        {
            TextInputControl input = run as TextInputControl;

            return new RunSnapshot
            {
                text = text,
                bold = input?.bold ?? false,
                italic = input?.italic ?? false,
                strikethrough = input?.strikethrough ?? false,
                controlColorHex = run.controlColorHex,
                gradient = run.gradient,
                fontName = run.fontName,
                fontSize = run.fontSize,
                stylingType = run.stylingType
            };
        }

        public TextRun Build() => new TextRun
        {
            bold = bold,
            italic = italic,
            strikethrough = strikethrough,
            controlColorHex = controlColorHex,
            gradient = gradient,
            fontName = fontName,
            fontSize = fontSize,
            stylingType = stylingType,
            text = text
        };
    }

    // A block as plain data.
    public sealed class BlockSnapshot
    {
        public TextStyleType stylingType;
        public readonly List<RunSnapshot> runs = new List<RunSnapshot>();
    }
}
