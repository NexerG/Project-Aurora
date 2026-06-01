using ArctisAurora.EngineWork;

namespace ArctisAurora.Core.Filing.Serialization
{
    public static class Paths
    {
        // Mount the virtual file system before any path constant below is resolved.
        // (Static field initializers run in textual order, so this must stay first.)
        private static readonly bool _mounted = Mount();

        public static readonly string DATA = GetPath("Data");
        public static readonly string BUILD_UI = GetPath("Data\\XML");
        public static readonly string FONTS = GetPath("Data\\Fonts");
        public static readonly string UIMASKS = GetPath("Data\\UIMasks");
        public static readonly string XML = GetPath("Data\\XML");
        public static readonly string XMLSCHEMAS = GetPath("Data\\XML\\Schemas");
        public static readonly string XMLDOCUMENTS = GetPath("Data\\XML\\Documents");
        public static readonly string XMLDOCUMENTS_INPUTS = GetPath("Data\\XML\\Documents\\Inputs");
        public static readonly string XMLDOCUMENTS_SAMPLERS = GetPath("Data\\XML\\Documents\\Samplers");
        public static readonly string SCENES = GetPath("Data\\Scenes");

        // Engine-owned config (Bootstrap, registries, default samplers) can live in the engine
        // project's Data folder rather than the running app's, so resolve it through the VFS
        // instead of assuming it sits next to the app's own files.
        public static readonly string BOOTSTRAP = Doc("Bootstrap.xml");

        // Resolve a document under Data/XML/Documents across all mounts (app first, engine fallback).
        public static string Doc(string name) => VirtualFileSystem.ResolveFile("XML/Documents/" + name);

        // Resolve a sampler document under Data/XML/Documents/Samplers across all mounts.
        public static string SamplerDoc(string name) => VirtualFileSystem.ResolveFile("XML/Documents/Samplers/" + name);

        private static bool Mount()
        {
            // Primary mount: the running application's own Data folder.
            string primary = GetPath("Data");
            VirtualFileSystem.Mount(new DirectoryMount(primary));

            // While editing (Debug), engine-default Data lives in the engine project
            // ('ParticleSimulator', a sibling of each application). Mount it at lower priority so
            // applications inherit engine defaults without copying them, and can still override any
            // file locally. (Shipping bundles these into archives — a future PakMount — instead.)
            if (Engine.isDebug)
            {
                string engineData = Path.GetFullPath(Path.Combine(primary, "..", "..", "ParticleSimulator", "Data"));
                if (Directory.Exists(engineData) &&
                    !string.Equals(engineData, primary, StringComparison.OrdinalIgnoreCase))
                {
                    VirtualFileSystem.Mount(new DirectoryMount(engineData));
                }
            }
            return true;
        }

        private static string GetPath(string path)
        {
            if (Engine.isDebug)
                return Path.GetFullPath(Path.Combine("..", "..", "..", path));

            return Path.Combine(AppContext.BaseDirectory, path);
        }
    }
}
