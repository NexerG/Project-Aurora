using ArctisAurora.Core.Diagnostics;
using ArctisAurora.Core.Filing;
using ArctisAurora.EngineWork;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace ArctisAurora.Core.Security
{
    // A vault encrypted under one master key. <vault>/.aurora/Vault.lock.axml holds that key wrapped once per way in:
    // <VaultLock Version Id State><Slot Kind="Password" Kdf… Salt…>…</Slot><Slot Kind="Recovery" Salt>…</Slot></VaultLock>.
    public static class VaultLock
    {
        private static readonly LogChannel Log = LogChannel.For("Encryption");

        public const string folderName = ".aurora";
        private const string fileName = "Vault.lock.axml";

        // states of the lock file
        private const string locked = "Locked";
        private const string encryptingState = "Encrypting";
        private const string decryptingState = "Decrypting";

        private static readonly byte[] slotAssociated = "Aurora.VaultSlot"u8.ToArray();
        private static readonly byte[] recoveryInfo = "Aurora.Recovery"u8.ToArray();
        private static readonly byte[] helloInfo = "Aurora.Hello"u8.ToArray();
        private const string base32 = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        private const int recoverySize = 32;

        // the open vault and its master key
        private static string? root;
        private static byte[]? master;

        // files still to convert, and which way
        private static Queue<string>? converting;
        private static bool encrypting;
        private static int failed;
        private const double stepMs = 8.0;

        public static bool isConverting => converting != null;

        public static string LockPath(string vault) => Path.Combine(vault, folderName, fileName);

        public static bool HasLock(string vault) => File.Exists(LockPath(vault));

        public static bool IsOpen(string vault) => master != null && root != null && Same(root, vault);

        // The master key for a file in the open vault; null outside it or while none is open.
        public static byte[]? KeyFor(string path) => master != null && Inside(path) ? master : null;

        // Whether a write to the path is sealed: inside the open vault, unless the vault is being decrypted.
        public static bool Covers(string path) => master != null && !(converting != null && !encrypting) && Inside(path);

        // A path inside an encrypted vault that is not the open one, where nothing may be written.
        public static bool Refuses(string path)
        {
            for (string? folder = Path.GetDirectoryName(Path.GetFullPath(path)); folder != null; folder = Path.GetDirectoryName(folder))
                if (HasLock(folder)) return !IsOpen(folder);
            return false;
        }

        #region ---- slots ----
        // Locks the open vault: a new master key wrapped by the password and the recovery key, then every file encrypted.
        public static void Create(string vault, PasswordKey password, byte[] recovery)
        {
            byte[] key = Cipher.NewKey();
            XElement file = new XElement("VaultLock",
                new XAttribute("Version", 1), new XAttribute("Id", Guid.NewGuid().ToString("N")), new XAttribute("State", encryptingState),
                PasswordSlot(password, key), RecoverySlot(recovery, key));
            WriteLock(vault, file);
            Use(vault, key);
        }

        // The master key when the secret is the password or the recovery key; null when it is neither. Safe off the main thread.
        public static byte[]? Open(string vault, string secret)
        {
            XElement file = XElement.Load(LockPath(vault));
            byte[]? recovery = ParseRecovery(secret);
            foreach (XElement slot in file.Elements("Slot"))
            {
                byte[] wrapped = Convert.FromBase64String(slot.Value);
                switch ((string?)slot.Attribute("Kind"))
                {
                    case "Recovery" when recovery != null:
                        byte[] slotKey = RecoveryKey(recovery, Convert.FromBase64String((string?)slot.Attribute("Salt") ?? ""));
                        if (Cipher.TryDecrypt(slotKey, wrapped, out byte[] fromRecovery, slotAssociated)) return fromRecovery;
                        break;
                    case "Password" when recovery == null:
                        using (PasswordKey? password = PasswordKey.FromAttributes(slot, secret))
                            if (password != null && Cipher.TryDecrypt(password.key, wrapped, out byte[] fromPassword, slotAssociated)) return fromPassword;
                        break;
                }
            }
            return null;
        }

        // Replaces the open vault's password slot.
        public static void ChangePassword(string vault, PasswordKey password)
        {
            if (!IsOpen(vault)) return;
            XElement file = XElement.Load(LockPath(vault));
            file.Elements("Slot").Where(s => (string?)s.Attribute("Kind") == "Password").Remove();
            file.Add(PasswordSlot(password, master!));
            WriteLock(vault, file);
        }

        // The vault's Windows Hello credential name and id when it has a Hello slot.
        public static (string credential, string id)? HelloOf(string vault)
        {
            if (!HasLock(vault)) return null;
            XElement file = XElement.Load(LockPath(vault));
            XElement? slot = file.Elements("Slot").FirstOrDefault(s => (string?)s.Attribute("Kind") == "WindowsHello");
            return slot == null ? null : ((string?)slot.Attribute("Credential") ?? "", (string?)file.Attribute("Id") ?? "");
        }

        public static string IdOf(string vault) => (string?)XElement.Load(LockPath(vault)).Attribute("Id") ?? "";

        public static string HelloCredential(string vault) => "Aurora.Vault." + IdOf(vault);

        // Adds a Windows Hello slot to the open vault from the credential's signature, replacing any earlier one.
        public static void AddHello(string vault, string credential, byte[] signature)
        {
            if (!IsOpen(vault)) return;
            XElement file = XElement.Load(LockPath(vault));
            file.Elements("Slot").Where(s => (string?)s.Attribute("Kind") == "WindowsHello").Remove();
            byte[] salt = RandomNumberGenerator.GetBytes(16);
            file.Add(new XElement("Slot", new XAttribute("Kind", "WindowsHello"), new XAttribute("Credential", credential), new XAttribute("Salt", Convert.ToBase64String(salt)),
                Convert.ToBase64String(Cipher.Encrypt(HelloKey(signature, salt), master!, slotAssociated))));
            WriteLock(vault, file);
        }

        // The master key when the signature opens the Hello slot; null when it does not.
        public static byte[]? OpenWithHello(string vault, byte[] signature)
        {
            XElement? slot = XElement.Load(LockPath(vault)).Elements("Slot").FirstOrDefault(s => (string?)s.Attribute("Kind") == "WindowsHello");
            if (slot == null) return null;
            byte[] key = HelloKey(signature, Convert.FromBase64String((string?)slot.Attribute("Salt") ?? ""));
            return Cipher.TryDecrypt(key, Convert.FromBase64String(slot.Value), out byte[] opened, slotAssociated) ? opened : null;
        }

        private static byte[] HelloKey(byte[] signature, byte[] salt) =>
            HKDF.DeriveKey(HashAlgorithmName.SHA256, signature, Cipher.keySize, salt, helloInfo);

        private static XElement PasswordSlot(PasswordKey password, byte[] key) =>
            new XElement("Slot", new XAttribute("Kind", "Password"), password.Attributes(),
                Convert.ToBase64String(Cipher.Encrypt(password.key, key, slotAssociated)));

        private static XElement RecoverySlot(byte[] recovery, byte[] key)
        {
            byte[] salt = RandomNumberGenerator.GetBytes(16);
            return new XElement("Slot", new XAttribute("Kind", "Recovery"), new XAttribute("Salt", Convert.ToBase64String(salt)),
                Convert.ToBase64String(Cipher.Encrypt(RecoveryKey(recovery, salt), key, slotAssociated)));
        }

        private static byte[] RecoveryKey(byte[] recovery, byte[] salt) =>
            HKDF.DeriveKey(HashAlgorithmName.SHA256, recovery, Cipher.keySize, salt, recoveryInfo);

        private static void WriteLock(string vault, XElement file)
        {
            string path = LockPath(vault);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using MemoryStream bytes = new MemoryStream();
            file.Save(bytes);
            EncryptedFile.WriteWhole(path, bytes.ToArray());
        }
        #endregion

        #region ---- recovery key ----
        public static byte[] NewRecovery() => RandomNumberGenerator.GetBytes(recoverySize);

        // Base32 in dash-separated groups of four.
        public static string FormatRecovery(byte[] recovery)
        {
            StringBuilder text = new StringBuilder();
            int buffer = 0, bits = 0, written = 0;
            foreach (byte b in recovery)
            {
                buffer = (buffer << 8) | b;
                bits += 8;
                while (bits >= 5)
                {
                    Group(text, ref written);
                    text.Append(base32[(buffer >> (bits - 5)) & 31]);
                    bits -= 5;
                }
            }
            if (bits > 0)
            {
                Group(text, ref written);
                text.Append(base32[(buffer << (5 - bits)) & 31]);
            }
            return text.ToString();
        }

        private static void Group(StringBuilder text, ref int written)
        {
            if (written > 0 && written % 4 == 0) text.Append('-');
            written++;
        }

        // The key a recovery text spells, dashes and spaces ignored; null when the text is not one.
        public static byte[]? ParseRecovery(string text)
        {
            string letters = new string(text.Where(c => c != '-' && !char.IsWhiteSpace(c)).Select(char.ToUpperInvariant).ToArray());
            if (letters.Length != (recoverySize * 8 + 4) / 5) return null;

            byte[] bytes = new byte[recoverySize];
            int buffer = 0, bits = 0, at = 0;
            foreach (char c in letters)
            {
                int value = base32.IndexOf(c);
                if (value < 0) return null;
                buffer = (buffer << 5) | value;
                bits += 5;
                if (bits < 8) continue;
                if (at < bytes.Length) bytes[at++] = (byte)(buffer >> (bits - 8));
                bits -= 8;
            }
            return at == bytes.Length ? bytes : null;
        }
        #endregion

        #region ---- open vault ----
        // Holds the master key for the vault and finishes a conversion the lock file says was cut short.
        public static void Use(string vault, byte[] key)
        {
            Close();
            root = Path.GetFullPath(vault);
            master = key;

            string? state = (string?)XElement.Load(LockPath(vault)).Attribute("State");
            if (state == encryptingState) StartConversion(true);
            else if (state == decryptingState) StartConversion(false);
        }

        // Decrypts every file of the open vault, then drops its lock.
        public static void Remove(string vault)
        {
            if (!IsOpen(vault)) return;
            SetState(decryptingState);
            StartConversion(false);
        }

        // Zeroes the master key.
        public static void Close()
        {
            if (master != null) CryptographicOperations.ZeroMemory(master);
            master = null;
            root = null;
            converting = null;
        }

        private static void SetState(string state)
        {
            XElement file = XElement.Load(LockPath(root!));
            file.SetAttributeValue("State", state);
            WriteLock(root!, file);
        }

        private static bool Inside(string path)
        {
            if (root == null) return false;
            string full = Path.GetFullPath(path);
            string prefix = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
            return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && !full.StartsWith(prefix + folderName + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        private static bool Same(string a, string b) =>
            string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);
        #endregion

        #region ---- conversion ----
        // Every file of the open vault, a slice per main tick, each swapped in whole so an interrupted run resumes.
        private static void StartConversion(bool encrypt)
        {
            FileWriteQueue.Drain();
            encrypting = encrypt;
            failed = 0;
            converting = new Queue<string>(Directory.EnumerateFiles(root!, "*", SearchOption.AllDirectories).Where(Inside));
            Engine.Post(Step);
        }

        private static void Step()
        {
            if (converting == null || master == null) return;

            Stopwatch clock = Stopwatch.StartNew();
            while (converting.Count > 0 && clock.Elapsed.TotalMilliseconds < stepMs)
            {
                string path = converting.Dequeue();
                try
                {
                    ConvertFile(path);
                }
                catch (Exception e)
                {
                    failed++;
                    Log.Warn($"'{path}' was not {(encrypting ? "encrypted" : "decrypted")}: {e.Message}");
                }
            }

            if (converting.Count > 0)
            {
                Task.Delay(1).ContinueWith(_ => Engine.Post(Step));
                return;
            }
            Finish();
        }

        private static void ConvertFile(string path)
        {
            if (!File.Exists(path) || path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) return;

            byte[] bytes = File.ReadAllBytes(path);
            if (encrypting)
            {
                if (!EncryptedFile.IsEnvelope(bytes)) EncryptedFile.WriteVault(path, master!, bytes);
                return;
            }
            if (EncryptedFile.IsVaultEnvelope(bytes) && EncryptedFile.TryReadVault(bytes, master!, out byte[] plain))
                EncryptedFile.WriteWhole(path, plain);
        }

        private static void Finish()
        {
            converting = null;
            if (failed > 0)
            {
                Log.Warn($"{failed} file(s) were not converted; the next unlock tries them again.");
                return;
            }

            if (encrypting)
            {
                SetState(locked);
                Log.Info($"vault '{root}' is encrypted.");
                return;
            }

            string vault = root!;
            if (HelloOf(vault) is (string credential, _)) _ = WindowsHello.Delete(credential);
            File.Delete(LockPath(vault));
            string folder = Path.Combine(vault, folderName);
            if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder);
            Close();
            Log.Info($"vault '{vault}' is no longer encrypted.");
        }
        #endregion
    }
}
