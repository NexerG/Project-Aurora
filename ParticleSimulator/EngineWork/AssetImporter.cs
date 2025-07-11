using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp;
using StbTrueTypeSharp;
using ArctisAurora.EngineWork.Renderer.UI;
using System.ComponentModel.Design;
using Assimp;
using Silk.NET.Maths;

namespace ArctisAurora.EngineWork
{
    internal unsafe class AssetImporter
    {
        internal static Image<Rgba32> GetLetter(string fontPath = "C:\\Windows\\Fonts\\Arial.ttf", char character = 'A', int textureSize = 64, float fontSize = 128, float spread = 4)
        {
            byte[] fontData = File.ReadAllBytes(fontPath);
            var fontInfo = new StbTrueType.stbtt_fontinfo();
            fixed (byte* fontDataPtr = fontData)
            {
                if (StbTrueType.stbtt_InitFont(fontInfo, fontDataPtr, 0) == 0)
                {
                    throw new Exception("Failed to load font");
                }
            }
            float scale = StbTrueType.stbtt_ScaleForPixelHeight(fontInfo, fontSize);

            int glyphIndex = StbTrueType.stbtt_FindGlyphIndex(fontInfo, character);

            int advanceWidth, leftSideBearing;
            StbTrueType.stbtt_GetGlyphHMetrics(fontInfo, glyphIndex, &advanceWidth, &leftSideBearing);

            int x0, y0, x1, y1;
            StbTrueType.stbtt_GetGlyphBitmapBox(fontInfo, glyphIndex, scale, scale, &x0, &y0, &x1, &y1);

            int glyphWidth = x1 - x0;
            int glyphHeight = y1 - y0;

            int padding = (int)(spread * 2);
            int sdfWidth = glyphWidth + padding;
            int sdfHeight = glyphHeight + padding;

            byte[] flippedMap = new byte[sdfWidth * sdfHeight];
            byte[] bitmap = new byte[sdfWidth * sdfHeight];

            fixed (byte* bitmapPtr = flippedMap)
            {
                StbTrueType.stbtt_MakeGlyphBitmap(
                    fontInfo,
                    bitmapPtr,
                    glyphWidth,
                    glyphHeight,
                    glyphWidth,
                    1f,
                    1f,
                    glyphIndex);
            }

            for (int y = 0; y < glyphHeight; y++)
            {
                int srcY = glyphHeight - 1 - y;
                Buffer.BlockCopy(
                    flippedMap, srcY * glyphWidth,
                    bitmap, y * glyphWidth,
                    glyphWidth);
            }

            byte[] msdf = new byte[sdfWidth * sdfHeight * 3];
            GenerateSDFPerAxis(msdf, 0, textureSize, textureSize, bitmap, glyphWidth, glyphHeight, padding, IsVerticalEdge);
            GenerateSDFPerAxis(msdf, 1, textureSize, textureSize, bitmap, glyphWidth, glyphHeight, padding, IsHorizontalEdge);
            GenerateSDFPerAxis(msdf, 2, textureSize, textureSize, bitmap, glyphWidth, glyphHeight, padding, IsDiagonalEdge);

            var image = new Image<Rgba32>(sdfWidth, sdfHeight);

            for (int y = 0; y < sdfHeight; y++)
            {
                for (int x = 0; x < sdfWidth; x++)
                {
                    int idx = (y * sdfWidth + x) * 3;

                    // Get MSDF values (0-255)
                    byte r = msdf[idx];
                    byte g = msdf[idx + 1];
                    byte b = msdf[idx + 2];

                    // Store in Image<Rgba32> (copy RGB, set alpha to 255)
                    image[x, y] = new Rgba32(r, g, b, 255);
                }
            }

            image.Save("C:\\Users\\gmgyt\\Desktop\\A_MSDF.png");

            return image;
        }

