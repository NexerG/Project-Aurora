using ArctisAurora.EngineWork.Renderer.UI;
using Assimp;
using Silk.NET.Maths;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using StbTrueTypeSharp;
using System.ComponentModel.Design;
using System.IO;
using System.Text;
using static ArctisAurora.EngineWork.AssetImporter;

namespace ArctisAurora.EngineWork
{
    internal unsafe class AssetImporter
    {
        public struct OffsetTable
        {
            public uint version;
            public ushort tableCount;
        }

        public struct TableEntry
        {
            public string name;
            public uint checksum;
            public uint offset;
            public uint length;
        }

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

        internal static Image<Rgba32> ImportFont(string fontName = "arial.ttf")
        {
            var fs = new FileStream("C:\\Windows\\Fonts\\" + fontName, FileMode.Open, FileAccess.Read);
            var reader = new BinaryReader(fs);

            OffsetTable offsetTable = new OffsetTable
            {
                version = ReadUInt32BE(reader),  // Big-endian
                tableCount = ReadUInt16BE(reader)
            };

            // Skip other fields (searchRange, entrySelector, rangeShift)
            fs.Position += 6;
            TableEntry[] tables = new TableEntry[offsetTable.tableCount];
            for (int i = 0; i < offsetTable.tableCount; i++)
            {
                tables[i] = new TableEntry
                {
                    name = new string(reader.ReadChars(4)),
                    checksum = ReadUInt32BE(reader),
                    offset = ReadUInt32BE(reader),
                    length = ReadUInt32BE(reader)
                };
            }

            Console.WriteLine($"Found {offsetTable.tableCount} of tables");
            for (int i = 0; i < offsetTable.tableCount; i++)
            {
                Console.WriteLine($"    TN: {tables[i].name} @loc {tables[i].offset}");
            }

            uint glyphCount = 0;
            foreach (var entry in tables)
            {
                if (entry.name == "maxp") // maxp
                {
                    reader.BaseStream.Position = entry.offset + 4;
                    glyphCount = ReadUInt16BE(reader);
                    break;
                }
            }

            TableEntry locaTable = tables.First(t => t.name == "loca"); // 'loca'
            TableEntry headTable = tables.First(t => t.name == "head"); // 'head'

            // 2. Read the indexToLocFormat flag from 'head' (determines 16/32-bit offsets)
            reader.BaseStream.Position = headTable.offset + 50; // Offset 50 in 'head'
            uint indexToLocFormat = ReadUInt16BE(reader); // 0 = uint16, 1 = uint32

            // 3. Read glyph offsets from 'loca'
            reader.BaseStream.Position = locaTable.offset;
            uint[] glyphOffsets = new uint[glyphCount + 1]; // +1 for sentinel value

            for (int i = 0; i <= glyphCount; i++)
            {
                if (indexToLocFormat == 0)
                    glyphOffsets[i] = (uint)(ReadUInt16BE(reader) * 2); // 16-bit → scale ×2
                else
                    glyphOffsets[i] = ReadUInt32BE(reader); // 32-bit
            }

            char targetChar = 'C';
            ushort charIndex = GetGlyphIndex(targetChar, reader, tables);
            Bezier b = GetGlyphOutline(charIndex, reader, tables, glyphOffsets);

            //Bezier b = new Bezier();
            //b.Test();

            Image<Rgba32> image = new Image<Rgba32>(128, 128);
            GenerateMSDF(b, ref image);

            image.Save("C:\\Users\\gmgyt\\Desktop\\msdf.png");

            return image;
        }

