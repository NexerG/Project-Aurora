namespace ArctisAurora.Core.Tex
{
    // A font's em and ex in sp, for the em and ex units.
    public interface ITexFontMetrics
    {
        int Quad(TexFont font);

        int XHeight(TexFont font);
    }
}
