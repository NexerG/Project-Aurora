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

        public static void AddLibraryEntry(object dict, Type t)
        {
            if (library.TryGetValue(t, out var _))
            {
                return;
            }
            library.Add(t, dict);
        }

        public static Dictionary<key, type> GetRegistry<key, type>(Type t)
        {
            if (library.TryGetValue(t, out var dict))
            {
                return (Dictionary<key, type>)dict;
            }
            return null;
        }

        public static T GetAsset<T>(string name)
        {
            Type t = typeof(T);
            if(library.TryGetValue(t, out var dict))
            {
                var d = (Dictionary<string, T>)dict;
                if(d.TryGetValue(name, out var asset))
                {
                    return asset;
                }
            }
            throw new Exception("Asset not found");
        }
    }
}