        internal static Image<Rgba32> ImportFont(string fontPath = "C:\\Windows\\Fonts\\Arial.ttf", char character = 'A', int textureSize = 128, float fontSize = 128, float spread = 1)
        {
            byte[] fontData = File.ReadAllBytes(fontPath);
            var fontInfo = new StbTrueType.stbtt_fontinfo();
            fixed (byte* fontDataPtr = fontData)
            {
                if (StbTrueType.stbtt_InitFont(fontInfo, fontDataPtr, 0) == 0)
                {
                    throw new Exception("Failed to load font");
                }
            }

            float scale = StbTrueType.stbtt_ScaleForPixelHeight(fontInfo, fontSize);

            int glyphIndex = StbTrueType.stbtt_FindGlyphIndex(fontInfo, character);

            int advanceWidth, leftSideBearing;
            StbTrueType.stbtt_GetGlyphHMetrics(fontInfo, glyphIndex, &advanceWidth, &leftSideBearing);

            int x0, y0, x1, y1;
            StbTrueType.stbtt_GetGlyphBitmapBox(fontInfo, glyphIndex, scale, scale, &x0, &y0, &x1, &y1);

            int glyphWidth = x1 - x0;
            int glyphHeight = y1 - y0;

            int padding = (int)(spread * 2);
            int sdfWidth = glyphWidth + padding;
            int sdfHeight = glyphHeight + padding;

            byte[] bitmap = new byte[sdfWidth * sdfHeight];
            byte[] flippedBitmap = new byte[sdfWidth * sdfHeight];
            byte[] sdf = new byte[sdfWidth * sdfHeight];

            fixed (byte* bitmapPtr = flippedBitmap)
            {
                StbTrueType.stbtt_MakeGlyphBitmap(
                    fontInfo,
                    bitmapPtr,
                    glyphWidth,
                    glyphHeight,
                    glyphWidth,
                    scale,
                    scale,
                    glyphIndex);
            }

            for (int y = 0; y < glyphHeight; y++)
            {
                int srcY = glyphHeight - 1 - y;
                Buffer.BlockCopy(
                    flippedBitmap, srcY * glyphWidth,
                    bitmap, y * glyphWidth,
                    glyphWidth);
            }

            GenerateSDF(sdf, sdfWidth, sdfHeight,
                        bitmap, glyphWidth, glyphHeight,
                        spread);

            var image = new Image<Rgba32>(sdfWidth, sdfHeight);

            for (int y = 0; y < sdfHeight; y++)
            {
                for (int x = 0; x < sdfWidth; x++)
                {
                    byte value = sdf[y * sdfWidth + x];
                    image[x, y] = new Rgba32(value, value, value, 255);
                }
            }

            return image;
        }

        internal static Image<Rgba32> ImportFont()
        {
            Bezier b = new Bezier();
            b.Test();

            Image<Rgba32> image = new Image<Rgba32>(128, 128);
            GenerateSDFOnAxis(b, ref image);

            return image;
        }

        private static void GenerateSDF(byte[] output, int outWidth, int outHeight, byte[] input, int inWidth, int inHeight, float spread)
        {
            // This is a simplified SDF generation algorithm
            // For production, consider using a more optimized approach

            float maxDist = spread;
            float scale = 1.0f / maxDist;

            for (int y = 0; y < outHeight; y++)
            {
                for (int x = 0; x < outWidth; x++)
                {
                    float minDist = maxDist;

                    // Sample input coordinates (centered in output)
                    int sx = x - (outWidth - inWidth) / 2;
                    int sy = y - (outHeight - inHeight) / 2;

                    bool inside = (sx >= 0 && sx < inWidth && sy >= 0 && sy < inHeight) &&
                                  (input[sy * inWidth + sx] > 128);

                    // Simple brute-force SDF calculation
                    for (int j = 0; j < inHeight; j++)
                    {
                        for (int i = 0; i < inWidth; i++)
                        {
                            if ((input[j * inWidth + i] > 128) != inside)
                            {
                                float dx = (sx - i);
                                float dy = (sy - j);
                                float dist = (float)Math.Sqrt(dx * dx + dy * dy);
                                if (dist < minDist) minDist = dist;
                            }
                        }
                    }

                    float signedDist = inside ? minDist : -minDist;
                    float value = signedDist * scale * 0.5f + 0.5f;
                    value = Math.Clamp(value, 0, 1);
                    output[y * outWidth + x] = (byte)(value * 255);
                }
            }
        }

