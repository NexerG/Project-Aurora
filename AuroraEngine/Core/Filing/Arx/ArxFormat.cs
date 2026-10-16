using ArctisAurora.Core.Security;
using System.IO.MemoryMappedFiles;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace ArctisAurora.Core.Filing.Arx
{
    public enum ArxKind : byte
    {
        Blob = 1,
        Xml = 2,
    }

    [Flags]
    public enum ArxEntryFlags : byte
    {
        None = 0,
        Compressed = 1,
    }

    // header: magic | version | flags | tocOffset | tocLength | signatureLength
    internal static class ArxLayout
    {
        public const uint magic = 0x00585241;
        public const ushort version = 1;
        public const int headerSize = 24;
        public const int alignment = 16;
        public static readonly byte[] tocAssociated = Encoding.ASCII.GetBytes("Aurora.Arx.Toc");
    }

    // Builds an archive: entries in, one signed and encrypted .arx out.
    public sealed class ArxWriter
    {
        private readonly ArxStrings _strings = new ArxStrings();
        private readonly List<(string path, ArxKind kind, byte[] data)> _entries = new List<(string, ArxKind, byte[])>();

        public int Count => _entries.Count;

        public void AddBlob(string path, byte[] bytes) => _entries.Add((Normalize(path), ArxKind.Blob, bytes));

        public void AddXml(string path, XElement root)
        {
            using MemoryStream buffer = new MemoryStream();
            using (BinaryWriter writer = new BinaryWriter(buffer, Encoding.UTF8, true))
                ArxXml.Encode(root, _strings, writer);
            _entries.Add((Normalize(path), ArxKind.Xml, buffer.ToArray()));
        }

        public void Write(string file, ArxKeys keys)
        {
            using FileStream stream = new FileStream(file, FileMode.Create, FileAccess.Write, FileShare.None);
            stream.Position = Align(ArxLayout.headerSize);

            List<(int pathId, ArxKind kind, long offset, byte[] stored)> placed = new List<(int, ArxKind, long, byte[])>();
            foreach ((string path, ArxKind kind, byte[] data) in _entries)
            {
                byte[] stored = Cipher.Encrypt(keys.key, data, Encoding.UTF8.GetBytes(path));
                long offset = stream.Position;
                stream.Write(stored);
                stream.Position = Align(stream.Position);
                placed.Add((_strings.Id(path), kind, offset, stored));
            }

            using MemoryStream plain = new MemoryStream();
            using (BinaryWriter toc = new BinaryWriter(plain, Encoding.UTF8, true))
            {
                toc.Write(_strings.list.Count);
                foreach (string s in _strings.list) toc.Write(s);
                toc.Write(placed.Count);
                foreach ((int pathId, ArxKind kind, long offset, byte[] stored) in placed)
                {
                    toc.Write7BitEncodedInt(pathId);
                    toc.Write((byte)kind);
                    toc.Write((byte)ArxEntryFlags.None);
                    toc.Write(offset);
                    toc.Write(stored.Length);
                    toc.Write(SHA256.HashData(stored));
                }
            }
            byte[] tocBlob = Cipher.Encrypt(keys.key, plain.ToArray(), ArxLayout.tocAssociated);
            long tocOffset = stream.Position;
            stream.Write(tocBlob);

            byte[] header = new byte[ArxLayout.headerSize];
            using (BinaryWriter h = new BinaryWriter(new MemoryStream(header)))
            {
                h.Write(ArxLayout.magic);
                h.Write(ArxLayout.version);
                h.Write((ushort)0);
                h.Write(tocOffset);
                h.Write(tocBlob.Length);
                h.Write(ArxArchive.signatureSize);
            }

            using ECDsa ecdsa = ECDsa.Create();
            ecdsa.ImportPkcs8PrivateKey(keys.signingKey, out _);
            byte[] signature = ecdsa.SignData(ArxArchive.Signed(header, tocBlob), HashAlgorithmName.SHA256);
            stream.Write(signature);

            stream.Position = 0;
            stream.Write(header);
        }

        private static long Align(long position) => (position + ArxLayout.alignment - 1) & ~(long)(ArxLayout.alignment - 1);

        public static string Normalize(string path) => path.Replace('\\', '/').TrimStart('/');
    }

    // A mounted archive: signature checked and table read at open, each entry checked and decrypted when read.
    public sealed class ArxArchive : IDisposable
    {
        public const int signatureSize = 64;

        private readonly struct Entry
        {
            public readonly string path;
            public readonly ArxKind kind;
            public readonly long offset;
            public readonly int length;
            public readonly byte[] hash;

            public Entry(string path, ArxKind kind, long offset, int length, byte[] hash)
            {
                this.path = path;
                this.kind = kind;
                this.offset = offset;
                this.length = length;
                this.hash = hash;
            }
        }

        public readonly string file;
        private readonly byte[] _key;
        private readonly MemoryMappedFile _map;
        private readonly MemoryMappedViewAccessor _view;
        private readonly string[] _strings;
        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        public IEnumerable<string> Paths => _entries.Keys;

        private ArxArchive(string file, byte[] key, MemoryMappedFile map, MemoryMappedViewAccessor view, string[] strings)
        {
            this.file = file;
            _key = key;
            _map = map;
            _view = view;
            _strings = strings;
        }

        // Opens and verifies an archive; throws InvalidDataException when it is not one these keys built.
        public static ArxArchive Open(string file, ArxKeys keys)
        {
            file = Path.GetFullPath(file);
            long size = new FileInfo(file).Length;
            if (size < ArxLayout.headerSize) throw new InvalidDataException($"{file} is not an archive.");

            MemoryMappedFile map = MemoryMappedFile.CreateFromFile(file, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
            MemoryMappedViewAccessor view = map.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            try
            {
                byte[] header = Read(view, 0, ArxLayout.headerSize);
                long tocOffset;
                int tocLength, signatureLength;
                using (BinaryReader h = new BinaryReader(new MemoryStream(header)))
                {
                    if (h.ReadUInt32() != ArxLayout.magic) throw new InvalidDataException($"{file} is not an archive.");
                    if (h.ReadUInt16() != ArxLayout.version) throw new InvalidDataException($"{file} is an unsupported archive version.");
                    h.ReadUInt16();
                    tocOffset = h.ReadInt64();
                    tocLength = h.ReadInt32();
                    signatureLength = h.ReadInt32();
                }
                if (tocOffset < ArxLayout.headerSize || tocLength <= 0 || signatureLength != signatureSize || tocOffset + tocLength + signatureLength != size)
                    throw new InvalidDataException($"{file} has a damaged header.");

                byte[] tocBlob = Read(view, tocOffset, tocLength);
                byte[] signature = Read(view, tocOffset + tocLength, signatureLength);
                using (ECDsa ecdsa = ECDsa.Create())
                {
                    ecdsa.ImportSubjectPublicKeyInfo(keys.publicKey, out _);
                    if (!ecdsa.VerifyData(Signed(header, tocBlob), signature, HashAlgorithmName.SHA256))
                        throw new InvalidDataException($"{file} failed its signature check.");
                }
                if (!Cipher.TryDecrypt(keys.key, tocBlob, out byte[] plain, ArxLayout.tocAssociated))
                    throw new InvalidDataException($"{file} could not be decrypted with this app's key.");

                using BinaryReader toc = new BinaryReader(new MemoryStream(plain), Encoding.UTF8);
                string[] strings = new string[toc.ReadInt32()];
                for (int i = 0; i < strings.Length; i++) strings[i] = toc.ReadString();

                ArxArchive archive = new ArxArchive(file, keys.key, map, view, strings);
                int count = toc.ReadInt32();
                for (int i = 0; i < count; i++)
                {
                    string path = strings[toc.Read7BitEncodedInt()];
                    ArxKind kind = (ArxKind)toc.ReadByte();
                    toc.ReadByte();
                    long offset = toc.ReadInt64();
                    int length = toc.ReadInt32();
                    byte[] hash = toc.ReadBytes(32);
                    archive._entries[path] = new Entry(path, kind, offset, length, hash);
                }
                return archive;
            }
            catch
            {
                view.Dispose();
                map.Dispose();
                throw;
            }
        }

        public bool Contains(string path) => _entries.ContainsKey(ArxWriter.Normalize(path));

        public bool IsXml(string path) => _entries.TryGetValue(ArxWriter.Normalize(path), out Entry e) && e.kind == ArxKind.Xml;

        // An entry's bytes; an XML entry comes back as UTF-8 text.
        public byte[] ReadBytes(string path)
        {
            Entry entry = Find(path);
            byte[] plain = Decrypt(entry);
            return entry.kind == ArxKind.Xml ? Encoding.UTF8.GetBytes(Decode(plain).ToString(SaveOptions.DisableFormatting)) : plain;
        }

        public XElement ReadXml(string path)
        {
            Entry entry = Find(path);
            byte[] plain = Decrypt(entry);
            return entry.kind == ArxKind.Xml ? Decode(plain) : XElement.Load(new MemoryStream(plain));
        }

        public void Dispose()
        {
            _view.Dispose();
            _map.Dispose();
        }

        private Entry Find(string path)
        {
            if (_entries.TryGetValue(ArxWriter.Normalize(path), out Entry entry)) return entry;
            throw new FileNotFoundException($"{file} has no entry {path}.");
        }

        private byte[] Decrypt(Entry entry)
        {
            byte[] stored = Read(_view, entry.offset, entry.length);
            if (!SHA256.HashData(stored).AsSpan().SequenceEqual(entry.hash) ||
                !Cipher.TryDecrypt(_key, stored, out byte[] plain, Encoding.UTF8.GetBytes(entry.path)))
                throw new InvalidDataException($"{file}: entry {entry.path} is damaged or was modified.");
            return plain;
        }

        private XElement Decode(byte[] plain)
        {
            using BinaryReader reader = new BinaryReader(new MemoryStream(plain), Encoding.UTF8);
            return ArxXml.Decode(reader, _strings);
        }

        private static byte[] Read(MemoryMappedViewAccessor view, long offset, int length)
        {
            byte[] bytes = new byte[length];
            view.ReadArray(offset, bytes, 0, length);
            return bytes;
        }

        internal static byte[] Signed(byte[] header, byte[] tocBlob)
        {
            byte[] signed = new byte[header.Length + tocBlob.Length];
            header.CopyTo(signed, 0);
            tocBlob.CopyTo(signed, header.Length);
            return signed;
        }
    }
}
