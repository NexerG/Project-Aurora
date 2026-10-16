using ArctisAurora.Core.Security;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace ArctisAurora.Core.Filing
{
    // Reads and writes of vault files: an encrypted vault's envelopes opened on read and its writes sealed; plain elsewhere.
    public static class VaultFiles
    {
        public static byte[] ReadBytes(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (!EncryptedFile.IsVaultEnvelope(bytes)) return bytes;

            byte[] master = VaultLock.KeyFor(path) ?? throw new LockedFileException(path);
            return EncryptedFile.TryReadVault(bytes, master, out byte[] plain) ? plain : throw new LockedFileException(path);
        }

        // Decoded as File.ReadAllText does, by byte-order mark or else UTF-8.
        public static string ReadText(string path)
        {
            using StreamReader reader = new StreamReader(new MemoryStream(ReadBytes(path)), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }

        public static Stream OpenRead(string path) => new MemoryStream(ReadBytes(path));

        // An envelope already sealed is written as it is.
        public static void WriteBytes(string path, byte[] bytes)
        {
            if (VaultLock.Refuses(path)) throw new LockedFileException(path);
            if (VaultLock.Covers(path) && !EncryptedFile.IsEnvelope(bytes)) EncryptedFile.WriteVault(path, VaultLock.KeyFor(path)!, bytes);
            else File.WriteAllBytes(path, bytes);
        }

        public static void WriteText(string path, string text, Encoding encoding) =>
            WriteBytes(path, [.. encoding.GetPreamble(), .. encoding.GetBytes(text)]);

        public static void WriteText(string path, string text) => WriteText(path, text, new UTF8Encoding(false));

        // Refuses an existing target, as File.Copy does.
        public static void Copy(string from, string to)
        {
            if (File.Exists(to)) throw new IOException($"'{to}' already exists.");
            WriteBytes(to, ReadBytes(from));
        }

        // Under the file's own password when it has one, else sealed by the vault; plain otherwise, through settings when given.
        public static void SaveXml(XDocument file, string path, PasswordKey? key, XmlWriterSettings? settings)
        {
            if (key != null)
            {
                EncryptedFile.SaveXml(file, path, key, settings ?? new XmlWriterSettings { Indent = true });
                return;
            }
            if (VaultLock.Refuses(path)) throw new LockedFileException(path);
            if (VaultLock.Covers(path))
            {
                EncryptedFile.WriteVault(path, VaultLock.KeyFor(path)!, EncryptedFile.Bytes(file, settings ?? new XmlWriterSettings { Indent = true }));
                return;
            }
            if (settings == null)
            {
                file.Save(path);
                return;
            }
            using XmlWriter writer = XmlWriter.Create(path, settings);
            file.Save(writer);
        }
    }
}
