using Silk.NET.Maths;

namespace ArctisAurora.EngineWork.Renderer.UI
{
    internal class Bezier
    {
        internal class Point
        {
            public Vector2D<float> pos = new Vector2D<float>(1,1);
            public Vector3D<int> color = new Vector3D<int>(0, 0, 0);
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

        internal List<Point> points = new List<Point>();

        internal Bezier() { }

        internal Bezier(List<Point> points)
        {
            this.points = points;
        }

        internal void AddPoint(Point p)
        {
            points.Add(p);
        }

        internal void AddPoint(Vector2D<float> np, bool isAnchored)
        {
            Point point = new Point(np, isAnchored);
            points.Add(point);
        }

        internal void RemovePointAt(int i)
        {
            points.RemoveAt(i);
        }

        internal void Clear()
        {
            points.Clear();
        }
    }
}
