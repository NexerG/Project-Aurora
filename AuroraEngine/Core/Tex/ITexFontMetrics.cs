namespace ArctisAurora.Core.Tex
{
    // A font's em and ex in sp, for the em and ex units, and a text's width in sp, for \settowidth.
    public interface ITexFontMetrics
    {
        int Quad(TexFont font);

        int XHeight(TexFont font);

        int Width(string text, TexFont font);
    }
}
