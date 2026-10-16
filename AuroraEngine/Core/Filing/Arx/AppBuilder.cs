using ArctisAurora.Core.Diagnostics;
using ArctisAurora.Core.Filing.Serialization;
using ArctisAurora.Core.Registry;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ArctisAurora.Core.Filing.Arx
{
    [A_XSDType("BuildSource", "Build")]
    public class BuildSource
    {
        [A_XSDElementProperty("Source", "Build")]
        public string source { get; set; } = string.Empty;
    }

    [A_XSDType("BuildPattern", "Build")]
    public class BuildPattern
    {
        [A_XSDElementProperty("Include", "Build")]
        public string include { get; set; } = string.Empty;
    }

    [A_XSDType("Build", "Build")]
    public class BuildDefinition
    {
        [A_XSDElementProperty("App", "Build")]
        public string app { get; set; } = string.Empty;

        [A_XSDElementProperty("Project", "Build")]
        public string project { get; set; } = string.Empty;

        [A_XSDElementProperty("Output", "Build")]
        public string output { get; set; } = string.Empty;

        [A_XSDElementProperty("Data", "Build")]
        public List<BuildSource> data { get; set; } = new List<BuildSource>();

        [A_XSDElementProperty("Shaders", "Build")]
        public List<BuildSource> shaders { get; set; } = new List<BuildSource>();

        [A_XSDElementProperty("Loose", "Build")]
        public List<BuildPattern> loose { get; set; } = new List<BuildPattern>();

        [A_XSDElementProperty("Exclude", "Build")]
        public List<BuildPattern> exclude { get; set; } = new List<BuildPattern>();
    }

    // Publishes an app in Release and packs its data and shaders into one signed archive beside the exe.
    public static class AppBuilder
    {
        private static readonly LogChannel Log = LogChannel.For("Build");

        private static readonly string[] defaultExcludes = { "XML/Schemas/**" };

        public static bool Run(string buildFile)
        {
            buildFile = Path.GetFullPath(buildFile);
            string baseDir = Path.GetDirectoryName(buildFile)!;
            BuildDefinition build = Parse(buildFile);
            string output = Path.GetFullPath(Path.Combine(baseDir, build.output));
            ArxKeys keys = ArxKeys.LoadOrCreate(build.app);

            if (!Publish(Path.GetFullPath(Path.Combine(baseDir, build.project)), output, keys)) return false;
            foreach (string stale in new[] { "Data", "Shaders" })
                if (Directory.Exists(Path.Combine(output, stale))) Directory.Delete(Path.Combine(output, stale), true);

            List<Regex> excludes = defaultExcludes.Concat(build.exclude.Select(e => e.include)).Select(Glob).ToList();
            List<Regex> loose = build.loose.Select(l => Glob(l.include)).ToList();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            ArxWriter writer = new ArxWriter();
            int looseCount = 0;

            foreach (BuildSource data in build.data)
            {
                string root = Path.GetFullPath(Path.Combine(baseDir, data.source));
                foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                {
                    string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                    if (excludes.Any(r => r.IsMatch(relative)) || !seen.Add(relative)) continue;

                    if (loose.Any(r => r.IsMatch(relative)))
                    {
                        string target = Path.Combine(output, "Data", relative);
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        File.Copy(file, target, true);
                        looseCount++;
                    }
                    else if (file.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) writer.AddXml(relative, XElement.Load(file));
                    else writer.AddBlob(relative, File.ReadAllBytes(file));
                }
            }

            foreach (BuildSource shaders in build.shaders)
            {
                string root = Path.GetFullPath(Path.Combine(baseDir, shaders.source));
                foreach (string file in Directory.EnumerateFiles(root, "*.spv", SearchOption.AllDirectories))
                {
                    string relative = "Shaders/" + Path.GetRelativePath(root, file).Replace('\\', '/');
                    if (seen.Add(relative)) writer.AddBlob(relative, File.ReadAllBytes(file));
                }
            }

            string archive = Path.Combine(output, build.app + ".arx");
            writer.Write(archive, keys);
            Log.Info($"{build.app}: {writer.Count} entries packed into {archive}, {looseCount} loose files beside it.");
            return true;
        }

        private static BuildDefinition Parse(string buildFile)
        {
            XElement root = XElement.Load(buildFile);
            XNamespace ns = root.GetDefaultNamespace();
            BuildDefinition build = new BuildDefinition();
            XmlReflection.ApplyAttributes(root, build);
            build.data = Children<BuildSource>(root, ns + "Data");
            build.shaders = Children<BuildSource>(root, ns + "Shaders");
            build.loose = Children<BuildPattern>(root, ns + "Loose");
            build.exclude = Children<BuildPattern>(root, ns + "Exclude");
            return build;
        }

        private static List<T> Children<T>(XElement root, XName name) where T : new()
        {
            List<T> list = new List<T>();
            foreach (XElement element in root.Elements(name))
            {
                T item = new T();
                XmlReflection.ApplyAttributes(element, item!);
                list.Add(item);
            }
            return list;
        }

        private static bool Publish(string project, string output, ArxKeys keys)
        {
            ProcessStartInfo start = new ProcessStartInfo("dotnet") { UseShellExecute = false };
            foreach (string arg in new[] { "publish", project, "-c", "Release", "-o", output, "-nologo", "-v", "quiet", "-p:ErrorOnDuplicatePublishOutputFiles=false",
                         "-p:ArxKey=" + Convert.ToBase64String(keys.key), "-p:ArxPublicKey=" + Convert.ToBase64String(keys.publicKey) })
                start.ArgumentList.Add(arg);

            using Process process = Process.Start(start)!;
            process.WaitForExit();
            if (process.ExitCode != 0) Log.Error($"dotnet publish {project} failed with exit code {process.ExitCode}.");
            return process.ExitCode == 0;
        }

        private static Regex Glob(string pattern)
        {
            string regex = Regex.Escape(pattern.Replace('\\', '/')).Replace(@"\*\*/", "(.*/)?").Replace(@"\*\*", ".*").Replace(@"\*", "[^/]*").Replace(@"\?", "[^/]");
            return new Regex("^" + regex + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
    }
}
