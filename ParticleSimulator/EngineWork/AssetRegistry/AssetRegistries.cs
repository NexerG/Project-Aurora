using ArctisAurora.EngineWork.Renderer;

namespace ArctisAurora.EngineWork.AssetRegistry
{
    public abstract class Asset()
    {
        public abstract void LoadAsset(Asset asset, string name, string path);

        public abstract void LoadDefault();
    }

    internal class AssetRegistries
    {

        public static Dictionary<string, AVulkanMesh> meshes = new Dictionary<string, AVulkanMesh>();

        public static Dictionary<string, FontAsset> fonts = new Dictionary<string, FontAsset>();
        public static Dictionary<string, TextureAsset> textures = new Dictionary<string, TextureAsset>();

        //public static Dictionary<string, audio> audio;
    }
}
