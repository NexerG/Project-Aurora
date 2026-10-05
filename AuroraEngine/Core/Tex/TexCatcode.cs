namespace ArctisAurora.Core.Tex
{
    // TeX's character categories, numbered as \catcode reads them.
    public enum TexCatcode : byte
    {
        Escape, BeginGroup, EndGroup, MathShift, AlignTab, EndLine, Parameter, Superscript,
        Subscript, Ignored, Space, Letter, Other, Active, Comment, Invalid
    }
}
