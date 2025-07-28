using ArctisAurora.EngineWork.Renderer.UI;
using Silk.NET.Maths;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Runtime.InteropServices;
using Windows.Devices.Bluetooth.Advertisement;
using static ArctisAurora.EngineWork.Renderer.UI.AuroraFont;
using AuroraFont = ArctisAurora.EngineWork.Renderer.UI.AuroraFont;

namespace ArctisAurora.EngineWork.Serialization
{
    internal unsafe static class AssetImporter
    {
        internal static void ImportFont(string characters, string fontName)
        {
            var fs = new FileStream("C:\\Windows\\Fonts\\" + fontName, FileMode.Open, FileAccess.Read);
            var reader = new BinaryReader(fs);

            AuroraFont font = new AuroraFont();

            font.fontMeta = new FontMeta
            {
                version = ReadUInt32BE(reader),  // Big-endian
                tableCount = ReadUInt16BE(reader)
            };

            // Skip other fields (searchRange, entrySelector, rangeShift)
            fs.Position += 6;
            font.tableEntries = new TableEntry[font.fontMeta.tableCount];
            for (int i = 0; i < font.fontMeta.tableCount; i++)
            {
                font.tableEntries[i] = new TableEntry
                {
                    name = new string(reader.ReadChars(4)),
                    checksum = ReadUInt32BE(reader),
                    offset = ReadUInt32BE(reader),
                    length = ReadUInt32BE(reader)
                };
            }

            string baseName = fontName.Split('.')[0];
            string path = Paths.FONTS + $"\\{baseName}\\{baseName}" + ".auroraFont";

            Serializer.Serialize(font, path);

            /*if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
            }

            using FileStream stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite);
            using BinaryWriter writer = new BinaryWriter(stream);
            
            writer.Write(font.offsetTable.version);
            writer.Write(font.offsetTable.tableCount);

            for(int i = 0; i < font.offsetTable.tableCount; i++)
            {
                writer.Write(font.tableEntries[i].name.ToCharArray());
                writer.Write(font.tableEntries[i].checksum);
                writer.Write(font.tableEntries[i].offset);
                writer.Write(font.tableEntries[i].length);
            }

            writer.Close();*/
        }

        //internal static Image<Rgba32> GenerateGlyphAtlas(string characters, string fontName = "arial.ttf")
        //{
        //    Glyph glyph;
        //    return ImportFont(out glyph, letter, fontName);
        //}

