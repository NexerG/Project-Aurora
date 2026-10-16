using ArctisAurora.Core.Filing.Arx;
using ArctisAurora.Core.Filing.Serialization;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Security;
using ArctisAurora.Core.Testing;
using System.Security.Cryptography;
using System.Xml.Linq;

namespace ArctisAurora.Tests
{
    internal static class ArxTests
    {
        [A_XSDActionDependency("Arx.XmlRoundTrip", "Test")]
        private static IEnumerator<int> XmlRoundTrip(TestContext t)
        {
            if (Paths.packed) yield break;
            ArxKeys keys = NewKeys();
            ArxWriter writer = new ArxWriter();
            Dictionary<string, XElement> sources = new Dictionary<string, XElement>();
            IReadOnlyList<IDataMount> mounts = VirtualFileSystem.Mounts;
            for (int i = 0; i < mounts.Count; i++)
            {
                if (mounts[i] is not DirectoryMount mount || !Directory.Exists(mount.Root)) continue;
                foreach (string file in Directory.EnumerateFiles(mount.Root, "*.xml", SearchOption.AllDirectories))
                {
                    string relative = Path.GetRelativePath(mount.Root, file).Replace('\\', '/');
                    if (relative.StartsWith("XML/Schemas/", StringComparison.OrdinalIgnoreCase)) continue;
                    XElement root = XElement.Load(file);
                    string key = i + "/" + relative;
                    writer.AddXml(key, root);
                    sources[key] = root;
                }
            }

            string path = TempArchive();
            writer.Write(path, keys);
            int mismatched = 0;
            using (ArxArchive archive = ArxArchive.Open(path, keys))
            {
                foreach (KeyValuePair<string, XElement> source in sources)
                {
                    if (XNode.DeepEquals(Stripped(source.Value), archive.ReadXml(source.Key))) continue;
                    if (mismatched++ == 0) t.Check(false, $"{source.Key} decodes to its source minus comments");
                }
            }
            File.Delete(path);
            t.Check(sources.Count > 50, $"the Data trees supply XML to round-trip ({sources.Count} files)");
            t.Check(mismatched == 0, $"every XML entry decodes to its source minus comments ({mismatched} differ)");
            yield break;
        }

        [A_XSDActionDependency("Arx.BlobRoundTrip", "Test")]
        private static IEnumerator<int> BlobRoundTrip(TestContext t)
        {
            ArxKeys keys = NewKeys();
            byte[] random = RandomNumberGenerator.GetBytes(100_003);
            ArxWriter writer = new ArxWriter();
            writer.AddBlob("Shaders/a.spv", random);
            writer.AddBlob("Empty/b.bin", Array.Empty<byte>());

            string path = TempArchive();
            writer.Write(path, keys);
            using (ArxArchive archive = ArxArchive.Open(path, keys))
            {
                t.Check(archive.ReadBytes("Shaders/a.spv").AsSpan().SequenceEqual(random), "a blob reads back byte for byte");
                t.Check(archive.ReadBytes("empty/B.BIN").Length == 0, "an empty blob reads back, path case-insensitive");
                t.Check(!archive.Contains("Shaders/missing.spv"), "an absent entry is not reported");
            }
            File.Delete(path);
            yield break;
        }