        private static void GenerateSDFOnAxis(Bezier b, ref Image<Rgba32> image)
        {
            int width = image.Width;
            int height = image.Height;

            // go through each pixel
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    //check the closest point for each axis
                    Vector2D<float> p = new Vector2D<float>((float)x / width, (float)y / height);
                    float horizontalD = HorizontalCheck(p, b, width);
                    float verticalD = VerticalCheck(p, b, height);
                    float diagonalD = DiagonalCheck(p, b, width);
                    image[x, y] = new Rgba32(horizontalD, verticalD, diagonalD, 1);
                }
            }
        }

        private static void GenerateSDFPerAxis(
            byte[] output, int channel, int outWidth, int outHeight,
            byte[] input, int inWidth, int inHeight, int padding,
            Func<byte[], int, int, int, int, bool> edgeDetector)
        {
            float maxDist = padding;
            float scale = 1f / maxDist;

            for (int y = 0; y < outHeight; y++)
            {
                for (int x = 0; x < outWidth; x++)
                {
                    float minDist = maxDist;

                    // Sample input coordinates (centered in output)
                    int sx = x - padding;
                    int sy = y - padding;

                    bool inside = (sx >= 0 && sx < inWidth && sy >= 0 && sy < inHeight) &&
                                (input[sy * inWidth + sx] > 128);

                    // Find nearest edge of the specified type
                    for (int j = 0; j < inHeight; j++)
                    {
                        for (int i = 0; i < inWidth; i++)
                        {
                            if ((input[j * inWidth + i] > 128) != inside &&
                                edgeDetector(input, inWidth, inHeight, i, j))
                            {
                                float dx = (sx - i);
                                float dy = (sy - j);
                                float dist = (float)Math.Sqrt(dx * dx + dy * dy);
                                if (dist < minDist) minDist = dist;
                            }
                        }
                    }

                    float signedDist = inside ? minDist : -minDist;
                    float value = signedDist * scale * 0.5f + 0.5f;
                    value = Math.Clamp(value, 0, 1);
                    output[(y * outWidth + x) * 3 + channel] = (byte)(value * 255);
                }
            }
        }

        public unsafe static void GenerateMSDF(byte[] output, byte[] input, int w, int h)
        {
            fixed (byte* inPtr = input, outPtr = output)
            {
                // Generate 3 SDFs (red/green/blue channels)
                for (int ch = 0; ch < 3; ch++)
                {
                    for (int y = 0; y < h; y++)
                    {
                        for (int x = 0; x < w; x++)
                        {
                            float minDist = FindSignedDistance(
                                input, w, h,
                                x, y,
                                edgeType: ch // 0=horiz, 1=vert, 2=diagonal edges
                            );
                            output[(y * w + x) * 3 + ch] = (byte)((minDist * 0.5f + 0.5f) * 255);
                        }
                    }
                }
            }
        }

        private static float FindSignedDistance(byte[] bitmap, int w, int h, int x, int y, int edgeType)
        {
            // Simplified: Finds distance to nearest edge of specified type
            // (Real MSDF uses more sophisticated edge classification)
            bool inside = bitmap[y * w + x] > 128;
            float minDist = float.MaxValue;

            for (int j = 0; j < h; j++)
            {
                for (int i = 0; i < w; i++)
                {
                    if ((bitmap[j * w + i] > 128) != inside &&
                        IsEdgeType(bitmap, w, h, i, j, edgeType))
                    {
                        float dx = x - i;
                        float dy = y - j;
                        float dist = MathF.Sqrt(dx * dx + dy * dy);
                        minDist = Math.Min(minDist, inside ? dist : -dist);
                    }
                }
            }
            return minDist;
        }

        private static bool IsEdgeType(byte[] bitmap, int w, int h, int x, int y, int edgeType)
        {
            // Classify edges (simplified approximation)
            if (edgeType == 0) return IsHorizontalEdge(bitmap, w, h, x, y);
            if (edgeType == 1) return IsVerticalEdge(bitmap, w, h, x, y);
            return IsDiagonalEdge(bitmap, w, h, x, y);
        }

        private static bool IsHorizontalEdge(byte[] bitmap, int w, int h, int x, int y)
        {
            // Current pixel's alpha (1=inside, 0=outside)
            bool current = bitmap[y * w + x] > 128;

            // Check top neighbor (if exists)
            if (y > 0 && (bitmap[(y - 1) * w + x] > 128) != current)
                return true;

            // Check bottom neighbor (if exists)
            if (y < h - 1 && (bitmap[(y + 1) * w + x] > 128) != current)
                return true;

            return false;
        }

        private static bool IsVerticalEdge(byte[] bitmap, int w, int h, int x, int y)
        {
            bool current = bitmap[y * w + x] > 128;

            // Check left neighbor
            if (x > 0 && (bitmap[y * w + (x - 1)] > 128) != current)
                return true;

            // Check right neighbor
            if (x < w - 1 && (bitmap[y * w + (x + 1)] > 128) != current)
                return true;

            return false;
        }

        private static bool IsDiagonalEdge(byte[] bitmap, int w, int h, int x, int y)
        {
            bool current = bitmap[y * w + x] > 128;

            // Check top-left
            if (x > 0 && y > 0 && (bitmap[(y - 1) * w + (x - 1)] > 128) != current)
                return true;

            // Check top-right
            if (x < w - 1 && y > 0 && (bitmap[(y - 1) * w + (x + 1)] > 128) != current)
                return true;

            // Check bottom-left
            if (x > 0 && y < h - 1 && (bitmap[(y + 1) * w + (x - 1)] > 128) != current)
                return true;

            // Check bottom-right
            if (x < w - 1 && y < h - 1 && (bitmap[(y + 1) * w + (x + 1)] > 128) != current)
                return true;

            return false;
        }

        // -----------------------

        private static float HorizontalCheck(Vector2D<float> pos, Bezier b, int width)
        {
            Vector2D<float> posHorizontalRight = new Vector2D<float>(1, pos.Y);
            Vector2D<float> posHorizontalLeft = new Vector2D<float>(0, pos.Y);

            float distance = width;
            for (int i = 0; i < b.points.Count; i++)
            {
                if (CheckIntersect(pos, posHorizontalRight, b.points[i].pos, b.points[(i + 1) % b.points.Count].pos, out Vector2D<float> intersect))
                {
                    float localD = MathF.Abs(pos.X - intersect.X);
                    if (localD < distance)
                    {
                        distance = localD;
                    }
                }
            }

            for (int i = 0; i < b.points.Count; i++)
            {
                if (CheckIntersect(pos, posHorizontalLeft, b.points[i].pos, b.points[(i + 1) % b.points.Count].pos, out Vector2D<float> intersect))
                {
                    float localD = MathF.Abs(pos.X - intersect.X);
                    if (localD < distance)
                    {
                        distance = localD;
                    }
                }
            }

            return distance;
        }

        private static float VerticalCheck(Vector2D<float> pos, Bezier b, int height)
        {
            Vector2D<float> posVerticalRight = new Vector2D<float>(pos.X, 1);
            Vector2D<float> posVerticalLeft = new Vector2D<float>(pos.X, 0);

            float distance = height;
            for (int i = 0; i < b.points.Count; i++)
            {
                if (CheckIntersect(pos, posVerticalRight, b.points[i].pos, b.points[(i + 1) % b.points.Count].pos, out Vector2D<float> intersect))
                {
                    float localD = MathF.Abs(pos.X - intersect.X);
                    if (localD < distance)
                    {
                        distance = localD;
                    }
                }
            }

            for (int i = 0; i < b.points.Count; i++)
            {
                if (CheckIntersect(pos, posVerticalLeft, b.points[i].pos, b.points[(i + 1) % b.points.Count].pos, out Vector2D<float> intersect))
                {
                    float localD = MathF.Abs(pos.X - intersect.X);
                    if (localD < distance)
                    {
                        distance = localD;
                    }
                }
            }

            return distance;
        }

        private static float DiagonalCheck(Vector2D<float> pos, Bezier b, int height)
        {
            Vector2D<float> upRight = new Vector2D<float>(pos.X + 1, pos.Y + 1);
            Vector2D<float> botRight = new Vector2D<float>(pos.X + 1, pos.Y - 1);
            Vector2D<float> botLeft = new Vector2D<float>(pos.X - 1, pos.Y - 1);
            Vector2D<float> topLeft = new Vector2D<float>(pos.X - 1, pos.Y + 1);

            float distance = height;
            for (int i = 0; i < b.points.Count; i++)
            {
                if (CheckIntersect(pos, upRight, b.points[i].pos, b.points[(i + 1) % b.points.Count].pos, out Vector2D<float> intersect))
                {
                    float localD = MathF.Abs(pos.X - intersect.X);
                    if (localD < distance)
                    {
                        distance = localD;
                    }
                }
            }

            for (int i = 0; i < b.points.Count; i++)
            {
                if (CheckIntersect(pos, botRight, b.points[i].pos, b.points[(i + 1) % b.points.Count].pos, out Vector2D<float> intersect))
                {
                    float localD = MathF.Abs(pos.X - intersect.X);
                    if (localD < distance)
                    {
                        distance = localD;
                    }
                }
            }

            for (int i = 0; i < b.points.Count; i++)
            {
                if (CheckIntersect(pos, botLeft, b.points[i].pos, b.points[(i + 1) % b.points.Count].pos, out Vector2D<float> intersect))
                {
                    float localD = MathF.Abs(pos.X - intersect.X);
                    if (localD < distance)
                    {
                        distance = localD;
                    }
                }
            }

            for (int i = 0; i < b.points.Count; i++)
            {
                if (CheckIntersect(pos, topLeft, b.points[i].pos, b.points[(i + 1) % b.points.Count].pos, out Vector2D<float> intersect))
                {
                    float localD = MathF.Abs(pos.X - intersect.X);
                    if (localD < distance)
                    {
                        distance = localD;
                    }
                }
            }

            return distance;
        }

        private static bool CheckIntersect(Vector2D<float> p1, Vector2D<float> p2, Vector2D<float> p3, Vector2D<float> p4, out Vector2D<float> intersect)
        {
            intersect = Vector2D<float>.Zero;

            Vector2D<float> l1 = p2 - p1;
            Vector2D<float> l2 = p4 - p3;

            Vector2D<float> l3 = p3 - p1;

            float cross = Cross(l1, l2);
            float intCross = Cross(l3, l1);

            if (MathF.Abs(cross) < float.Epsilon)
            {
                return false;
            }

            float t = Cross(l3, l2) / cross;
            float u = Cross(l3, l1) / cross;

            if (t >= 0 && t <= 1 && u >= 0 && u <= 1)
            {
                intersect = p1 + t * l1;
                return true;
            }

            return false;
        }

        private static float Cross(Vector2D<float> a, Vector2D<float> b)
        {
            return a.X * b.Y - a.Y * b.X;
        }
    }
}
