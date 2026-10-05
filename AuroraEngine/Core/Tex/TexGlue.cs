namespace ArctisAurora.Core.Tex
{
    // A skip in sp: natural width, stretch and shrink, each with its order of infinity (0 finite, 1-3 fil to filll).
    public struct TexGlue
    {
        public int width, stretch, shrink;
        public byte stretchOrder, shrinkOrder;
    }
}
