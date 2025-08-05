using ArctisAurora.EngineWork.AssetRegistry;
using ArctisAurora.EngineWork.Rendering;
using ArctisAurora.EngineWork.Serialization;
using Assimp;

namespace ArctisAurora.EngineWork
{
    internal static class Bootstrapper
    {
        public static void PreprareAssets()
        {

        }

        public static void PreprareDefaultAssets()
        {
            // this should load the default config and register them to the AssetRegistry
            // but for now it'll just be a hardcoded list of assets

            // load default mesh
            AVulkanMesh mesh = AVulkanMesh.LoadDefault();
            AssetRegistries.meshes.Add("default", mesh);

            AVulkanMesh UIMesh = new AVulkanMesh();
            MeshImporter importer = new MeshImporter();
            Scene scene1 = importer.ImportFBX("C:\\Users\\gmgyt\\Desktop\\VienetinisPlane.fbx");
            UIMesh.LoadCustomMesh(scene1);
            AssetRegistries.meshes.Add("uidefault", UIMesh);

            // load default font
            FontAsset font = new FontAsset("default");
            font.LoadDefault();
        }
    }
}