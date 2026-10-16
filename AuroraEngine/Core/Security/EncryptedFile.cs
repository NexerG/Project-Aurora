using System.Xml;
using System.Xml.Linq;

namespace ArctisAurora.Core.Security
{
    // A file whose whole content is one Cipher blob: under a password, <Encrypted Kdf… Salt…>base64</Encrypted>,
    // or under its vault's master key, <Encrypted Key="Vault">base64</Encrypted>.
    public static class EncryptedFile
    {
        public const string rootName = "Encrypted";
        private const string vaultKey = "Vault";

        private static readonly byte[] associated = "Aurora.File"u8.ToArray();
        private static readonly byte[] vaultAssociated = "Aurora.VaultFile"u8.ToArray();

        // A password-encrypted file; a vault envelope is not one.
        public static bool IsEncrypted(string path)
        {
            if (!File.Exists(path)) return false;
            try
            {
                using XmlReader reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore });
                return reader.MoveToContent() == XmlNodeType.Element && reader.LocalName == rootName && reader.GetAttribute("Key") != vaultKey;
            }
            catch (XmlException)
            {
                return false;
            }
        }

        // The envelope's Key attribute when the bytes are an envelope of either kind; null when they are not one.
        private static string? EnvelopeOf(byte[] bytes)
        {
            try
            {
                using XmlReader reader = XmlReader.Create(new MemoryStream(bytes), new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore });
                return reader.MoveToContent() == XmlNodeType.Element && reader.LocalName == rootName ? reader.GetAttribute("Key") ?? "" : null;
            }
            catch (XmlException)
            {
                return null;
            }
        }

        public static bool IsEnvelope(byte[] bytes) => EnvelopeOf(bytes) != null;

        public static bool IsVaultEnvelope(byte[] bytes) => EnvelopeOf(bytes) == vaultKey;

        // The key when the password opens the file; null when it does not.
        public static PasswordKey? Unlock(string path, string password)
        {
            XElement envelope = XElement.Load(path);
            PasswordKey? key = PasswordKey.FromAttributes(envelope, password);
            if (key == null) return null;
            if (Cipher.TryDecrypt(key.key, Convert.FromBase64String(envelope.Value), out _, associated)) return key;

            key.Dispose();
            return null;
        }

        // A plain file as it is; a vault envelope opened with the open vault's key; a password one with key, or the key
        // FileKeys holds, which is then returned in key.
        public static XDocument LoadXml(string path, LoadOptions options, ref PasswordKey? key)
        {
            XDocument document = XDocument.Load(path, options);
            if (document.Root?.Name.LocalName != rootName) return document;

            byte[] blob = Convert.FromBase64String(document.Root.Value);
            byte[] plain;
            if ((string?)document.Root.Attribute("Key") == vaultKey)
            {
                byte[] master = VaultLock.KeyFor(path) ?? throw new LockedFileException(path);
                if (!Cipher.TryDecrypt(master, blob, out plain, vaultAssociated)) throw new LockedFileException(path);
                return XDocument.Load(new MemoryStream(plain), options);
            }

            key ??= FileKeys.Take(path) ?? throw new LockedFileException(path);
            if (!Cipher.TryDecrypt(key.key, blob, out plain, associated)) throw new LockedFileException(path);
            return XDocument.Load(new MemoryStream(plain), options);
        }

        public static bool TryRead(string path, PasswordKey key, out byte[] content) =>
            Cipher.TryDecrypt(key.key, Convert.FromBase64String(XElement.Load(path).Value), out content, associated);

        public static bool TryReadVault(byte[] envelope, byte[] master, out byte[] content) =>
            Cipher.TryDecrypt(master, Convert.FromBase64String(XElement.Load(new MemoryStream(envelope)).Value), out content, vaultAssociated);

        public static void SaveXml(XDocument document, string path, PasswordKey key, XmlWriterSettings settings) =>
            Write(path, key, Bytes(document, settings));

        public static byte[] Bytes(XDocument document, XmlWriterSettings settings)
        {
            using MemoryStream plain = new MemoryStream();
            using (XmlWriter writer = XmlWriter.Create(plain, settings)) document.Save(writer);
            return plain.ToArray();
        }

        public static void Write(string path, PasswordKey key, byte[] content) =>
            WriteEnvelope(path, new XElement(rootName, key.Attributes(), Convert.ToBase64String(Cipher.Encrypt(key.key, content, associated))));

        public static void WriteVault(string path, byte[] master, byte[] content) =>
            WriteEnvelope(path, new XElement(rootName, new XAttribute("Key", vaultKey), Convert.ToBase64String(Cipher.Encrypt(master, content, vaultAssociated))));

        // Writes bytes through a temp file beside the target, swapped in whole.
        public static void WriteWhole(string path, byte[] content)
        {
            string temp = path + ".tmp";
            using (FileStream stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(content);
                stream.Flush(true);
            }
            File.Move(temp, path, overwrite: true);
        }

        private static void WriteEnvelope(string path, XElement envelope)
        {
            using MemoryStream bytes = new MemoryStream();
            envelope.Save(bytes);
            WriteWhole(path, bytes.ToArray());
        }
    }
}