        private static ushort GetGlyphIndex(char character, BinaryReader reader, TableEntry[] tables)
        {
            // Find 'cmap' table (character to glyph mapping)
            var cmapTable = tables.First(t => t.name == "cmap"); // "cmap"
            reader.BaseStream.Position = cmapTable.offset;

            ushort version = ReadUInt16BE(reader);
            ushort numSubtables = ReadUInt16BE(reader);

            // Search for Unicode BMP subtable (PlatformID=3, EncodingID=1)
            for (int i = 0; i < numSubtables; i++)
            {
                ushort platformID = ReadUInt16BE(reader);
                ushort encodingID = ReadUInt16BE(reader);
                uint subtableOffset = ReadUInt32BE(reader);

                if (platformID == 3 && encodingID == 1) // Windows Unicode
                {
                    long savedPos = reader.BaseStream.Position;
                    reader.BaseStream.Position = cmapTable.offset + subtableOffset;

                    ushort format = ReadUInt16BE(reader);
                    if (format == 4) // Format 4 (segmented mapping)
                    {
                        ushort length = ReadUInt16BE(reader);
                        ushort language = ReadUInt16BE(reader);
                        ushort segCountX2 = ReadUInt16BE(reader);
                        ushort segCount = (ushort)(segCountX2 / 2);
                        ushort searchRange = ReadUInt16BE(reader);
                        ushort entrySelector = ReadUInt16BE(reader);
                        ushort rangeShift = ReadUInt16BE(reader);

                        // Read segmentation data
                        ushort[] endCodes = new ushort[segCount];
                        for (int j = 0; j < segCount; j++) endCodes[j] = ReadUInt16BE(reader);

                        ushort reservedPad = ReadUInt16BE(reader);

                        ushort[] startCodes = new ushort[segCount];
                        for (int j = 0; j < segCount; j++) startCodes[j] = ReadUInt16BE(reader);

                        short[] idDeltas = new short[segCount];
                        for (int j = 0; j < segCount; j++) idDeltas[j] = (short)ReadUInt16BE(reader);

                        long idRangeOffsetStart = reader.BaseStream.Position;
                        ushort[] idRangeOffsets = new ushort[segCount];
                        for (int j = 0; j < segCount; j++) idRangeOffsets[i] = ReadUInt16BE(reader);

                        // Find glyph index for the character
                        for (int j = 0; j < segCount; j++)
                        {
                            if (idRangeOffsets[j] == 0)
                            {
                                return (ushort)((character + idDeltas[j]) % 65536);
                            }
                            else
                            {
                                long offsetInWords = idRangeOffsets[j] / 2
                                    + (character - startCodes[j])
                                    - (segCount - j);

                                long glyphOffset = idRangeOffsetStart + j * 2 + offsetInWords * 2;

                                long saved = reader.BaseStream.Position;
                                reader.BaseStream.Position = glyphOffset;
                                ushort glyphIndex = ReadUInt16BE(reader);
                                reader.BaseStream.Position = saved;

                                if (glyphIndex != 0)
                                    return (ushort)((glyphIndex + idDeltas[i]) % 65536);
                                else
                                    return 0;
                            }
                        }
                    }
                    reader.BaseStream.Position = savedPos;
                }
            }
            throw new Exception($"Glyph for '{character}' not found!");
        }

