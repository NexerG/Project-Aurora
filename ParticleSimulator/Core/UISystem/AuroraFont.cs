using ArctisAurora.Core.Filing.Serialization;
using Silk.NET.Maths;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Runtime.InteropServices;

namespace ArctisAurora.Core.UISystem
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
            //[@Serializable]
            public int characterCount;
            //[@Serializable]
            public char[] characters;
        }

        [@Serializable]
        public FontMeta fontMeta;
        [@Serializable]
        public TableEntry[] tableEntries;

        [@Serializable]
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
            byte[] tableEntryBuffer = new byte[(Marshal.SizeOf<TableEntry>() + sizeof(int)) * fontMeta.tableCount];

            using (FileStream fileStream = new FileStream(path, FileMode.Open, FileAccess.Read))
            {
                fileStream.Seek(Marshal.SizeOf<FontMeta>() + sizeof(int), SeekOrigin.Begin);
                fileStream.Read(tableEntryBuffer, 0, tableEntryBuffer.Length);
            }
            GCHandle handleTables = GCHandle.Alloc(tableEntryBuffer, GCHandleType.Pinned);
            for (int i = 0; i < fontMeta.tableCount; i++)
            {
                IntPtr entryPtr = handleTables.AddrOfPinnedObject() + (i * (Marshal.SizeOf<TableEntry>()) + sizeof(int));
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

                reader.BaseStream.Position = headTable.offset + 18; // Offset 18 in 'head' is unitsPerEm
                ushort unitsPerEm = AssetImporter.ReadUInt16BE(reader);

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
                    Glyph glyph = GetGlyphOutline(glyphIndex, glyphOffsets, glyf, reader, unitsPerEm);
                    glyphs.glyphs[i] = glyph;
                }

                //loading distances between glyphs
                TableEntry hhea = fontData.tableEntries.First(t => t.name == "hhea");
                reader.BaseStream.Position = hhea.offset + 34;
                ushort numberOfHMetrics = AssetImporter.ReadUInt16BE(reader);

                TableEntry hmtx = fontData.tableEntries.First(t => t.name == "hmtx"); // for distances between glyphs
                ushort[] advanceWidth = new ushort[numGlyphs];
                short[] lsb = new short[numGlyphs];
                reader.BaseStream.Position = hmtx.offset;
                for (int i = 0; i < numberOfHMetrics; i++)
                {
                    advanceWidth[i] = AssetImporter.ReadUInt16BE(reader);
                    lsb[i] = AssetImporter.ReadInt16BE(reader);
                }

                for (int i = numberOfHMetrics; i < numGlyphs ; i++)
                {
                    lsb[i] = AssetImporter.ReadInt16BE(reader);
                    advanceWidth[i] = advanceWidth[numberOfHMetrics - 1];
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

                reader.BaseStream.Position = hhea.offset + 4; // ascender is at offset 4
                short ascender = AssetImporter.ReadInt16BE(reader);
                reader.BaseStream.Position = hhea.offset + 6;
                short descender = AssetImporter.ReadInt16BE(reader);
                float lineHeight = (ascender - descender) / unitsPerEm;

                for (int i = 0; i < glyphs.glyphCount; i++)
                {
                    char character = fontData.textData.characters[i];
                    ushort glyphIndex = GetGlyphIndex(character, reader, cmap);
                    glyphs.glyphs[i].advanceWidth = (float)advanceWidth[glyphIndex] / unitsPerEm;
                    glyphs.glyphs[i].leftSideOffset = (float)lsb[glyphIndex] / unitsPerEm;
                    if(glyphs.glyphs[i].yMin < 0)
                    {
                        glyphs.glyphs[i].tsb = -(glyphs.glyphs[i].yMin) / unitsPerEm;
                    }
                    if (glyphs.glyphs[i].glyphHeight == 0)
                        glyphs.glyphs[i].glyphHeight = lineHeight;

                    if (glyphs.glyphs[i].glyphWidth == 0)
                        glyphs.glyphs[i].glyphWidth = glyphs.glyphs[i].advanceWidth;
                }
            }

            string baseName = fontName.Split('.')[0];
            string atlasDataPath = Path.Combine(Paths.FONTS, baseName, $"{baseName}.agd"); // aurora glyph data
            Serializer.SerializeAttributed(glyphs, atlasDataPath);

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
                    // Generate MTSDF for the glyph
                    GenerateMTSDF(g, ref atlasImage, x, y, glyphsPerAxis, perGlyphSize / 6f);
                    //GenerateMTSDF(g, ref atlasImage, x, y, glyphsPerAxis, 255f);
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
                        for (int j = 0; j < segCount; j++) idRangeOffsets[j] = AssetImporter.ReadUInt16BE(reader);

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
                                    return (ushort)((glyphIndex + idDeltas[j]) % 65536);
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

        private static Glyph GetGlyphOutline(ushort glyphIndex, uint[] glyphOffsets, TableEntry glyfTable, BinaryReader reader, float unitsPerEm)
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

            glyph.SetParams(xMin, xMax, yMin, yMax, unitsPerEm);

            float coordScale = MathF.Max(xMax - xMin, yMax - yMin);

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
                    bezier.points[i].SetX((float)x / coordScale);
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
                    bezier.points[i].SetY((float)y / coordScale);
                    flagIndex++;
                }
            }

            glyph.BuildEdges();
            int colorIndex = 0;
            for (int i = 0; i < glyph.edgeContours.Count; i++)
            {
                List<Edge> edges = glyph.edgeContours[i];
                if (edges.Count == 0) continue;

                for (int j = 0; j < edges.Count; j++)
                {
                    Edge prev = edges[(j - 1 + edges.Count) % edges.Count];
                    Edge current = edges[j];
                    // Entry direction of current edge: B'(0) = 2(C - P0)
                    Vector2D<float> dirIn = current.control - current.p0;
                    // Exit direction of previous edge: B'(1) = 2(P1 - C)
                    Vector2D<float> dirOut = prev.p1 - prev.control;

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

            return glyph;
        }

        private static void GenerateMTSDF(Glyph glyph, ref Image<Rgba32> image, int startX, int startY, int glyphsPerAxis, float distanceFactor)
        {
            int cellSize = image.Width / glyphsPerAxis;

            float scale = MathF.Max(glyph.xMax - glyph.xMin, glyph.yMax - glyph.yMin);
            float normW = (glyph.xMax - glyph.xMin) / scale;
            float normH = (glyph.yMax - glyph.yMin) / scale;

            int pad = 2;
            int innerSize = cellSize - pad * 2;
            int spreadPx = innerSize / 8;
            float spreadU = (spreadPx / (float)innerSize) * normW;
            float spreadV = (spreadPx / (float)innerSize) * normH;

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

                    image[startX + x, startY + y] = new Rgba32(redDist, greenDist, blueDist, trueDist);
                }
            }
        }

        private static float GetClosestDistanceOfChannel(Vector2D<float> p, Glyph glyph, Vector3D<int> channel)
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
            int samples = 24;
            float bestT = 0f;
            float bestDist = Vector2D.DistanceSquared(p, edge.p0);

            float d1Sq = Vector2D.DistanceSquared(p, edge.p1);
            if (d1Sq < bestDist) { bestDist = d1Sq; bestT = 1f; }

            for (int i = 1; i < samples; i++)
            {
                float t = (float)i / samples;
                float omt = 1f - t;
                Vector2D<float> pt = omt * omt * edge.p0 + 2f * omt * t * edge.control + t * t * edge.p1;
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
                Vector2D<float> bt = omt * omt * edge.p0 + 2f * omt * t2 * edge.control + t2 * t2 * edge.p1;
                // B'(t) = 2(1-t)(C-P0) + 2t(P1-C)
                Vector2D<float> bt1 = 2f * omt * (edge.control - edge.p0) + 2f * t2 * (edge.p1 - edge.control);
                // B''(t) = 2(P0 - 2C + P1)  (constant)
                Vector2D<float> bt2 = 2f * (edge.p0 - 2f * edge.control + edge.p1);

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
            Vector2D<float> refined = omt2 * omt2 * edge.p0 + 2f * omt2 * t2 * edge.control + t2 * t2 * edge.p1;
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

                    // Quadratic bezier: B(t) = (1-t)^2*P0 + 2(1-t)t*C + t^2*P1
                    // Solve B_y(t) = p.Y for t
                    // (P0.Y - 2*C.Y + P1.Y)t^2 + 2(C.Y - P0.Y)t + (P0.Y - p.Y) = 0

                    float ay = edge.p0.Y - 2f * edge.control.Y + edge.p1.Y;
                    float by = 2f * (edge.control.Y - edge.p0.Y);
                    float cy = edge.p0.Y - p.Y;

                    float[] roots = SolveQuadratic(ay, by, cy);

                    for (int i = 0; i < roots.Length; i++)
                    {
                        float t = roots[i];
                        if (t < 0f || t >= 1f) continue;

                        // X position of curve at this t
                        float omt = 1f - t;
                        float bx = omt * omt * edge.p0.X + 2f * omt * t * edge.control.X + t * t * edge.p1.X;

                        // Only count crossings to the right of p (ray casting rightward)
                        if (bx <= p.X) continue;

                        // Curve's Y derivative at t: B'_y(t) = 2(ay*t + by/2)
                        float dy = 2f * ay * t + by;

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

    [@Serializable]
    public class AtlasMetaData : IDeserialize
    {
        [@Serializable]
        public int glyphCount;
        [@Serializable]
        public char[] chars;
        [@Serializable]
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

                    glyphs[i].advanceWidth = reader.ReadSingle();
                    glyphs[i].leftSideOffset = reader.ReadSingle();
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