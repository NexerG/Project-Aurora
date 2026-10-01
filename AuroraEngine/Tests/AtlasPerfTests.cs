using ArctisAurora.Core.Diagnostics;
using ArctisAurora.Core.Filing;
using ArctisAurora.Core.Filing.Serialization;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Testing;
using ArctisAurora.EngineWork;
using System.Xml.Linq;

namespace ArctisAurora.Tests
{
    internal static class AtlasPerfTests
    {
        private const int bakeSize = 64;

        // Bakes the icon set, arial and cambria into a temp root, one bake per frame.
        [A_XSDActionDependency("Perf.AtlasBake", "Test")]
        private static IEnumerator<int> AtlasBake(TestContext t)
        {
            if (Engine.isDebug || !Profiling.compiledIn)
            {
                t.StartMeasure();
                yield return t.EndMeasure();
                yield break;
            }

            string root = Path.Combine(Path.GetTempPath(), "AuroraAtlasBake");
            if (Directory.Exists(root)) Directory.Delete(root, true);
            string[] svgs = VirtualFileSystem.EnumerateAll("Icons/default/svg", "*.svg").ToArray();
            Dictionary<string, string> charsets = ReadCharsets();

            IconSet.GenerateIconAtlas("default", svgs, bakeSize, root);
            yield return 1;

            t.StartMeasure();
            yield return 1;

            IconSet.GenerateIconAtlas("default", svgs, bakeSize, root);
            yield return 1;

            AssetImporter.ImportFont(charsets["Latin"], "arial", AssetImporter.ResolveSystemFont("arial.ttf"), 0,
                null, null, null, bakeSize, root);
            yield return 1;

            AssetImporter.ImportFont(charsets["Math"], "cambria", AssetImporter.ResolveSystemFont("cambria.ttc"), 1,
                AssetImporter.ResolveSystemFont("cambriab.ttf"), AssetImporter.ResolveSystemFont("cambriai.ttf"), null,
                bakeSize, root);
            yield return 1;

            yield return t.EndMeasure();
        }

        private static Dictionary<string, string> ReadCharsets()
        {
            Dictionary<string, string> charsets = new Dictionary<string, string>();
            foreach (string file in VirtualFileSystem.EnumerateAll("XML/Imports", "*.imports.xml"))
            {
                XElement root = XElement.Load(file);
                foreach (XElement charset in root.Elements(root.GetDefaultNamespace() + "Charset"))
                    charsets[(string)charset.Attribute("Name")!] = (string)charset.Attribute("Chars")!;
            }
            return charsets;
        }
    }
}
