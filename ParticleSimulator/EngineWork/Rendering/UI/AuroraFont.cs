using ArctisAurora.EngineWork.Serialization;
using Silk.NET.Maths;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Runtime.InteropServices;

namespace ArctisAurora.EngineWork.Rendering.UI
{
    [@Serializable]
    public class AuroraFont : IDeserialize
    {
        [StructLayout(LayoutKind.Sequential, Pack = 1), @Serializable]
        public struct FontMeta
        {
            public uint version;
            public ushort tableCount;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1), @Serializable]
        public struct TableEntry
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 4)]
            public string name;
            public uint checksum;
            public uint offset;
            public uint length;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1), @Serializable]
        public struct TextData
        {
            public int characterCount;
            public char[] characters;
        }


        public FontMeta fontMeta;
        public TableEntry[] tableEntries;

        public TextData textData;

        public void Deserialize(string path)
        {
            // meta data
            byte[] fontMetaBuffer = new byte[Marshal.SizeOf<FontMeta>()];
            using (FileStream fileStream = new FileStream(path, FileMode.Open, FileAccess.Read))
            {
                fileStream.Read(fontMetaBuffer);
                fileStream.Close();
            }

            GCHandle handleMeta = GCHandle.Alloc(fontMetaBuffer, GCHandleType.Pinned);
            fontMeta = Marshal.PtrToStructure<FontMeta>(handleMeta.AddrOfPinnedObject());
            handleMeta.Free();

            // table entries
            tableEntries = new TableEntry[fontMeta.tableCount];
            byte[] tableEntryBuffer = new byte[Marshal.SizeOf<TableEntry>() * fontMeta.tableCount];

            using (FileStream fileStream = new FileStream(path, FileMode.Open, FileAccess.Read))
            {
                fileStream.Seek(Marshal.SizeOf<FontMeta>(), SeekOrigin.Begin);
                fileStream.Read(tableEntryBuffer, 0, tableEntryBuffer.Length);
            }
            GCHandle handleTables = GCHandle.Alloc(tableEntryBuffer, GCHandleType.Pinned);
            for (int i = 0; i < fontMeta.tableCount; i++)
            {
                IntPtr entryPtr = handleTables.AddrOfPinnedObject() + (i * Marshal.SizeOf<TableEntry>());
                tableEntries[i] = Marshal.PtrToStructure<TableEntry>(entryPtr);
            }
            handleTables.Free();

            // character data
            textData = new TextData();
            using (FileStream fileStream = new FileStream(path, FileMode.Open, FileAccess.Read))
            {
                long offset = Marshal.SizeOf<FontMeta>() + (Marshal.SizeOf<TableEntry>() * fontMeta.tableCount);
                fileStream.Seek(offset, SeekOrigin.Begin);

                byte[] characterCount = new byte[Marshal.SizeOf<int>()];
                fileStream.Read(characterCount, 0, characterCount.Length);

                GCHandle handleTextData = GCHandle.Alloc(characterCount, GCHandleType.Pinned);
                textData.characterCount = Marshal.PtrToStructure<int>(handleTextData.AddrOfPinnedObject());
                handleTextData.Free();

                // Read characters
                BinaryReader reader = new BinaryReader(fileStream, System.Text.Encoding.Unicode);
                textData.characters = new char[textData.characterCount];
                for (int i = 0; i < textData.characterCount; i++)
                {
                    textData.characters[i] = reader.ReadChar();
                }
            }
        }

        internal static void GenerateGlyphAtlas(AuroraFont fontData, string fontName, int perGlyphSize)
        {
            string path = "C:\\Windows\\Fonts\\" + fontName;
            AtlasMetaData glyphs = new AtlasMetaData();
            using (BinaryReader reader = new BinaryReader(new FileStream(path, FileMode.Open, FileAccess.Read)))
            {
                TableEntry maxp = fontData.tableEntries.First(t => t.name == "maxp");
                reader.BaseStream.Position = maxp.offset + 4;
                uint numGlyphs = AssetImporter.ReadUInt16BE(reader);

                uint[] glyphOffsets = new uint[numGlyphs + 1];
                TableEntry headTable = fontData.tableEntries.First(t => t.name == "head"); // 'head'
                reader.BaseStream.Position = headTable.offset + 50; // Offset 50 in 'head'
                uint indexToLocFormat = AssetImporter.ReadUInt16BE(reader); // 0 = uint16, 1 = uint32

                TableEntry locaTable = fontData.tableEntries.First(t => t.name == "loca"); // 'loca'
                reader.BaseStream.Position = locaTable.offset;
                for (int i = 0; i <= numGlyphs; i++)
                {
                    if (indexToLocFormat == 0)
                        glyphOffsets[i] = (uint)(AssetImporter.ReadUInt16BE(reader) * 2); // 16-bit → scale ×2
                    else
                        glyphOffsets[i] = AssetImporter.ReadUInt32BE(reader); // 32-bit
                }

                // loading glyph outlines
                TableEntry cmap = fontData.tableEntries.First(t => t.name == "cmap"); // for index
                TableEntry glyf = fontData.tableEntries.First(t => t.name == "glyf"); // for glyph outlines

                glyphs.glyphCount = fontData.textData.characterCount;
                glyphs.chars = fontData.textData.characters;
                glyphs.glyphs = new Glyph[fontData.textData.characterCount];
                for (int i = 0; i < fontData.textData.characterCount; i++)
                {
                    char character = fontData.textData.characters[i];
                    ushort glyphIndex = GetGlyphIndex(character, reader, cmap);
                    Glyph glyph = GetGlyphOutline(glyphIndex, glyphOffsets, glyf, reader);
                    glyphs.glyphs[i] = glyph;
                }

                //loading distances between glyphs
                TableEntry hhea = fontData.tableEntries.First(t => t.name == "hhea");
                reader.BaseStream.Position = hhea.offset + 34;
                ushort numberOfHMetrics = AssetImporter.ReadUInt16BE(reader);

                TableEntry hmtx = fontData.tableEntries.First(t => t.name == "hmtx"); // for distances between glyphs
                ushort[] rsb = new ushort[numberOfHMetrics];
                short[] lsb = new short[numGlyphs];
                reader.BaseStream.Position = hmtx.offset;
                for (int i = 0; i < numberOfHMetrics; i++)
                {
                    rsb[i] = AssetImporter.ReadUInt16BE(reader);
                    lsb[i] = AssetImporter.ReadInt16BE(reader);
                }

                for (int i = numberOfHMetrics; i < numGlyphs ; i++)
                {
                    lsb[i] = AssetImporter.ReadInt16BE(reader);
                }

                //TableEntry vhea = fontData.tableEntries.First(t => t.name == "vhea");
                //reader.BaseStream.Position = vhea.offset + 34;
                //ushort numberOfVMetrics = AssetImporter.ReadUInt16BE(reader);
                //
                //TableEntry vmtx = fontData.tableEntries.First(t => t.name == "vmtx");
                //ushort[] bsb = new ushort[numberOfVMetrics];
                //short[] tsb = new short[numGlyphs];
                //reader.BaseStream.Position = vmtx.offset;
                //for (int i = 0; i < numGlyphs; i++)
                //{
                //    bsb[i] = AssetImporter.ReadUInt16BE(reader);
                //    tsb[i] = AssetImporter.ReadInt16BE(reader);
                //}
                //for (int i = numberOfVMetrics; i < numGlyphs; i++)
                //{
                //    tsb[i] = AssetImporter.ReadInt16BE(reader);
                //}

                for (int i = 0; i < glyphs.glyphCount; i++)
                {
                    char character = fontData.textData.characters[i];
                    ushort glyphIndex = GetGlyphIndex(character, reader, cmap);
                    glyphs.glyphs[i].rsb = (float)rsb[glyphIndex] / 2048f;
                    glyphs.glyphs[i].lsb = (float)lsb[glyphIndex] / 2048f;
                    if(glyphs.glyphs[i].yMin < 0)
                    {
                        float tsb = -(glyphs.glyphs[i].yMin) / 2048f;
                        glyphs.glyphs[i].tsb = tsb;
                    }
                    //glyphs.glyphs[i].bsb = (float)bsb[glyphIndex] / 2048f;

                    //Console.WriteLine($"LSB : {glyphs.glyphs[i].lsb} , RSB : {glyphs.glyphs[i].rsb}");
                }
            }

            string baseName = fontName.Split('.')[0];
            string atlasDataPath = Path.Combine(Paths.FONTS, baseName, $"{baseName}.agd"); // aurora glyph data
            Serializer.Serialize(glyphs, atlasDataPath);

            //here we generate the atlas
            int glyphsPerAxis = (int)Math.Ceiling(MathF.Sqrt(fontData.textData.characterCount));
            Image<Rgba32> atlasImage = new Image<Rgba32>(perGlyphSize * glyphsPerAxis, perGlyphSize * glyphsPerAxis);
            for (int i = 0; i < glyphsPerAxis; i++)
            {
                for (int j = 0; j < glyphsPerAxis; j++)
                {
                    int index = i * glyphsPerAxis + j;
                    if (index >= fontData.textData.characterCount)
                        break;
                    Glyph g = glyphs.glyphs[index];
                    if (g == null)
                        continue;
                    // Calculate position in the atlas
                    int x = j * perGlyphSize;
                    int y = i * perGlyphSize;
                    // Create a new image for the glyph
                    // Generate MSDF for the glyph
                    GenerateMSDF(g, ref atlasImage, x, y, glyphsPerAxis, 255f);
                    // Copy the glyph image to the atlas
                }
            }

            atlasImage.Save(Path.Combine(Paths.FONTS, baseName, $"{baseName}_atlas.png"));
        }

        private static ushort GetGlyphIndex(char character, BinaryReader reader, TableEntry cmap)
        {
            reader.BaseStream.Position = cmap.offset;

            ushort version = AssetImporter.ReadUInt16BE(reader);
            ushort numSubtables = AssetImporter.ReadUInt16BE(reader);

            // Search for Unicode BMP subtable (PlatformID=3, EncodingID=1)
            for (int i = 0; i < numSubtables; i++)
            {
                ushort platformID = AssetImporter.ReadUInt16BE(reader);
                ushort encodingID = AssetImporter.ReadUInt16BE(reader);
                uint subtableOffset = AssetImporter.ReadUInt32BE(reader);

                if (platformID == 3 && encodingID == 1) // Windows Unicode
                {
                    long savedPos = reader.BaseStream.Position;
                    reader.BaseStream.Position = cmap.offset + subtableOffset;

                    ushort format = AssetImporter.ReadUInt16BE(reader);
                    if (format == 4) // Format 4 (segmented mapping)
                    {
                        ushort length = AssetImporter.ReadUInt16BE(reader);
                        ushort language = AssetImporter.ReadUInt16BE(reader);
                        ushort segCountX2 = AssetImporter.ReadUInt16BE(reader);
                        ushort segCount = (ushort)(segCountX2 / 2);
                        ushort searchRange = AssetImporter.ReadUInt16BE(reader);
                        ushort entrySelector = AssetImporter.ReadUInt16BE(reader);
                        ushort rangeShift = AssetImporter.ReadUInt16BE(reader);

                        // Read segmentation data
                        ushort[] endCodes = new ushort[segCount];
                        for (int j = 0; j < segCount; j++) endCodes[j] = AssetImporter.ReadUInt16BE(reader);

                        ushort reservedPad = AssetImporter.ReadUInt16BE(reader);

                        ushort[] startCodes = new ushort[segCount];
                        for (int j = 0; j < segCount; j++) startCodes[j] = AssetImporter.ReadUInt16BE(reader);

                        short[] idDeltas = new short[segCount];
                        for (int j = 0; j < segCount; j++) idDeltas[j] = (short)AssetImporter.ReadUInt16BE(reader);

                        long idRangeOffsetStart = reader.BaseStream.Position;
                        ushort[] idRangeOffsets = new ushort[segCount];
                        for (int j = 0; j < segCount; j++) idRangeOffsets[i] = AssetImporter.ReadUInt16BE(reader);

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
                                ushort glyphIndex = AssetImporter.ReadUInt16BE(reader);
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

        private static Glyph GetGlyphOutline(ushort glyphIndex, uint[] glyphOffsets, TableEntry glyfTable, BinaryReader reader)
        {
            uint start = glyphOffsets[glyphIndex];
            uint end = glyphOffsets[glyphIndex + 1];

            if (start == end)
                return new Glyph();

            reader.BaseStream.Position = glyfTable.offset + start;

            // --- Read Glyph Header ---
            short numContours = AssetImporter.ReadInt16BE(reader);
            if (numContours <= 0)
                return new Glyph(); // Skip composite/empty glyphs

            Glyph glyph = new Glyph();
            short xMin = AssetImporter.ReadInt16BE(reader);
            short yMin = AssetImporter.ReadInt16BE(reader);
            short xMax = AssetImporter.ReadInt16BE(reader);
            short yMax = AssetImporter.ReadInt16BE(reader);

            glyph.SetParams(xMin, xMax, yMin, yMax, 2048);

            short xK = (short)(xMax - xMin);
            short yK = (short)(yMax - yMin);

            // --- Read Contour End Points ---
            ushort[] endPts = new ushort[numContours];
            for (int i = 0; i < numContours; i++)
                endPts[i] = AssetImporter.ReadUInt16BE(reader);

            ushort pointCount = (ushort)(endPts.Last() + 1);

            // --- Read Instructions (skip) ---
            ushort instructionLength = AssetImporter.ReadUInt16BE(reader);
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
                        x += AssetImporter.ReadInt16BE(reader);
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
                        y += AssetImporter.ReadInt16BE(reader);
                    }
                    bezier.points[i].SetY((float)y / yK);
                    flagIndex++;
                }
            }

            //for (int i = 0; i < glyph.contours.Count; i++)
            //{
            //    SubdivideEdges(glyph.contours[i].points, 4);
            //    //Console.WriteLine($"Contour {i} has {glyph.contours[i].points.Count} points.");
            //}

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

        internal static void GenerateMSDF(Glyph g, ref Image<Rgba32> image, int startX, int startY, int glyphsPerAxis, float distanceFactor)
        {
            int width = image.Width / glyphsPerAxis;
            int height = image.Height / glyphsPerAxis;

            // go through each pixel
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    Vector2D<float> p = new Vector2D<float>((x + 0.5f) / width, (y + 0.5f) / height);
                    float redDist = Math.Clamp(GetClosestDistance(p, g, new Vector3D<int>(1, 0, 0)) * distanceFactor, -1, 1);
                    float greenDist = Math.Clamp(GetClosestDistance(p, g, new Vector3D<int>(0, 1, 0)) * distanceFactor, -1, 1);
                    float blueDist = Math.Clamp(GetClosestDistance(p, g, new Vector3D<int>(0, 0, 1)) * distanceFactor, -1, 1);

                    redDist = redDist * 0.5f + 0.5f;
                    greenDist = greenDist * 0.5f + 0.5f;
                    blueDist = blueDist * 0.5f + 0.5f;


                    image[startX + x, startY + y] = new Rgba32(redDist, greenDist, blueDist, 1f);
                }
            }
        }

        private static float GetClosestDistance(Vector2D<float> p, Glyph glyph, Vector3D<int> channel)
        {
            if (glyph.contours.Count == 0)
            {
                return -1f;
            }

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
            else if (d2 == minDist)
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

    [@Serializable]
    public class AtlasMetaData : IDeserialize
    {
        public int glyphCount;
        public char[] chars;
        public Glyph[] glyphs;

        public void Deserialize(string name)
        {
            string path = Paths.FONTS + $"\\{name}\\{name}.agd";
            using (FileStream fileStream = new FileStream(path, FileMode.Open, FileAccess.Read))
            {
                BinaryReader reader = new BinaryReader(fileStream, System.Text.Encoding.Unicode);
                glyphCount = reader.ReadInt32();
                chars = new char[glyphCount];
                glyphs = new Glyph[glyphCount];

                for (int i = 0; i < glyphCount; i++)
                {
                    chars[i] = reader.ReadChar();
                }

                // COMMENTED OUT CUZ IT MIGHT HAVE PROBLEMS WITH BEZIER LIST.
                // DIDNT TEST AND THIS IS NOT PERFORMANCE CRITICAL

                //long offset = sizeof(int) + count * sizeof(char) * 2;
                //fileStream.Seek(offset, SeekOrigin.Begin);

                //byte[] buffer = new byte[Marshal.SizeOf<>()];
                //GCHandle handleMeta = GCHandle.Alloc(fontMetaBuffer, GCHandleType.Pinned);
                //fontMeta = Marshal.PtrToStructure<FontMeta>(handleMeta.AddrOfPinnedObject());
                //handleMeta.Free();

                for (int i = 0; i < glyphCount; i++)
                {
                    glyphs[i] = new Glyph();
                    glyphs[i].xMin = (short)reader.ReadInt16();
                    glyphs[i].yMin = (short)reader.ReadInt16();
                    glyphs[i].xMax = (short)reader.ReadInt16();
                    glyphs[i].yMax = (short)reader.ReadInt16();

                    glyphs[i].glyphWidth = reader.ReadSingle();
                    glyphs[i].glyphHeight = reader.ReadSingle();

                    glyphs[i].rsb = reader.ReadSingle();
                    glyphs[i].lsb = reader.ReadSingle();
                    glyphs[i].tsb = reader.ReadSingle();
                }
            }
        }

        public Glyph GetGlyph(char character)
        {
            int index = Array.IndexOf(chars, character);
            if (index >= 0 && index < glyphs.Length)
            {
                return glyphs[index];
            }
            return null; // or throw an exception if preferred
        }

        public (Glyph, int) GetGlyphAndIndex(char character)
        {
            int index = Array.IndexOf(chars, character);
            if (index >= 0 && index < glyphs.Length)
            {
                return (glyphs[index], index);
            }
            return (null, -1); // or throw an exception if preferred
        }

        public int GetIndexOfChar(char character)
        {
            int index = Array.IndexOf(chars, character);
            if (index >= 0 && index < glyphs.Length)
            {
                return index;
            }
            return -1; // or throw an exception if preferred
        }
    }
}