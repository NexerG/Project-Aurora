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
        internal class Point
        {
            public Vector2D<float> pos = new Vector2D<float>(1,1);
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

        /*internal Bezier(int listLength)
        {
            this.points = points;
        }*/

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

        internal void Test()
        {
            Point p1 = new Point(new Vector2D<float>(0.5f, 0.0f), true);
            Point p2 = new Point(new Vector2D<float>(1.0f, 0.5f), true);
            Point p3 = new Point(new Vector2D<float>(0.5f, 1.0f), true);
            Point p4 = new Point(new Vector2D<float>(0.0f, 0.5f), true);

            points.Add(p1);
            points.Add(p2);
            points.Add(p3);
            points.Add(p4);
        }
    }
}
