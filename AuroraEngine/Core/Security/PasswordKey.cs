using Konscious.Security.Cryptography;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace ArctisAurora.Core.Security
{
    // An Argon2id key derived from a password; its parameters and salt travel as XML attributes.
    public sealed class PasswordKey : IDisposable
    {
        public const string kdf = "Argon2id";

        // defaults for a new key
        public const int defaultMemoryKiB = 65536;
        public const int defaultPasses = 3;
        public const int defaultLanes = 4;
        private const int saltSize = 16;

        // derivation parameters
        public readonly int memoryKiB;
        public readonly int passes;
        public readonly int lanes;
        public readonly byte[] salt;

        public readonly byte[] key;

        private PasswordKey(int memoryKiB, int passes, int lanes, byte[] salt, byte[] key)
        {
            this.memoryKiB = memoryKiB;
            this.passes = passes;
            this.lanes = lanes;
            this.salt = salt;
            this.key = key;
        }

        public static PasswordKey Create(string password) =>
            Derive(password, defaultMemoryKiB, defaultPasses, defaultLanes, RandomNumberGenerator.GetBytes(saltSize));

        public static PasswordKey Derive(string password, int memoryKiB, int passes, int lanes, byte[] salt)
        {
            byte[] secret = Encoding.UTF8.GetBytes(password);
            try
            {
                using Argon2id argon = new Argon2id(secret) { Salt = salt, MemorySize = memoryKiB, Iterations = passes, DegreeOfParallelism = lanes };
                return new PasswordKey(memoryKiB, passes, lanes, salt, argon.GetBytes(Cipher.keySize));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(secret);
            }
        }

        public XAttribute[] Attributes() => new[]
        {
            new XAttribute("Kdf", kdf),
            new XAttribute("Memory", memoryKiB.ToString(CultureInfo.InvariantCulture)),
            new XAttribute("Passes", passes.ToString(CultureInfo.InvariantCulture)),
            new XAttribute("Lanes", lanes.ToString(CultureInfo.InvariantCulture)),
            new XAttribute("Salt", Convert.ToBase64String(salt))
        };

        // Null when the element names no Argon2id parameters.
        public static PasswordKey? FromAttributes(XElement element, string password)
        {
            if ((string?)element.Attribute("Kdf") != kdf) return null;
            if (!int.TryParse((string?)element.Attribute("Memory"), NumberStyles.None, CultureInfo.InvariantCulture, out int memory)
                || !int.TryParse((string?)element.Attribute("Passes"), NumberStyles.None, CultureInfo.InvariantCulture, out int passes)
                || !int.TryParse((string?)element.Attribute("Lanes"), NumberStyles.None, CultureInfo.InvariantCulture, out int lanes)
                || element.Attribute("Salt") is not XAttribute salt) return null;
            return Derive(password, memory, passes, lanes, Convert.FromBase64String(salt.Value));
        }

        public void Dispose() => CryptographicOperations.ZeroMemory(key);
    }
}
