using ArctisAurora.Core.UISystem;
using Silk.NET.Maths;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ArctisAurora.Core.Generators
{
    public static class MTSDFGen
    {
        // Width of the distance range in atlas texels. The UI fragment shader hardcodes the same
        // number; an atlas baked at any other range renders with the wrong antialiasing.
        public const float PxRange = 4f;

        // Assigns the three MSDF channel masks around each contour, breaking at corners.
        public static void ColorEdges(Glyph glyph)
        {
            int colorIndex = 0;
            for (int i = 0; i < glyph.edgeContours.Count; i++)
            {
                List<Edge> edges = glyph.edgeContours[i];
                if (edges.Count == 0) continue;

                for (int j = 0; j < edges.Count; j++)
                {
                    Edge prev = edges[(j - 1 + edges.Count) % edges.Count];
                    Edge current = edges[j];
                    // Entry direction of current edge: B'(0) = 3(C0 - P0)
                    Vector2D<float> dirIn = current.c0 - current.p0;
                    // Exit direction of previous edge: B'(1) = 3(P1 - C1)
                    Vector2D<float> dirOut = prev.p1 - prev.c1;

                    float lenOut = Vector2D.Distance(dirOut, Vector2D<float>.Zero);
                    float lenIn = Vector2D.Distance(dirIn, Vector2D<float>.Zero);
                    if (lenOut > 1e-6f && lenIn > 1e-6f)
                    {
                        float directionDot = Vector2D.Dot(dirOut / lenOut, dirIn / lenIn);
                        if (directionDot < 0.5f)
                            colorIndex++;
                    }

                    switch (colorIndex % 3)
                    {
                        case 0:
                            edges[j].color = new Vector3D<int>(1, 1, 0);
                            break;
                        case 1:
                            edges[j].color = new Vector3D<int>(0, 1, 1);
                            break;
                        case 2:
                            edges[j].color = new Vector3D<int>(1, 0, 1);
                            break;
                    }
                }
                colorIndex++;
                bool isOfColor = edges[0].color == edges[edges.Count - 1].color;
                if(isOfColor)
                {
                    if(edges[edges.Count - 1].color == new Vector3D<int>(1, 1, 0))
                        edges[edges.Count - 1].color = new Vector3D<int>(0, 1, 1);

                    else if(edges[edges.Count - 1].color == new Vector3D<int>(0, 1, 1))
                        edges[edges.Count - 1].color = new Vector3D<int>(1, 0, 1);

                    else if(edges[edges.Count - 1].color == new Vector3D<int>(1, 0, 1))
                        edges[edges.Count - 1].color = new Vector3D<int>(0, 1, 1);
                }
            }
        }

        // Rasterizes one shape into a square cell of the atlas.
        public static void GenerateCell(Glyph glyph, Image<Rgba32> image, int startX, int startY, int cellSize, float pxRange)
        {
            float scale = MathF.Max(glyph.xMax - glyph.xMin, glyph.yMax - glyph.yMin);
            float normW = (glyph.xMax - glyph.xMin) / scale;
            float normH = (glyph.yMax - glyph.yMin) / scale;

            int pad = 1;
            int innerSize = cellSize - pad * 2;
            int spreadPx = innerSize / 8;
            float spreadU = (spreadPx / (float)innerSize) * normW;
            float spreadV = (spreadPx / (float)innerSize) * normH;

            // Contours are normalized against max(w, h), so the long axis always spans 1 and the
            // texel size below it is the one the shader's pxRange is measured in.
            float distanceFactor = innerSize / ((pxRange * 0.5f) * (1f + 2f * spreadPx / (float)innerSize));

            for (int x = 0; x < innerSize; x++)
            {
                for (int y = 0; y < innerSize; y++)
                {
                    float px = ((x + 0.5f) / innerSize) * (normW + 2 * spreadU) - spreadU;
                    float py = ((y + 0.5f) / innerSize) * (normH + 2 * spreadV) - spreadV;
                    Vector2D<float> p = new Vector2D<float>(px, py);

                    float redDist = Math.Clamp(GetClosestDistanceOfChannel(p, glyph, new Vector3D<int>(1, 0, 0)) * distanceFactor, -1, 1);
                    float greenDist = Math.Clamp(GetClosestDistanceOfChannel(p, glyph, new Vector3D<int>(0, 1, 0)) * distanceFactor, -1, 1);
                    float blueDist = Math.Clamp(GetClosestDistanceOfChannel(p, glyph, new Vector3D<int>(0, 0, 1)) * distanceFactor, -1, 1);
                    float trueDist = Math.Clamp(GetClosestDistanceOfChannel(p, glyph, new Vector3D<int>(1, 1, 1)) * distanceFactor, -1, 1);

                    redDist = redDist * 0.5f + 0.5f;
                    greenDist = greenDist * 0.5f + 0.5f;
                    blueDist = blueDist * 0.5f + 0.5f;
                    trueDist = trueDist * 0.5f + 0.5f;

                    image[startX + pad + x, startY + pad + y] = new Rgba32(redDist, greenDist, blueDist, trueDist);
                }
            }
        }

        public static float GetClosestDistanceOfChannel(Vector2D<float> p, Glyph glyph, Vector3D<int> channel)
        {
            if (glyph.edgeContours.Count == 0) return -1;

            float minDist = float.MaxValue;
            int contourIndex = 0;
            int edgeIndex = 0;
            for (int contour = 0; contour < glyph.edgeContours.Count; contour++)
            {
                List<Edge> edges = glyph.edgeContours[contour];
                for (int j = 0; j < edges.Count; j++)
                {
                    if (edges[j].color * channel == Vector3D<int>.Zero) continue;

                    float dist = ClosestTOnBezier(p, edges[j]);
                    if (minDist > dist)
                    {
                        minDist = dist;
                        contourIndex = contour;
                        edgeIndex = j;
                    }
                }
            }

            bool wn = ComputeWindingNumber(p, glyph) == 0;
            if (wn)
                minDist = -minDist;

            return minDist;
        }

        private static float ClosestTOnBezier(Vector2D<float> p, Edge edge)
        {
            // Phase 1: coarse sample to find bracket
            int samples = 32;
            float bestT = 0f;
            float bestDist = Vector2D.DistanceSquared(p, edge.p0);

            float d1Sq = Vector2D.DistanceSquared(p, edge.p1);
            if (d1Sq < bestDist) { bestDist = d1Sq; bestT = 1f; }

            for (int i = 1; i < samples; i++)
            {
                float t = (float)i / samples;
                float omt = 1f - t;
                Vector2D<float> pt = omt * omt * omt * edge.p0 + 3f * omt * omt * t * edge.c0
                    + 3f * omt * t * t * edge.c1 + t * t * t * edge.p1;
                float dSq = Vector2D.DistanceSquared(p, pt);
                if (dSq < bestDist) { bestDist = dSq; bestT = t; }
            }

            // Phase 2: Newton refinement (minimize |B(t) - p|^2)
            // f(t)  = dot(B(t)-p, B'(t))
            // f'(t) = dot(B'(t), B'(t)) + dot(B(t)-p, B''(t))
            float t2 = bestT;
            for (int iter = 0; iter < 4; iter++)
            {
                float omt = 1f - t2;

                // B(t)
                Vector2D<float> bt = omt * omt * omt * edge.p0 + 3f * omt * omt * t2 * edge.c0
                    + 3f * omt * t2 * t2 * edge.c1 + t2 * t2 * t2 * edge.p1;
                // B'(t) = 3(1-t)^2(C0-P0) + 6(1-t)t(C1-C0) + 3t^2(P1-C1)
                Vector2D<float> bt1 = 3f * omt * omt * (edge.c0 - edge.p0) + 6f * omt * t2 * (edge.c1 - edge.c0)
                    + 3f * t2 * t2 * (edge.p1 - edge.c1);
                // B''(t) = 6(1-t)(C1 - 2C0 + P0) + 6t(P1 - 2C1 + C0)
                Vector2D<float> bt2 = 6f * omt * (edge.c1 - 2f * edge.c0 + edge.p0)
                    + 6f * t2 * (edge.p1 - 2f * edge.c1 + edge.c0);

                Vector2D<float> diff = bt - p;
                float f = Vector2D.Dot(diff, bt1);
                float fPrime = Vector2D.Dot(bt1, bt1) + Vector2D.Dot(diff, bt2);

                if (MathF.Abs(fPrime) < 1e-10f) break;

                float step = f / fPrime;
                t2 -= step;
                t2 = Math.Clamp(t2, 0f, 1f);

                if (MathF.Abs(step) < 1e-6f) break;
            }

            // Compare refined result with best sample
            float omt2 = 1f - t2;
            Vector2D<float> refined = omt2 * omt2 * omt2 * edge.p0 + 3f * omt2 * omt2 * t2 * edge.c0
                + 3f * omt2 * t2 * t2 * edge.c1 + t2 * t2 * t2 * edge.p1;
            float refinedDist = Vector2D.DistanceSquared(p, refined);
            if (refinedDist < bestDist) { bestDist = refinedDist; }

            return MathF.Sqrt(bestDist);
        }

        private static float[] SolveCubic(float a, float b, float c, float d)
        {
            // Handle degenerate cases
            if (MathF.Abs(a) < 1e-6f)
            {
                return SolveQuadratic(b, c, d);
            }

            // Normalize
            float invA = 1f / a;
            b *= invA;
            c *= invA;
            d *= invA;

            // Depressed cubic: t^3 + pt + q = 0  (substitute t = x - b/3)
            float b2 = b * b;
            float p = c - b2 / 3f;
            float q = d - b * c / 3f + 2f * b2 * b / 27f;
            float shift = b / 3f;

            float disc = q * q / 4f + p * p * p / 27f;

            if (disc > 1e-6f)
            {
                // One real root
                float sqrtDisc = MathF.Sqrt(disc);
                float u = MathF.Cbrt(-q / 2f + sqrtDisc);
                float v = MathF.Cbrt(-q / 2f - sqrtDisc);
                return new float[] { u + v - shift };
            }
            else if (MathF.Abs(disc) <= 1e-6f)
            {
                // Two real roots (one double)
                float u = MathF.Cbrt(-q / 2f);
                return new float[] { 2f * u - shift, -u - shift };
            }
            else
            {
                // Three real roots (Vieta's trigonometric method)
                float r = MathF.Sqrt(-p * p * p / 27f);
                float theta = MathF.Acos(Math.Clamp(-q / (2f * r), -1f, 1f));
                float m = 2f * MathF.Cbrt(r);

                return new float[]
                {
            m * MathF.Cos(theta / 3f) - shift,
            m * MathF.Cos((theta + 2f * MathF.PI) / 3f) - shift,
            m * MathF.Cos((theta + 4f * MathF.PI) / 3f) - shift
                };
            }
        }

        private static float[] SolveQuadratic(float a, float b, float c)
        {
            if (MathF.Abs(a) < 1e-6f)
            {
                if (MathF.Abs(b) < 1e-6f) return Array.Empty<float>();
                return new float[] { -c / b };
            }

            float disc = b * b - 4f * a * c;
            if (disc < 0) return Array.Empty<float>();

            float sqrtDisc = MathF.Sqrt(disc);
            float inv2a = 1f / (2f * a);
            return new float[]
            {
        (-b + sqrtDisc) * inv2a,
        (-b - sqrtDisc) * inv2a
            };
        }

        private static int ComputeWindingNumber(Vector2D<float> p, Glyph glyph)
        {
            int winding = 0;

            for (int c = 0; c < glyph.edgeContours.Count; c++)
            {
                List<Edge> edges = glyph.edgeContours[c];
                for (int e = 0; e < edges.Count; e++)
                {
                    Edge edge = edges[e];

                    // Cubic bezier: B(t) = (1-t)^3*P0 + 3(1-t)^2t*C0 + 3(1-t)t^2*C1 + t^3*P1
                    // Solve B_y(t) = p.Y for t, as ay*t^3 + by*t^2 + cy*t + dy0 = 0

                    float ay = -edge.p0.Y + 3f * edge.c0.Y - 3f * edge.c1.Y + edge.p1.Y;
                    float by = 3f * edge.p0.Y - 6f * edge.c0.Y + 3f * edge.c1.Y;
                    float cy = -3f * edge.p0.Y + 3f * edge.c0.Y;
                    float dy0 = edge.p0.Y - p.Y;

                    float[] roots = SolveCubic(ay, by, cy, dy0);

                    for (int i = 0; i < roots.Length; i++)
                    {
                        float t = roots[i];
                        if (t < 0f || t >= 1f) continue;

                        // X position of curve at this t
                        float omt = 1f - t;
                        float bx = omt * omt * omt * edge.p0.X + 3f * omt * omt * t * edge.c0.X
                            + 3f * omt * t * t * edge.c1.X + t * t * t * edge.p1.X;

                        // Only count crossings to the right of p (ray casting rightward)
                        if (bx <= p.X) continue;

                        // Curve's Y derivative at t: B'_y(t) = 3ay*t^2 + 2by*t + cy
                        float dy = 3f * ay * t * t + 2f * by * t + cy;

                        if (dy > 0f)
                            winding++;
                        else if (dy < 0f)
                            winding--;
                    }
                }
            }
            return winding;
        }
    }
}