        [A_XSDActionDependency("Arx.MountResolves", "Test")]
        private static IEnumerator<int> MountResolves(TestContext t)
        {
            ArxKeys keys = NewKeys();
            XElement suite = new XElement(XName.Get("TestSuite", "http://arctisaurora/AuroraTestTypes"), new XElement(XName.Get("Test", "http://arctisaurora/AuroraTestTypes"), new XAttribute("Action", "A")));
            ArxWriter writer = new ArxWriter();
            writer.AddXml("XML/Documents/Tests/One.tests.xml", suite);
            writer.AddXml("XML/Documents/Tests/Two.other.xml", suite);
            writer.AddBlob("Fonts/f/f.agd", new byte[] { 1, 2, 3 });

            string path = TempArchive();
            writer.Write(path, keys);
            using (ArxArchive archive = ArxArchive.Open(path, keys))
            {
                ArxMount mount = new ArxMount(archive);
                string[] found = mount.Enumerate("XML/Documents/Tests", "*.tests.xml").ToArray();
                t.Check(found.Length == 1 && found[0] == Path.Combine(archive.file, "XML", "Documents", "Tests", "One.tests.xml"),
                    "enumerate matches the pattern and returns a path under the archive file");
                t.Check(mount.DirExists("XML/Documents") && !mount.DirExists("XML/Missing"), "directories exist by entry prefix");
                t.Check(DataFiles.Exists(found[0]) && !DataFiles.Exists(found[0] + ".nope"), "DataFiles.Exists sees archive entries");
                t.Check(XNode.DeepEquals(DataFiles.LoadXml(Path.Combine(Path.GetDirectoryName(found[0])!, "..", "Tests", "One.tests.xml")), suite),
                    "DataFiles.LoadXml reads an entry through a non-normalised path");
                using Stream blob = DataFiles.Open(mount.GetFullPath("Fonts/f/f.agd"));
                t.Check(blob.Length == 3 && blob.ReadByte() == 1, "DataFiles.Open streams a blob entry");
            }
            File.Delete(path);
            yield break;
        }

        [A_XSDActionDependency("Arx.TamperedEntryRefused", "Test")]
        private static IEnumerator<int> TamperedEntryRefused(TestContext t)
        {
            ArxKeys keys = NewKeys();
            ArxWriter writer = new ArxWriter();
            writer.AddBlob("a.bin", new byte[256]);
            string path = TempArchive();
            writer.Write(path, keys);
            FlipByte(path, 64);

            using (ArxArchive archive = ArxArchive.Open(path, keys))
                t.Check(Throws(() => archive.ReadBytes("a.bin")), "a flipped payload byte refuses the entry");
            File.Delete(path);
            yield break;
        }

        [A_XSDActionDependency("Arx.TamperedTocRefused", "Test")]
        private static IEnumerator<int> TamperedTocRefused(TestContext t)
        {
            ArxKeys keys = NewKeys();
            ArxWriter writer = new ArxWriter();
            writer.AddBlob("a.bin", new byte[256]);
            string path = TempArchive();
            writer.Write(path, keys);
            FlipByte(path, new FileInfo(path).Length - ArxArchive.signatureSize - 4);

            t.Check(Throws(() => ArxArchive.Open(path, keys).Dispose()), "a flipped table byte refuses the archive");
            File.Delete(path);
            yield break;
        }

        [A_XSDActionDependency("Arx.WrongKeyRefused", "Test")]
        private static IEnumerator<int> WrongKeyRefused(TestContext t)
        {
            ArxKeys keys = NewKeys();
            ArxWriter writer = new ArxWriter();
            writer.AddBlob("a.bin", new byte[16]);
            string path = TempArchive();
            writer.Write(path, keys);

            ArxKeys other = NewKeys();
            t.Check(Throws(() => ArxArchive.Open(path, new ArxKeys { key = keys.key, publicKey = other.publicKey }).Dispose()),
                "another public key refuses the archive");
            t.Check(Throws(() => ArxArchive.Open(path, new ArxKeys { key = other.key, publicKey = keys.publicKey }).Dispose()),
                "another AES key refuses the archive");
            File.Delete(path);
            yield break;
        }

        private static ArxKeys NewKeys()
        {
            using ECDsa ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            return new ArxKeys { key = Cipher.NewKey(), signingKey = ecdsa.ExportPkcs8PrivateKey(), publicKey = ecdsa.ExportSubjectPublicKeyInfo() };
        }

        private static string TempArchive() => Path.Combine(Path.GetTempPath(), $"aurora-arx-{Guid.NewGuid():N}.arx");

        private static void FlipByte(string path, long offset)
        {
            using FileStream stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite);
            stream.Position = offset;
            int value = stream.ReadByte();
            stream.Position = offset;
            stream.WriteByte((byte)(value ^ 0xFF));
        }

        private static bool Throws(Action action)
        {
            try { action(); }
            catch (InvalidDataException) { return true; }
            return false;
        }

        private static XElement Stripped(XElement source)
        {
            XElement copy = new XElement(source);
            copy.DescendantNodesAndSelf().Where(n => n is XComment || n is XProcessingInstruction).ToList().ForEach(n => n.Remove());
            return copy;
        }
    }
}
