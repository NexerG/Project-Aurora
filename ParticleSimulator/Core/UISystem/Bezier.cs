using ArctisAurora.Core.Filing.Serialization;
using Silk.NET.Maths;

namespace ArctisAurora.Core.UISystem
{
    [@NonSerializable]
    public class Edge
    {
        public Vector2D<float> p0;     // start anchor
        public Vector2D<float> control; // control point
        public Vector2D<float> p1;     // end anchor
        public Vector3D<int> color;
    }

    [@Serializable]
    public class Bezier
    {
        [@Serializable]
        public class Point
        {
            public Vector2D<float> pos = new Vector2D<float>(1, 1);
            public bool isAnchor;
            public bool isFill;

            public Point() { }

            public Point(Vector2D<float> np)
            {
                pos = np;
                isAnchor = true;
            }

            public Point(Vector2D<float> np, bool isAnchored)
            {
                pos = np;
                isAnchor = isAnchored;
            }

            public void SetAnchor(bool state)
            {
                isAnchor = state;
            }

            public void SetX(float x)
            {
                pos.X = x;
            }

            public void SetX(int x)
            {
                pos.X = x;
            }

            public void SetY(float y)
            {
                pos.Y = y;
            }

            public void SetY(int y)
            {
                pos.Y = y;
            }
        }

        public List<Point> points = new List<Point>();

        public Bezier() { }

        public Bezier(List<Point> points)
        {
            this.points = points;
        }

        public void AddPoint(Point p)
        {
            points.Add(p);
        }

        public void AddPoint(Vector2D<float> np, bool isAnchored)
        {
            Point point = new Point(np, isAnchored);
            points.Add(point);
        }

        public void RemovePointAt(int i)
        {
            points.RemoveAt(i);
        }

        public void Clear()
        {
            points.Clear();
        }
    }
}