        private static Bezier GetGlyphOutline(ushort glyphIndex, BinaryReader reader, TableEntry[] tables, uint[] glyphOffsets)
        {
            var glyfTable = tables.First(t => t.name == "glyf"); // "glyf"
            uint start = glyphOffsets[glyphIndex];
            uint end = glyphOffsets[glyphIndex + 1];

            if (start == end)
                return null; // Empty glyph (e.g., space)

            reader.BaseStream.Position = glyfTable.offset + start;

            // --- Read Glyph Header ---
            short numContours = ReadInt16BE(reader);
            if (numContours <= 0)
                return null; // Skip composite/empty glyphs

            short xMin = ReadInt16BE(reader);
            short yMin = ReadInt16BE(reader);
            short xMax = ReadInt16BE(reader);
            short yMax = ReadInt16BE(reader);

            float xK = xMax - xMin;
            float yK = yMax - yMin;

            // --- Read Contour End Points ---
            ushort[] endPts = new ushort[numContours];
            for (int i = 0; i < numContours; i++)
                endPts[i] = ReadUInt16BE(reader);

            ushort pointCount = (ushort)(endPts.Last() + 1);

            // --- Read Instructions (skip) ---
            ushort instructionLength = ReadUInt16BE(reader);
            reader.BaseStream.Position += instructionLength;

            // --- Read Flags and Coordinates ---
            byte[] flags = new byte[pointCount];
            Bezier bezier = new Bezier();

            // Read flags
            for (int i = 0; i < pointCount; i++)
            {
                flags[i] = reader.ReadByte();
                bezier.points.Add(new Bezier.Point());
                bezier.points[i].SetAnchor((flags[i] & 0x01) != 0); // Bit 0 = on-curve
                if ((flags[i] & 0x08) != 0)
                {
                    uint repeater = reader.ReadByte();
                    for (int j = 1; j <= repeater; j++)
                    {
                        flags[i + j] = flags[i];
                        bezier.points.Add(new Bezier.Point());
                        bezier.points[i + j].SetAnchor(bezier.points[i].isAnchor);
                    }
                    i += (int)repeater;
                }
            }

            // Read X coordinates
            short x = 0;
            for (int i = 0; i < pointCount; i++)
            {
                if ((flags[i] & 0x02) != 0) // X-byte delta
                {
                    byte delta =  reader.ReadByte();
                    x += ((flags[i] & 0x10) != 0) ? delta : (short)-delta;
                }
                else if ((flags[i] & 0x10) == 0) // X-word delta
                {
                    x += ReadInt16BE(reader);
                }
                bezier.points[i].SetX((float)x/xMax);
            }

            // Read Y coordinates
            short y = 0;
            for (int i = 0; i < pointCount; i++)
            {
                if ((flags[i] & 0x04) != 0) // Y-byte delta
                {
                    byte delta = reader.ReadByte();
                    y += ((flags[i] & 0x20) != 0) ? delta : (short)-delta;
                }
                else if ((flags[i] & 0x20) == 0) // Y-word delta
                {
                    y += ReadInt16BE(reader);
                }
                bezier.points[i].SetY((float)y / yMax);
            }

            return bezier;
        }

        private static short ReadInt16BE(BinaryReader reader) =>
            BitConverter.ToInt16(reader.ReadBytes(2).Reverse().ToArray(), 0);

        private static ushort ReadUInt16BE(BinaryReader reader) =>
            BitConverter.ToUInt16(reader.ReadBytes(2).Reverse().ToArray(), 0);

        private static uint ReadUInt32BE(BinaryReader reader) =>
            BitConverter.ToUInt32(reader.ReadBytes(4).Reverse().ToArray(), 0);

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

        private static void GenerateMSDF(Bezier b, ref Image<Rgba32> image)
        {
            int width = image.Width;
            int height = image.Height;
            float maxDist = 0.2f;// (float)image.Width / 4;

            // go through each pixel
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    //if(isInside)
                    //{
                    //
                    //}
                    //else
                    //{
                        //check the closest point for each axis
                        Vector2D<float> p = new Vector2D<float>((float)x / width, (float)y / height);
                        float horizontalD = 1 - HorizontalCheck(p, b, width, maxDist);
                        float verticalD = 1 - VerticalCheck(p, b, height, maxDist);
                        float diagonalD = 1 - DiagonalCheck(p, b, width, maxDist);
                        image[x, y] = new Rgba32(horizontalD, verticalD, diagonalD, 1);
                    //}
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

        private static float HorizontalCheck(Vector2D<float> pos, Bezier b, int width, float maxDist)
        {
            Vector2D<float> posHorizontalRight = new Vector2D<float>(1, pos.Y);
            Vector2D<float> posHorizontalLeft = new Vector2D<float>(0, pos.Y);

            float distance = maxDist;
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

        private static float VerticalCheck(Vector2D<float> pos, Bezier b, int height, float maxDist)
        {
            Vector2D<float> posVerticalRight = new Vector2D<float>(pos.X, 1);
            Vector2D<float> posVerticalLeft = new Vector2D<float>(pos.X, 0);

            float distance = maxDist;
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

        private static float DiagonalCheck(Vector2D<float> pos, Bezier b, int height, float maxDist)
        {
            Vector2D<float> upRight = new Vector2D<float>(pos.X + 1, pos.Y + 1);
            Vector2D<float> botRight = new Vector2D<float>(pos.X + 1, pos.Y - 1);
            Vector2D<float> botLeft = new Vector2D<float>(pos.X - 1, pos.Y - 1);
            Vector2D<float> topLeft = new Vector2D<float>(pos.X - 1, pos.Y + 1);

            float distance = maxDist;
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
