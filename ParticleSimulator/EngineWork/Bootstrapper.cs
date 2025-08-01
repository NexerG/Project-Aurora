using ArctisAurora.EngineWork.AssetRegistry;
using ArctisAurora.EngineWork.Renderer;

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

            // load default font
            FontAsset font = FontAsset.LoadDefault();
            AssetRegistries.fonts.Add("default", font);
        }
    }
}