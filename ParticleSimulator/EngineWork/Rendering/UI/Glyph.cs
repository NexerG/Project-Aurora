using ArctisAurora.EngineWork.Serialization;

namespace ArctisAurora.EngineWork.Rendering.UI
{
    [@Serializable]
    public class Glyph
    {
        /*[@Serializable, StructLayout(LayoutKind.Sequential, Pack = 1)]
        struct GlyphData
        {
            public int Unicode;
            public short XMin, YMin, XMax, YMax;
            public float AdvanceWidth;
            public float BearingX, BearingY;
            public int AtlasX, AtlasY, Width, Height;
        }*/

        internal short xMin, yMin, xMax, yMax;
        internal float glyphWidth;
        internal float glyphHeight;

        [NonSerializable]
        internal List<Bezier> contours = new List<Bezier>();

        internal float rsb, lsb;
        internal float tsb = 0;

        public Glyph()
        {

        }

        internal void SetParams(short xMin, short xMax, short yMin, short yMax, float unitsPerEm)
        {
            this.xMin = xMin;
            this.xMax = xMax;
            this.yMin = yMin;
            this.yMax = yMax;

            glyphWidth = (xMax - xMin) / unitsPerEm;
            glyphHeight = (yMax - yMin) / unitsPerEm;
        }
    }
}