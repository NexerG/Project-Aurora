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

    [A_XSDType("BuildTarget", "Build")]
    public class BuildTarget
    {
        [A_XSDElementProperty("Rid", "Build")]
        public string rid { get; set; } = string.Empty;

        [A_XSDElementProperty("Framework", "Build")]
        public string framework { get; set; } = string.Empty;
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

        [A_XSDElementProperty("Target", "Build")]
        public List<BuildTarget> targets { get; set; } = new List<BuildTarget>();

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

        // Builds every target in the build file, or only the one named by rid.
        public static bool Run(string buildFile, string? rid = null)
        {
            buildFile = Path.GetFullPath(buildFile);
            string baseDir = Path.GetDirectoryName(buildFile)!;
            BuildDefinition build = Parse(buildFile);
            List<BuildTarget> targets = build.targets.Where(t => rid == null || t.rid == rid).ToList();
            if (targets.Count == 0)
            {
                if (rid == null) Log.Error($"{buildFile} declares no <Target>.");
                else Log.Error($"{buildFile} has no <Target Rid=\"{rid}\">.");
                return false;
            }
            string output = Path.GetFullPath(Path.Combine(baseDir, build.output));
            string project = Path.GetFullPath(Path.Combine(baseDir, build.project));
            ArxKeys keys = ArxKeys.LoadOrCreate(build.app);

            List<Regex> excludes = defaultExcludes.Concat(build.exclude.Select(e => e.include)).Select(Glob).ToList();
            List<Regex> loose = build.loose.Select(l => Glob(l.include)).ToList();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            ArxWriter writer = new ArxWriter();
            List<(string source, string relative)> looseFiles = new List<(string, string)>();

            foreach (BuildSource data in build.data)
            {
                string root = Path.GetFullPath(Path.Combine(baseDir, data.source));
                foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                {
                    string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                    if (excludes.Any(r => r.IsMatch(relative)) || !seen.Add(relative)) continue;

                    if (loose.Any(r => r.IsMatch(relative))) looseFiles.Add((file, relative));
                    else if (file.EndsWith(".axml", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) writer.AddXml(relative, XElement.Load(file));
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

            string? archive = null;
            foreach (BuildTarget target in targets)
            {
                string folder = Path.Combine(output, target.rid);
                if (!Publish(project, folder, target, keys)) return false;
                foreach (string stale in new[] { "Data", "Shaders" })
                    if (Directory.Exists(Path.Combine(folder, stale))) Directory.Delete(Path.Combine(folder, stale), true);

                foreach ((string source, string relative) in looseFiles)
                {
                    string copy = Path.Combine(folder, "Data", relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
                    File.Copy(source, copy, true);
                }

                string targetArchive = Path.Combine(folder, build.app + ".arx");
                if (archive == null) writer.Write(targetArchive, keys);
                else File.Copy(archive, targetArchive, true);
                archive = targetArchive;
                Log.Info($"{build.app} {target.rid}: {writer.Count} entries packed into {targetArchive}, {looseFiles.Count} loose files beside it.");
            }
            return true;
        }

        private static BuildDefinition Parse(string buildFile)
        {
            XElement root = XElement.Load(buildFile);
            XNamespace ns = root.GetDefaultNamespace();
            BuildDefinition build = new BuildDefinition();
            XmlReflection.ApplyAttributes(root, build);
            build.targets = Children<BuildTarget>(root, ns + "Target");
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

        private static bool Publish(string project, string output, BuildTarget target, ArxKeys keys)
        {
            ProcessStartInfo start = new ProcessStartInfo("dotnet") { UseShellExecute = false };
            foreach (string arg in new[] { "publish", project, "-c", "Release", "-f", target.framework, "-r", target.rid, "--self-contained", "-o", output,
                         "-nologo", "-v", "quiet", "-p:ErrorOnDuplicatePublishOutputFiles=false",
                         "-p:ArxKey=" + Convert.ToBase64String(keys.key), "-p:ArxPublicKey=" + Convert.ToBase64String(keys.publicKey) })
                start.ArgumentList.Add(arg);

            using Process process = Process.Start(start)!;
            process.WaitForExit();
            if (process.ExitCode != 0) Log.Error($"dotnet publish {project} for {target.rid} failed with exit code {process.ExitCode}.");
            return process.ExitCode == 0;
        }

        private static Regex Glob(string pattern)
        {
            string regex = Regex.Escape(pattern.Replace('\\', '/')).Replace(@"\*\*/", "(.*/)?").Replace(@"\*\*", ".*").Replace(@"\*", "[^/]*").Replace(@"\?", "[^/]");
            return new Regex("^" + regex + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
    }
}
