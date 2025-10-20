using ArctisAurora.EngineWork.Rendering;
using static ArctisAurora.EngineWork.Rendering.UI.Controls.VulkanControl;

namespace ArctisAurora.EngineWork.AssetRegistry
{
    public abstract class Asset()
    {
        public abstract void LoadAsset(Asset asset, string name, string path);

        public abstract void LoadDefault();
    }

    internal sealed class AssetRegistries
    {
        public static Dictionary<Type, object> library = new Dictionary<Type, object>();

        public static void CreateEntry(Type t)
        {
            if(library.TryGetValue(t, out var _))
            {
                return;
            }

            Type dictType = typeof(Dictionary<,>).MakeGenericType(typeof(string), t);
            var newDict = Activator.CreateInstance(dictType);
            library.Add(t, newDict);
        }

        public static Dictionary<string, T> GetRegistry<T>(Type t)
        {
            if(library.TryGetValue(t, out var dict))
            {
                return (Dictionary<string, T>)dict;
            }
            return null;
        }

        //public static Dictionary<string, AVulkanMesh> meshes = new Dictionary<string, AVulkanMesh>();
        //
        //public static Dictionary<string, FontAsset> fonts = new Dictionary<string, FontAsset>();
        //public static Dictionary<string, TextureAsset> textures = new Dictionary<string, TextureAsset>();
        //
        //public static Dictionary<string, ControlStyle> styles = new Dictionary<string, ControlStyle>();
        //public static Dictionary<string, Action> uiActions = new Dictionary<string, Action>();

        //public static Dictionary<string, audio> audio;
    }
}
