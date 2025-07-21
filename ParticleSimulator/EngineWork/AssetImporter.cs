using ArctisAurora.EngineWork.Renderer.UI;
using Silk.NET.Maths;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Windows.ApplicationModel.VoiceCommands;
using Image = SixLabors.ImageSharp.Image;

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

        internal static Image<Rgba32> TestLetterA()
        {
            Image<Rgba32> image = Image.Load<Rgba32>("C:\\Users\\gmgyt\\Downloads\\testas.png");

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

            char targetChar = 'G';
            ushort charIndex = GetGlyphIndex(targetChar, reader, tables);
            Bezier b = GetGlyphOutline(charIndex, reader, tables, glyphOffsets);

            Image<Rgba32> image = new Image<Rgba32>(128, 128);
            GenerateMSDF(b, ref image, 255f);

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

            //SubdivideEdges(bezier.points, 3);

            // set edge colors
            int colorindex = 0;
            for (int i = 0; i < pointCount; i++)
            {
                Vector2D<float> p1 = bezier.points[i].pos;
                Vector2D<float> p2 = bezier.points[(i + 1) % pointCount].pos;
                Vector2D<float> p3 = bezier.points[(i + 2) % pointCount].pos;

                float angle = CalcAngleInSegment(p1, p2, p3);
                if (angle > 1.13)
                {
                    colorindex++;
                }
                switch(colorindex % 3)
                {
                    case 0:
                        bezier.points[i].color = new Vector3D<int>(1,1,0);
                        break;
                    case 1:
                        bezier.points[i].color = new Vector3D<int>(0,1,1);
                        break;
                    case 2:
                        bezier.points[i].color = new Vector3D<int>(1,0,1);
                        break;
                }
            }
            Console.WriteLine(colorindex);
            return bezier;
        }

        private static short ReadInt16BE(BinaryReader reader) =>
            BitConverter.ToInt16(reader.ReadBytes(2).Reverse().ToArray(), 0);

        private static ushort ReadUInt16BE(BinaryReader reader) =>
            BitConverter.ToUInt16(reader.ReadBytes(2).Reverse().ToArray(), 0);

        private static uint ReadUInt32BE(BinaryReader reader) =>
            BitConverter.ToUInt32(reader.ReadBytes(4).Reverse().ToArray(), 0);

        private static void GenerateDSDF(Bezier b, ref Image<Rgba32> image)
        {
            int width = image.Width;
            int height = image.Height;

            // go through each pixel
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    Vector2D<float> p = new Vector2D<float>((float)x / width, (float)y / height);
                    float horizontalD = HorizontalCheck(p, b, width);
                    float verticalD = VerticalCheck(p, b, height);
                    float diagonalD = DiagonalCheck(p, b, width);
                    
                    if (IsInsidePolygon(p, b.points))
                    {
                        horizontalD = -horizontalD;
                        verticalD = -verticalD;
                        diagonalD = -diagonalD;
                    }

                    horizontalD = horizontalD * 0.5f + 0.5f;
                    verticalD = verticalD * 0.5f + 0.5f;
                    diagonalD = diagonalD * 0.5f + 0.5f;


                    image[x, y] = new Rgba32(horizontalD, verticalD, diagonalD, 1f);
                }
            }
        }

        private static void GenerateMSDF(Bezier b, ref Image<Rgba32> image, float distanceFactor)
        {
            int width = image.Width;
            int height = image.Height;

            // go through each pixel
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    Vector2D<float> p = new Vector2D<float>((x + 0.5f) / width, (y + 0.5f) / height);
                    float redDist = Math.Clamp(GetClosestDistance(p, b, new Vector3D<int>(1, 0, 0)) * distanceFactor, -1 , 1);
                    float greenDist = Math.Clamp(GetClosestDistance(p, b, new Vector3D<int>(0, 1, 0)) * distanceFactor, -1, 1);
                    float blueDist = Math.Clamp(GetClosestDistance(p, b, new Vector3D<int>(0, 0, 1)) * distanceFactor, -1, 1);

                    redDist = redDist * 0.5f + 0.5f;
                    greenDist = greenDist * 0.5f + 0.5f;
                    blueDist = blueDist * 0.5f + 0.5f;
            
            
                    image[x, y] = new Rgba32(redDist, greenDist, blueDist, 1f);
                    //image[x, height - y - 1] = new Rgba32(redDist, greenDist, blueDist, 1f);
                }
            }
        }

        private static void SubdivideEdges(List<Bezier.Point> points, int count)
        {
            List<Bezier.Point> original = new List<Bezier.Point>(points);
            points.Clear();

            for (int i = 0; i < original.Count; i++)
            {
                Vector2D<float> p1 = original[i].pos;
                Vector2D<float> p2 = original[(i + 1) % original.Count].pos;

                //points.Add(original[i]);

                Vector2D<float> segementLength = (p2 - p1) / count;

                for (int j = 0; j < count; j++)
                {
                    Vector2D<float> newPos = p1 + segementLength * j;
                    points.Add(new Bezier.Point(newPos, original[i].isAnchor));
                }
            }
        }

        private static float GetClosestDistance(Vector2D<float> p, Bezier bezier, Vector3D<int> channel)
        {
            float minDist = float.MaxValue;
            int index = 0;
            for (int i = 0; i < bezier.points.Count; i++)
            {
                Bezier.Point p0 = bezier.points[i];
                if ((p0.color * channel) == Vector3D<int>.Zero)
                {
                    continue;
                }

                Bezier.Point p1 = bezier.points[(i + 1) % bezier.points.Count];

                Vector2D<float> a = new Vector2D<float>(p0.pos.X, p0.pos.Y);
                Vector2D<float> b = new Vector2D<float>(p1.pos.X, p1.pos.Y);

                float dist = DistanceToLineSegment(p, a, b);
                if (dist < minDist)
                {
                    minDist = dist;
                    index = i;
                }
            }

            if (!IsRightOfSegement(p, bezier.points[index].pos, bezier.points[(index + 1) % bezier.points.Count].pos))
            {
                minDist = -minDist;
            }

            return minDist;
        }

        private static float DistanceToLineSegment(Vector2D<float> p, Vector2D<float> a, Vector2D<float> b)
        {
            Vector2D<float> ab = b - a;
            Vector2D<float> ap = p - a;

            float t = Vector2D.Dot(ap, ab) / Vector2D.Dot(ab, ab);
            t = Math.Clamp(t, 0f, 1f);

            Vector2D<float> closest = a + t * ab;
            return Vector2D.Distance(p, closest);
        }

        private static float SignedDistance(Vector2D<float> p, Bezier bezier, Vector3D<int> channel)
        {
            //float sign = 0;
            float smallestDist = float.MaxValue;
            for(int i = 0; i < bezier.points.Count; i++)
            {
                if (bezier.points[i].color * channel == Vector3D<int>.Zero)
                {
                    continue;
                }
                Vector2D<float> A = bezier.points[i].pos;
                Vector2D<float> B = bezier.points[(i + 1) % bezier.points.Count].pos;

                Vector2D<float> ab = new Vector2D<float>(B.X - A.X, B.Y - A.Y);
                Vector2D<float> ap = new Vector2D<float>(p.X - A.X, p.Y - A.Y);

                float lenSq = ab.X * ab.X + ab.Y * ab.Y;
                float t = (ab.X * ap.X + ab.Y * ap.Y) / lenSq;

                t = Math.Clamp(t, 0.0f, 1.0f);

                Vector2D<float> closestPoint = new Vector2D<float>(A.X + ab.X * t, A.Y + ab.Y * t);

                Vector2D<float> d = new Vector2D<float>(p.X - closestPoint.X, p.Y - closestPoint.Y);
                float dist = MathF.Sqrt(d.X * d.X + d.Y * d.Y);

                if (smallestDist > dist)
                {
                    smallestDist = dist;
                    //float cross = (ab.X * (p.Y - A.Y)) - (ab.Y * (p.X - A.X));
                    //sign = (cross < 0) ? -1 : 1;
                }
            }

            return smallestDist;// * sign;
        }

        private static float HorizontalCheck(Vector2D<float> pos, Bezier b, int width)
        {
            Vector2D<float> posHorizontalRight = new Vector2D<float>(10, pos.Y);
            Vector2D<float> posHorizontalLeft = new Vector2D<float>(-10, pos.Y);

            float distance = 1.0f;
            for (int i = 0; i < b.points.Count; i++)
            {
                var p1 = b.points[i].pos;
                var p2 = b.points[(i + 1) % b.points.Count].pos;
                if (CheckIntersect(pos, posHorizontalRight, p1, p2, out Vector2D<float> intersectR))
                {
                    float localD = (pos - intersectR).Length;
                    if (localD < distance)
                    {
                        distance = localD;
                    }
                }
                if (CheckIntersect(pos, posHorizontalLeft, p1, p2, out Vector2D<float> intersectL))
                {
                    float localD = (pos - intersectR).Length;
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
            Vector2D<float> posVerticaTop = new Vector2D<float>(pos.X, 10);
            Vector2D<float> posVerticalBot = new Vector2D<float>(pos.X, -10);

            float distance = 1.0f;
            for (int i = 0; i < b.points.Count; i++)
            {
                var p1 = b.points[i].pos;
                var p2 = b.points[(i + 1) % b.points.Count].pos;
                if (CheckIntersect(pos, posVerticaTop, p1, p2, out Vector2D<float> intersectTop))
                {
                    float localD = (pos - intersectTop).Length;
                    if (localD < distance)
                    {
                        distance = localD;
                    }
                }
                if (CheckIntersect(pos, posVerticalBot, p1, p2, out Vector2D<float> intersectBot))
                {
                    float localD = (pos - intersectBot).Length;
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
            Vector2D<float> upRight = new Vector2D<float>(pos.X + 10, pos.Y + 10);
            Vector2D<float> botRight = new Vector2D<float>(pos.X + 10, pos.Y - 10);
            Vector2D<float> botLeft = new Vector2D<float>(pos.X - 10, pos.Y - 10);
            Vector2D<float> topLeft = new Vector2D<float>(pos.X - 10, pos.Y + 10);

            float distance = 1.0f;
            for (int i = 0; i < b.points.Count; i++)
            {
                var p1 = b.points[i].pos;
                var p2 = b.points[(i + 1) % b.points.Count].pos;
                
                if (CheckIntersect(pos, upRight, p1, p2, out Vector2D<float> intersectUR))
                {
                    float localD = (pos - intersectUR).Length;
                    if (localD < distance)
                    {
                        distance = localD;
                    }
                }
                if (CheckIntersect(pos, botRight, p1, p2, out Vector2D<float> intersectBR))
                {
                    float localD = (pos - intersectBR).Length;
                    if (localD < distance)
                    {
                        distance = localD;
                    }
                }
                if (CheckIntersect(pos, botLeft, p1, p2, out Vector2D<float> intersectBL))
                {
                    float localD = (pos - intersectBL).Length;
                    if (localD < distance)
                    {
                        distance = localD;
                    }
                }
                if (CheckIntersect(pos, topLeft, p1, p2, out Vector2D<float> intersectTL))
                {
                    float localD = (pos - intersectTL).Length;
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

        private static bool IsInsidePolygon(Vector2D<float> point, List<Bezier.Point> polygon)
        {
            int rayIntersect = 0;
            for (int i = 0; i < polygon.Count; i++)
            {
                var p1 = polygon[i].pos;
                var p2 = polygon[(i + 1) % polygon.Count].pos;

                if ((point.X > MathF.Min(p1.X, p2.X)) && (point.X <= MathF.Max(p1.X, p2.X)) && (point.Y <= MathF.Max(p1.Y, p2.Y)))
                {
                    double xIntersect = (point.X - p1.X) * (p2.Y - p1.Y) / (p2.X - p1.X) + p1.Y;
                    if (p1.Y == p2.Y || point.Y <= xIntersect)
                    {
                        rayIntersect++;
                    }
                }
            }
            return rayIntersect % 2 == 1;
        }

        private static bool IsRightOfSegement(Vector2D<float> point, Vector2D<float> e1, Vector2D<float> e2)
        {
            var ab = new Vector2D<float>(e2.X - e1.X, e2.Y - e1.Y);
            var ap = new Vector2D<float>(point.X - e1.X, point.Y - e1.Y);

            float cross = ab.X * ap.Y - ab.Y * ap.X;

            return cross < 0; // true = point is to the right of the edge from e1 to e2
        }

        private static float CalcAngleInSegment(Vector2D<float> p1, Vector2D<float> p2, Vector2D<float> p3)
        {
            Vector2D<float> v1 = p2 - p1;
            Vector2D<float> v2 = p3 - p2;

            //Vector2D<float> v1Norm = v1 / v1.Length;
            //Vector2D<float> v2Norm = v2 / v2.Length;

            Vector2D<float> v1Norm = Vector2D.Normalize(v1);
            Vector2D<float> v2Norm = Vector2D.Normalize(v2);

            float dotProduct = Vector2D.Dot(v1Norm, v2Norm);
            float angle = MathF.Acos(Math.Clamp(dotProduct, -1, 1));

            return angle;
        }
    }
}