        internal static Image<Rgba32> ImportFont(out Glyph glyph, char letter, string fontName = "arial.ttf")
        {
            var fs = new FileStream("C:\\Windows\\Fonts\\" + fontName, FileMode.Open, FileAccess.Read);
            var reader = new BinaryReader(fs);

            FontMeta offsetTable = new FontMeta
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

            //Console.WriteLine($"Found {offsetTable.tableCount} of tables");
            //for (int i = 0; i < offsetTable.tableCount; i++)
            //{
            //    Console.WriteLine($"    TN: {tables[i].name} @loc {tables[i].offset}");
            //}

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

            char targetChar = letter;
            ushort charIndex = GetGlyphIndex(targetChar, reader, tables);
            glyph = GetGlyphOutline(charIndex, reader, tables, glyphOffsets);

            Image<Rgba32> image = new Image<Rgba32>(128, 128);
            GenerateMSDF(glyph, ref image, 255f);

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

        private static Glyph GetGlyphOutline(ushort glyphIndex, BinaryReader reader, TableEntry[] tables, uint[] glyphOffsets)
        {
            var glyfTable = tables.First(t => t.name == "glyf"); // "glyf"
            uint start = glyphOffsets[glyphIndex];
            uint end = glyphOffsets[glyphIndex + 1];

            if (start == end)
                return null;

            reader.BaseStream.Position = glyfTable.offset + start;

            // --- Read Glyph Header ---
            short numContours = ReadInt16BE(reader);
            if (numContours <= 0)
                return null; // Skip composite/empty glyphs

            Glyph glyph = new Glyph();
            short xMin = ReadInt16BE(reader);
            short yMin = ReadInt16BE(reader);
            short xMax = ReadInt16BE(reader);
            short yMax = ReadInt16BE(reader);

            glyph.SetParams(xMin, xMax, yMin, yMax, 2048);

            short xK = (short)(xMax - xMin);
            short yK = (short)(yMax - yMin);

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

            // Read flags
            int offset = 0;
            for (int con = 0; con < numContours; con++)
            {
                Bezier bezier = new Bezier();
                ushort endPoint = (ushort)(endPts[con] + 1);
                for (int i = offset; i < endPoint; i++)
                {
                    flags[i] = reader.ReadByte();
                    bezier.points.Add(new Bezier.Point());
                    bezier.points[i - offset].SetAnchor((flags[i] & 0x01) != 0); // Bit 0 = on-curve
                    if ((flags[i] & 0x08) != 0)
                    {
                        uint repeater = reader.ReadByte();
                        for (int j = 1; j <= repeater; j++)
                        {
                            flags[i + j] = flags[i];
                            bezier.points.Add(new Bezier.Point());
                            bezier.points[i + j - offset].SetAnchor(bezier.points[i - offset].isAnchor);
                        }
                        i += (int)repeater;
                    }
                }
                offset = endPoint;
                glyph.contours.Add(bezier);
            }

            // Read X coordinates
            short x = (short)-xMin;
            int flagIndex = 0;
            for (int j = 0; j < numContours; j++)
            {
                Bezier bezier = glyph.contours[j];
                for (int i = 0; i < bezier.points.Count; i++)
                {
                    if ((flags[flagIndex] & 0x02) != 0) // X-byte delta
                    {
                        byte delta = reader.ReadByte();
                        x += (flags[flagIndex] & 0x10) != 0 ? delta : (short)-delta;
                    }
                    else if ((flags[flagIndex] & 0x10) == 0) // X-word delta
                    {
                        x += ReadInt16BE(reader);
                    }
                    bezier.points[i].SetX((float)x / xK);
                    flagIndex++;
                }
            }

            // Read Y coordinates
            short y = (short)-yMin;
            flagIndex = 0;
            for (int j = 0; j < numContours; j++)
            {
                Bezier bezier = glyph.contours[j];
                for (int i = 0; i < bezier.points.Count; i++)
                {
                    if ((flags[flagIndex] & 0x04) != 0) // Y-byte delta
                    {
                        byte delta = reader.ReadByte();
                        y += (flags[flagIndex] & 0x20) != 0 ? delta : (short)-delta;
                    }
                    else if ((flags[flagIndex] & 0x20) == 0) // Y-word delta
                    {
                        y += ReadInt16BE(reader);
                    }
                    bezier.points[i].SetY((float)y / yK);
                    flagIndex++;
                }
            }

            //for (numContours = 0; numContours < glyph.contours.Count; numContours++)
            //{
            //    for(int i = 0; i < glyph.contours[numContours].points.Count; i++)
            //    {
            //        Vector2D<float> pos = glyph.contours[numContours].points[i].pos;
            //        Console.WriteLine($"Point {i} in contour {numContours}: ({pos.X}, {pos.Y})");
            //    }
            //}

            for (int i = 0; i < glyph.contours.Count; i++)
            {
                SubdivideEdges(glyph.contours[i].points, 4);
                //Console.WriteLine($"Contour {i} has {glyph.contours[i].points.Count} points.");
            }

            // set edge colors
            int colorindex = 0;
            for (int j = 0; j < numContours; j++)
            {
                Bezier b = glyph.contours[j];
                int localCount = b.points.Count;
                for (int i = 0; i < localCount; i++)
                {
                    Vector2D<float> p1 = b.points[i].pos;
                    Vector2D<float> p2 = b.points[(i + 1) % localCount].pos;
                    Vector2D<float> p3 = b.points[(i + 2) % localCount].pos;

                    float direction = CalcVectorAngle(p1, p2, p3);
                    if (direction < 0)
                    {
                        colorindex++;
                    }
                    switch (colorindex % 3)
                    {
                        case 0:
                            glyph.contours[j].points[i].color = new Vector3D<int>(1, 1, 0);
                            break;
                        case 1:
                            glyph.contours[j].points[i].color = new Vector3D<int>(0, 1, 1);
                            break;
                        case 2:
                            glyph.contours[j].points[i].color = new Vector3D<int>(1, 0, 1);
                            break;
                    }
                }
                colorindex++;
                bool isOfColor = b.points[0].color == b.points[localCount - 1].color;
                if (isOfColor)
                {
                    b.points[localCount - 1].color = new Vector3D<int>(0, 1, 1);
                }
            }

            return glyph;
        }

        private static short ReadInt16BE(BinaryReader reader) =>
            BitConverter.ToInt16(reader.ReadBytes(2).Reverse().ToArray(), 0);

        private static ushort ReadUInt16BE(BinaryReader reader) =>
            BitConverter.ToUInt16(reader.ReadBytes(2).Reverse().ToArray(), 0);

        private static uint ReadUInt32BE(BinaryReader reader) =>
            BitConverter.ToUInt32(reader.ReadBytes(4).Reverse().ToArray(), 0);

        private static void GenerateMSDF(Glyph g, ref Image<Rgba32> image, float distanceFactor)
        {
            int width = image.Width;
            int height = image.Height;

            // go through each pixel
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    Vector2D<float> p = new Vector2D<float>((x + 0.5f) / width, (y + 0.5f) / height);
                    float redDist = Math.Clamp(GetClosestDistance(p, g, new Vector3D<int>(1, 0, 0)) * distanceFactor, -1 , 1);
                    float greenDist = Math.Clamp(GetClosestDistance(p, g, new Vector3D<int>(0, 1, 0)) * distanceFactor, -1, 1);
                    float blueDist = Math.Clamp(GetClosestDistance(p, g, new Vector3D<int>(0, 0, 1)) * distanceFactor, -1, 1);

                    redDist = redDist * 0.5f + 0.5f;
                    greenDist = greenDist * 0.5f + 0.5f;
                    blueDist = blueDist * 0.5f + 0.5f;
            
            
                    image[x, y] = new Rgba32(redDist, greenDist, blueDist, 1f);
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

                Vector2D<float> segementLength = (p2 - p1) / count;

                for (int j = 0; j < count; j++)
                {
                    Vector2D<float> newPos = p1 + segementLength * j;
                    points.Add(new Bezier.Point(newPos, original[i].isAnchor));
                }
            }
        }

