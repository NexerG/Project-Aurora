using ArctisAurora.Core.UISystem;
using Silk.NET.Maths;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using static ArctisAurora.Core.UISystem.AuroraFont;
using AuroraFont = ArctisAurora.Core.UISystem.AuroraFont;

namespace ArctisAurora.Core.Filing.Serialization
{
    public unsafe static class AssetImporter
    {
        public static void ImportFont(string characters, string fontName)
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

            font.textData = new TextData
            {
                characterCount = characters.Length,
                characters = characters.ToCharArray()
            };

            //font.headTableInfo = new HeadTableInfo();
            //reader.BaseStream.Position = font.tableEntries.First(t => t.name == "head").offset + 50;
            //font.headTableInfo.indexToLocFormat = ReadUInt16BE(reader); // 0 = uint16, 1 = uint32

            string baseName = fontName.Split('.')[0];
            string path = Paths.FONTS + $"\\{baseName}\\{baseName}" + ".afm";

            Serializer.SerializeAttributed(font, path);
            reader.Dispose();
            reader.Close();
            fs.Dispose();
            fs.Close();

            AuroraFont f = new AuroraFont();
            Serializer.DeserializeAttributed(path, ref f);
            //f.Deserialize(path);
            AuroraFont.GenerateGlyphAtlas(f, "arial.ttf", 64);
        }

        internal static short ReadInt16BE(BinaryReader reader) =>
            BitConverter.ToInt16(reader.ReadBytes(2).Reverse().ToArray(), 0);

        internal static ushort ReadUInt16BE(BinaryReader reader) =>
            BitConverter.ToUInt16(reader.ReadBytes(2).Reverse().ToArray(), 0);

        internal static uint ReadUInt32BE(BinaryReader reader) =>
            BitConverter.ToUInt32(reader.ReadBytes(4).Reverse().ToArray(), 0);

    }
}
