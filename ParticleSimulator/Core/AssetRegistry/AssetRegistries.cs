using ArctisAurora.Core.AssetRegistry;
using ArctisAurora.EngineWork.Rendering;
using ArctisAurora.EngineWork.Serialization;
using System.Xml.Linq;
using static ArctisAurora.EngineWork.Rendering.UI.Controls.VulkanControl;

namespace ArctisAurora.EngineWork.AssetRegistry
{
    public abstract class Asset()
    {
        public abstract void LoadAsset(Asset asset, string name, string path);

        public abstract void LoadDefault();
    }

    [A_XSDType("Entry", "Registry")]
    public class AssetRegistryEntry
    {
        [A_XSDElementProperty("KeyType")]
        public int keyType { get; set; }
        [A_XSDElementProperty("ValueType")]
        public int valueType { get; set; }
    }

    [A_XSDElement("AssetRegistries","Registry")]
    public sealed class AssetRegistries : IXMLParser
    {
        [A_XSDElementProperty("Dictionary")]
        public List<AssetRegistryEntry> registries { get; set; } = new List<AssetRegistryEntry>();

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

        public void ParseXML(string xmlName)
        {
            //parse the XML and create the registries
            /*string path = Paths.REGISTRIES + "\\" + xmlName;
            XElement root = XElement.Load(path);
            XNamespace ns = root.GetDefaultNamespace();
            foreach (var dictElem in root.Elements(ns + "Dictionary"))
            {
                AssetRegistryEntry entry = new AssetRegistryEntry();
                entry.keyType = Type.GetType(dictElem.Attribute("KeyType").Value);
                entry.valueType = Type.GetType(dictElem.Attribute("ValueType").Value);
                registries.Add(entry);
                Type dictType = typeof(Dictionary<,>).MakeGenericType(entry.keyType, entry.valueType);
                object dictInstance = Activator.CreateInstance(dictType);
                AddLibraryEntry(dictInstance, dictType);
            }*/
        }
    }
}
