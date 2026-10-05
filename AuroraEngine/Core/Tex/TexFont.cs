namespace ArctisAurora.Core.Tex
{
    public enum TexFamily { Roman, Sans, Mono }

    // A font selection: family, series and shape at a size in sp.
    public readonly record struct TexFont(TexFamily family, bool bold, bool italic, int size);

    // What a character is set in.
    public readonly record struct TexStyle(TexFont font, string? color, bool underline);
}
