namespace ArctisAurora.Core.Tex
{
    public enum TexAlign { Justify, Left, Center, Right }

    public enum TexParKind { Text, Heading, Code, Quote }

    public enum TexListKind { None, Itemize, Enumerate, Description }

    // What a paragraph is besides its text.
    public struct TexParStyle
    {
        public TexParKind kind;

        // heading 1-6
        public int level;
        public TexAlign align;

        // list item
        public TexListKind list;
        public int listDepth;
        public int listKindDepth;
        public int itemNumber;

        // first paragraph of an \item
        public bool item;
    }

    public abstract class TexNode { }

    #region ---- horizontal ----
    public sealed class TexChar : TexNode
    {
        public readonly char ch;
        public readonly TexStyle style;

        public TexChar(char ch, TexStyle style)
        {
            this.ch = ch;
            this.style = style;
        }
    }

    public sealed class TexGlueNode : TexNode
    {
        public readonly TexGlue glue;

        // from a space in the source
        public readonly bool interword;
        public readonly TexStyle style;

        public TexGlueNode(TexGlue glue, bool interword, TexStyle style)
        {
            this.glue = glue;
            this.interword = interword;
            this.style = style;
        }
    }

    public sealed class TexKern : TexNode
    {
        public readonly int width;
        public readonly TexStyle style;

        public TexKern(int width, TexStyle style)
        {
            this.width = width;
            this.style = style;
        }
    }

    public sealed class TexPenalty : TexNode
    {
        public const int Forced = -10000;

        public readonly int penalty;

        public TexPenalty(int penalty) => this.penalty = penalty;
    }

    public sealed class TexMathNode : TexNode
    {
        public readonly string source;
        public readonly bool display;
        public readonly TexStyle style;

        public TexMathNode(string source, bool display, TexStyle style)
        {
            this.source = source;
            this.display = display;
            this.style = style;
        }
    }

    public sealed class TexHBox : TexNode
    {
        public readonly List<TexNode> list = new List<TexNode>();
    }
    #endregion

    #region ---- vertical ----
    public sealed class TexParagraph : TexNode
    {
        public TexParStyle style;
        public readonly List<TexNode> list = new List<TexNode>();

        // source line it started on, 0 = none
        public int line;

        public TexParagraph(TexParStyle style) => this.style = style;
    }

    public sealed class TexVGlue : TexNode
    {
        public readonly TexGlue glue;

        public TexVGlue(TexGlue glue) => this.glue = glue;
    }

    public sealed class TexVPenalty : TexNode
    {
        public readonly int penalty;

        public TexVPenalty(int penalty) => this.penalty = penalty;
    }

    public sealed class TexRuleNode : TexNode { }
    #endregion
}
