using ArctisAurora.Core.Diagnostics;
using ArctisAurora.Core.Security;
using System.Reflection;
using System.Security.Cryptography;
using System.Xml.Linq;

namespace ArctisAurora.Core.Filing.Arx
{
    // The AES project key and ECDsa P-256 signing pair an app's archives are built with.
    public sealed class ArxKeys
    {
        private static readonly LogChannel Log = LogChannel.For("Arx");

        public byte[] key = Array.Empty<byte>();
        public byte[] signingKey = Array.Empty<byte>();
        public byte[] publicKey = Array.Empty<byte>();

        public static string PathFor(string app) =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Arktis", "BuildKeys", app + ".keys.xml");

        // Loads an app's build keys, creating them on first use.
        public static ArxKeys LoadOrCreate(string app)
        {
            string path = PathFor(app);
            if (File.Exists(path))
            {
                XElement root = XElement.Load(path);
                return new ArxKeys
                {
                    key = Convert.FromBase64String((string)root.Attribute("Key")!),
                    signingKey = Convert.FromBase64String((string)root.Attribute("SigningKey")!),
                    publicKey = Convert.FromBase64String((string)root.Attribute("PublicKey")!),
                };
            }

            using ECDsa ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            ArxKeys keys = new ArxKeys
            {
                key = Cipher.NewKey(),
                signingKey = ecdsa.ExportPkcs8PrivateKey(),
                publicKey = ecdsa.ExportSubjectPublicKeyInfo(),
            };
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            new XElement("ArxKeys",
                new XAttribute("App", app),
                new XAttribute("Key", Convert.ToBase64String(keys.key)),
                new XAttribute("SigningKey", Convert.ToBase64String(keys.signingKey)),
                new XAttribute("PublicKey", Convert.ToBase64String(keys.publicKey))).Save(path);
            Log.Warn($"created build keys at {path} — back it up; archives built with it cannot be patched without it.");
            return keys;
        }

        // The AES key and public key the builder stamped into the running app; null when it carries none.
        public static ArxKeys? FromEntryAssembly()
        {
            Assembly? entry = Assembly.GetEntryAssembly();
            if (entry == null) return null;

            string? key = null, publicKey = null;
            foreach (AssemblyMetadataAttribute meta in entry.GetCustomAttributes<AssemblyMetadataAttribute>())
            {
                if (meta.Key == "ArxKey") key = meta.Value;
                else if (meta.Key == "ArxPublicKey") publicKey = meta.Value;
            }
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(publicKey)) return null;
            return new ArxKeys { key = Convert.FromBase64String(key), publicKey = Convert.FromBase64String(publicKey) };
        }
    }
}
