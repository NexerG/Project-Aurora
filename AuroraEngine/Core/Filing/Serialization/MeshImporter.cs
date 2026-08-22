using Assimp;

namespace ArctisAurora.Core.Filing.Serialization
{
    internal class MeshImporter
    {
        private static readonly Diagnostics.LogChannel Log = Diagnostics.LogChannel.For("Assets");

        public static MeshImporter Instance = null!;

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