        private static float GetClosestDistance(Vector2D<float> p, Glyph glyph, Vector3D<int> channel)
        {
            float minDist = float.MaxValue;
            int pointIndex = 0;
            int index = 0;
            for (int contour = 0; contour < glyph.contours.Count; contour++)
            {
                Bezier bezier = glyph.contours[contour];
                for (int j = 0; j < bezier.points.Count; j++)
                {
                    Bezier.Point p0 = bezier.points[j];
                    if (p0.color * channel == Vector3D<int>.Zero)
                    {
                        continue;
                    }

                    Bezier.Point p1 = bezier.points[(j + 1) % bezier.points.Count];

                    Vector2D<float> vec1 = new Vector2D<float>(p0.pos.X, p0.pos.Y);
                    Vector2D<float> vec2 = new Vector2D<float>(p1.pos.X, p1.pos.Y);

                    float dist = DistanceToLineSegment(p, vec1, vec2);
                    if (dist < minDist)
                    {
                        minDist = dist;
                        pointIndex = j;
                        index = contour;
                    }
                }
            }

            // do orthagonality check
            Bezier b = glyph.contours[index];
            Vector2D<float> op0 = b.points[pointIndex].pos;
            Vector2D<float> op1 = b.points[(pointIndex + 1) % b.points.Count].pos;
            Vector2D<float> op2;
            
            bool isOrthogonal = true;
            
            float d1 = Vector2D.Distance(op0, p);
            float d2 = Vector2D.Distance(op1, p);
            if (d1 == minDist)
            {
                Bezier.Point prevPoint = b.points[(pointIndex - 1 + b.points.Count) % b.points.Count];
                op2 = prevPoint.pos;
                isOrthogonal = IsOrthagonalToE1(op1, op0, op2, p);
                bool isOfColor = prevPoint.color * channel != Vector3D<int>.Zero;
                if (!isOrthogonal && isOfColor)
                {
                    pointIndex = (pointIndex - 1 + b.points.Count) % b.points.Count;
                }
            }
            else if(d2 == minDist)
            {
                Bezier.Point nextPoint = b.points[(pointIndex + 2) % b.points.Count];
                op2 = nextPoint.pos;
                isOrthogonal = IsOrthagonalToE1(op0, op1, op2, p);
                bool isOfColor = nextPoint.color * channel != Vector3D<int>.Zero;
                if (!isOrthogonal && isOfColor)
                {
                    pointIndex = (pointIndex + 1) % b.points.Count;
                }
            }

            if (!IsRightOfSegement(p, b.points[pointIndex].pos, b.points[(pointIndex + 1) % b.points.Count].pos))
            {
                minDist = -minDist;
            }

            return minDist;
        }

        private static bool IsOrthagonalToE1(Vector2D<float> a, Vector2D<float> b, Vector2D<float> c, Vector2D<float> d)
        {
            Vector2D<float> ab = b - a;
            Vector2D<float> bc = b - c;

            Vector2D<float> bd = b - d;

            Vector2D<float> abNorm = Vector2D.Normalize(ab);
            Vector2D<float> bcNorm = Vector2D.Normalize(bc);
            Vector2D<float> bdNorm = Vector2D.Normalize(bd);

            float dotA = MathF.Abs(Vector2D.Dot(abNorm, bdNorm));
            float dotB = MathF.Abs(Vector2D.Dot(bcNorm, bdNorm));

            //Console.WriteLine($"Angle A: {angleA}, Angle B: {angleB}, k1: {k1}, k2: {k2}");

            if (dotA - dotB < 0)
            {
                return true; // edge AB is closer to 90 degrees
            }
            return false; // edge CB is closer to 90 degrees
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
        
        private static bool IsRightOfSegement(Vector2D<float> point, Vector2D<float> e1, Vector2D<float> e2)
        {
            var ab = new Vector2D<float>(e2.X - e1.X, e2.Y - e1.Y);
            var ap = new Vector2D<float>(point.X - e1.X, point.Y - e1.Y);

            float cross = ab.X * ap.Y - ab.Y * ap.X;

            return cross < 0; // true = point is to the right of the edge from e1 to e2
        }

        private static float CalcVectorAngle(Vector2D<float> p1, Vector2D<float> p2, Vector2D<float> p3)
        {
            Vector2D<float> v1 = p2 - p1;
            Vector2D<float> v2 = p3 - p2;

            Vector2D<float> v1Norm = Vector2D.Normalize(v1);
            Vector2D<float> v2Norm = Vector2D.Normalize(v2);

            float dotProduct = Vector2D.Dot(v1Norm, v2Norm);

            return dotProduct;
        }
    }
}
