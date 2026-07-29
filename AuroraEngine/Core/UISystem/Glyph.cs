using ArctisAurora.Core.Filing.Serialization;
using Silk.NET.Maths;

namespace ArctisAurora.Core.UISystem
{
    [@Serializable]
    public class Glyph
    {
        public short xMin, yMin, xMax, yMax;
        public float glyphWidth = 1;
        public float glyphHeight = 1;

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

                // Expand first: TrueType leaves the on-curve point between two consecutive control
                // points implied, at their midpoint. Inserting those up front into a copy means the
                // edge walk below never has to mutate the list it is iterating — the old in-place
                // Insert(i + 2) ran off the end whenever such a pair straddled the contour's wrap.
                List<Bezier.Point> expanded = new List<Bezier.Point>(count + 4);
                for (int i = 0; i < count; i++)
                {
                    Bezier.Point current = pts[i];
                    Bezier.Point next = pts[(i + 1) % count];
                    expanded.Add(current);
                    if (!current.isAnchor && !next.isAnchor)
                        expanded.Add(new Bezier.Point((current.pos + next.pos) * 0.5f, true));
                }

                int total = expanded.Count;
                for (int i = 0; i < total; i++)
                {
                    Bezier.Point current = expanded[i];
                    if (!current.isAnchor) continue;   // control points are consumed by their edge

                    Bezier.Point next = expanded[(i + 1) % total];
                    Edge e = new Edge();
                    e.p0 = current.pos;
                    if (next.isAnchor)
                    {
                        // Straight run: a quadratic whose control sits on the line is the line.
                        e.control = (current.pos + next.pos) * 0.5f;
                        e.p1 = next.pos;
                    }
                    else
                    {
                        // No two control points are adjacent after expansion, so this is on-curve.
                        e.control = next.pos;
                        e.p1 = expanded[(i + 2) % total].pos;
                    }
                    edges.Add(e);
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