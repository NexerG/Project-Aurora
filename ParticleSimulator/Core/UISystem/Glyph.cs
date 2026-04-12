using ArctisAurora.Core.Filing.Serialization;
using Silk.NET.Maths;

namespace ArctisAurora.Core.UISystem
{
    [@Serializable]
    public class Glyph
    {
        public short xMin, yMin, xMax, yMax;
        public float glyphWidth;
        public float glyphHeight;

        [@NonSerializable]
        public List<List<Edge>> edgeContours = new List<List<Edge>>();

        [NonSerializable]
        public List<Bezier> contours = new List<Bezier>();

        public float advanceWidth;
        public float leftSideOffset;
        public float tsb = 0;

        public Glyph()
        {

        }

        public void BuildEdges()
        {
            edgeContours.Clear();

            for (int c = 0; c < contours.Count; c++)
            {
                Bezier bezier = contours[c];
                List<Edge> edges = new List<Edge>();
                List<Bezier.Point> pts = bezier.points;
                int count = pts.Count;
                if (count == 0) continue;

                int i = 0;
                while (i < count)
                {
                    Bezier.Point current = pts[i];
                    Bezier.Point next = pts[(i + 1) % count];

                    if (current.isAnchor && next.isAnchor)
                    {
                        Edge e = new Edge();
                        e.p0 = current.pos;
                        e.control = (current.pos + next.pos) * 0.5f;
                        e.p1 = next.pos;
                        //e.color = current.color;
                        edges.Add(e);
                        i++;
                    }
                    else if (current.isAnchor && !next.isAnchor)
                    {
                        Bezier.Point afterNext = pts[(i + 2) % count];
                        if (afterNext.isAnchor)
                        {
                            Edge e = new Edge();
                            e.p0 = current.pos;
                            e.control = next.pos;
                            e.p1 = afterNext.pos;
                            //e.color = current.color;
                            edges.Add(e);
                            i += 2;
                        }
                        else
                        {
                            Vector2D<float> mid = (next.pos + afterNext.pos) * 0.5f;
                            Edge e = new Edge();
                            e.p0 = current.pos;
                            e.control = next.pos;
                            e.p1 = mid;
                            //e.color = current.color;
                            edges.Add(e);

                            pts.Insert(i + 2, new Bezier.Point(mid, true));
                            count++;
                            i += 2;
                        }
                    }
                    else
                    {
                        i++;
                    }
                }
                edgeContours.Add(edges);
            }
        }

        public void SetParams(short xMin, short xMax, short yMin, short yMax, float unitsPerEm)
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