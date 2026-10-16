using Assimp;
using System.Runtime.InteropServices;

namespace ArctisAurora.Core.Filing.Serialization
{
    internal class MeshImporter
    {
        private static readonly Diagnostics.LogChannel Log = Diagnostics.LogChannel.For("Assets");

        public static MeshImporter Instance = null!;

        // Points AssimpNet's unversioned libdl at glibc's libdl.so.2.
        static MeshImporter()
        {
            if (!OperatingSystem.IsWindows())
                NativeLibrary.SetDllImportResolver(typeof(AssimpContext).Assembly,
                    (name, assembly, path) => name == "libdl.so" ? NativeLibrary.Load("libdl.so.2") : IntPtr.Zero);
        }

        public MeshImporter()
        {
            Instance = this;
        }

        internal Scene ImportFBX(string filePath)
        {
            AssimpContext importer  = new AssimpContext();
            Scene scene = importer.ImportFile(filePath, PostProcessPreset.TargetRealTimeMaximumQuality);
            if (scene != null )
            {
                return scene;
            }
            else Log.Error($"failed to load FBX file '{filePath}'");
            return null;
        }
    }
}