using Assimp;

namespace ArctisAurora.EngineWork.Serialization
{
    internal class MeshImporter
    {
        public static MeshImporter Instance;

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
            else Console.WriteLine("Failed to load FBX file");
            return null;
        }
    }
}