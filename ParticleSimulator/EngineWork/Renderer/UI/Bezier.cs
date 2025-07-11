using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Silk.NET.Maths;

namespace ArctisAurora.EngineWork.Renderer.UI
{
    internal class Bezier
    {
        internal struct Point
        {
            public Vector2D<float> pos;
            public bool isAnchor;
            public bool isFill;

            public Point(Vector2D<float> np)
            {
                pos = np;
                isAnchor = false;
            }

            public Point(Vector2D<float> np, bool isAnchored)
            {
                pos = np;
                isAnchor = isAnchored;
            }
        }

        internal List<Point> points;

        internal Bezier() { }

        internal Bezier(List<Point> points) 
        {
            this.points = points;
        }

        internal void AddPoint(Point p)
        {
            points.Add(p);
        }

        internal void RemovePointAt(int i)
        {
            points.RemoveAt(i);
        }

        internal void Clear()
        {
            points.Clear();
        }

        internal void Test()
        {
            Point p1 = new Point(new Vector2D<float>(0.5f, 0.0f), true);
            Point p2 = new Point(new Vector2D<float>(1.0f, 0.5f), true);
            Point p3 = new Point(new Vector2D<float>(0.5f, 1.0f), true);
            Point p4 = new Point(new Vector2D<float>(0.0f, 0.5f), true);

            points = new List<Point>();

            points.Add(p1);
            points.Add(p2);
            points.Add(p3);
            points.Add(p4);
        }
    }
}
