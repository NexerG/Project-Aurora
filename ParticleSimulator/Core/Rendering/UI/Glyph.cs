using ArctisAurora.EngineWork.Serialization;

namespace ArctisAurora.EngineWork.Rendering.UI
{
    [@Serializable]
    public class Glyph
    {
        internal short xMin, yMin, xMax, yMax;
        internal float glyphWidth;
        internal float glyphHeight;

        [NonSerializable]
        internal List<Bezier> contours = new List<Bezier>();

        internal float advanceWidth;
        internal float leftSideOffset;
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