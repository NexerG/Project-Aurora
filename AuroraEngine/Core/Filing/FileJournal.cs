using ArctisAurora.Core.Diagnostics;
using ArctisAurora.Core.Filing.Serialization;
using System.Security.Cryptography;
using System.Text;

namespace ArctisAurora.Core.Filing
{
    // In-place writes that survive a crash: a batch is recorded under Paths.JOURNAL, applied to its file, then cleared.
    public sealed class FileJournal : IDisposable
    {
        private static readonly LogChannel Log = LogChannel.For("Filing");

        // record header: magic, SHA-256 of the body, body length
        private const int magic = 0x4C4E4A41;
        private const int headerBytes = 4 + 32 + 4;

        private static readonly HashSet<string> held = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private readonly string path;
        private readonly string journal;
        private readonly FileStream record;

        private FileJournal(string path)
        {
            this.path = Path.GetFullPath(path);
            journal = JournalOf(path);
            Directory.CreateDirectory(Paths.JOURNAL);
            record = new FileStream(journal, FileMode.Create, FileAccess.Write, FileShare.None);
            lock (held) held.Add(this.path);
        }

        // A journal for path, held until disposed.
        public static FileJournal Open(string path) => new FileJournal(path);

        // Writes each (offset, bytes) into path and leaves it length bytes long, in one batch.
        public static void Commit(string path, List<(long offset, byte[] bytes)> writes, long length)
        {
            using FileJournal journal = Open(path);
            journal.Commit(writes, length);
        }

        public void Commit(List<(long offset, byte[] bytes)> writes, long length)
        {
            Record(writes, length);
            Apply(path, writes, length);
            record.Position = 0;
            record.Write(stackalloc byte[4]);
            record.Flush(false);
        }

        // Finishes a batch a crash left recorded for path; true when one was applied.
        public static bool Replay(string path)
        {
            lock (held)
                if (held.Contains(Path.GetFullPath(path))) return false;

            string journal = JournalOf(path);
            if (!File.Exists(journal)) return false;

            Read(journal, path, out bool cleared, out List<(long offset, byte[] bytes)>? writes, out long length);
            if (writes != null) Apply(path, writes, length);
            else if (!cleared) Log.Warn($"Dropped an unfinished write journal for {path}");
            File.Delete(journal);
            return writes != null;
        }

        // Records a batch and leaves it unapplied, as a crash right after recording would; returns the journal file.
        internal static string Record(string path, List<(long offset, byte[] bytes)> writes, long length)
        {
            FileJournal journal = new FileJournal(path);
            journal.Record(writes, length);
            journal.record.Dispose();
            lock (held) held.Remove(journal.path);
            return journal.journal;
        }

        public void Dispose()
        {
            record.Dispose();
            File.Delete(journal);
            lock (held) held.Remove(path);
        }

        private void Record(List<(long offset, byte[] bytes)> writes, long length)
        {
            using MemoryStream body = new MemoryStream();
            using (BinaryWriter writer = new BinaryWriter(body, Encoding.UTF8, true))
            {
                writer.Write(path);
                writer.Write(length);
                writer.Write(writes.Count);
                foreach ((long offset, byte[] bytes) in writes)
                {
                    writer.Write(offset);
                    writer.Write(bytes.Length);
                    writer.Write(bytes);
                }
            }

            byte[] buffer = body.GetBuffer();
            int count = (int)body.Length;
            record.Position = 0;
            using (BinaryWriter writer = new BinaryWriter(record, Encoding.UTF8, true))
            {
                writer.Write(magic);
                writer.Write(SHA256.HashData(buffer.AsSpan(0, count)));
                writer.Write(count);
                writer.Write(buffer, 0, count);
            }
            record.Flush(true);
        }

        internal static string JournalOf(string path) => Path.Combine(Paths.JOURNAL,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant())))[..32] + ".journal");

        // A recorded batch, or null when the record is cleared, torn or for another file.
        private static void Read(string journal, string path, out bool cleared, out List<(long offset, byte[] bytes)>? writes, out long length)
        {
            cleared = false;
            writes = null;
            length = 0;

            byte[] file = File.ReadAllBytes(journal);
            if (file.Length < headerBytes) return;
            int mark = BitConverter.ToInt32(file, 0);
            cleared = mark == 0;
            if (mark != magic) return;

            int count = BitConverter.ToInt32(file, 36);
            if (count < 0 || headerBytes + count > file.Length) return;
            ReadOnlySpan<byte> body = file.AsSpan(headerBytes, count);
            if (!SHA256.HashData(body).AsSpan().SequenceEqual(file.AsSpan(4, 32))) return;

            using BinaryReader reader = new BinaryReader(new MemoryStream(file, headerBytes, count), Encoding.UTF8);
            if (!string.Equals(reader.ReadString(), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)) return;

            length = reader.ReadInt64();
            int entries = reader.ReadInt32();
            List<(long offset, byte[] bytes)> read = new List<(long offset, byte[] bytes)>(entries);
            for (int i = 0; i < entries; i++)
            {
                long offset = reader.ReadInt64();
                read.Add((offset, reader.ReadBytes(reader.ReadInt32())));
            }
            writes = read;
        }

        private static void Apply(string path, List<(long offset, byte[] bytes)> writes, long length)
        {
            using FileStream stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);
            foreach ((long offset, byte[] bytes) in writes)
            {
                stream.Position = offset;
                stream.Write(bytes);
            }
            stream.SetLength(length);
            stream.Flush(true);
        }
    }
}
