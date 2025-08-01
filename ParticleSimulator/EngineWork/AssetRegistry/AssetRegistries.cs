using ArctisAurora.EngineWork.Renderer;

namespace ArctisAurora.EngineWork.AssetRegistry
{
    public abstract class Asset()
    {
        public abstract Asset LoadAsset(string name);

        public abstract Asset LoadDefault();
    }

    internal class AssetRegistries
    {

        public static Dictionary<string, AVulkanMesh> meshes = new Dictionary<string, AVulkanMesh>();

        public static Dictionary<string, FontAsset> fonts = new Dictionary<string, FontAsset>();
        //public static Dictionary<string, Image<Rgba32>> textures = new Dictionary<string, Image<Rgba32>>();

        //public static Dictionary<string, audio> audio;
    }
}
