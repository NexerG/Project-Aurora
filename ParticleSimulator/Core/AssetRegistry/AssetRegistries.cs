using ArctisAurora.Core.AssetRegistry;
using ArctisAurora.EngineWork.Rendering;
using ArctisAurora.EngineWork.Serialization;
using System.Reflection;
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
        [A_XSDElementProperty("Name")]
        public string name { get; set; }
        [A_XSDElementProperty("KeyType")]
        public AnyXMLType keyType { get; set; }
        [A_XSDElementProperty("ValueType")]
        public AnyXMLType valueType { get; set; }
    }

    [A_XSDElement("AssetRegistries", "Registry", "Registry")]
    public class AssetRegistries : IXMLParser
    {
        [A_XSDElementProperty("Dictionary", "Registry")]
        public static List<AssetRegistryEntry> registries { get; set; } = new List<AssetRegistryEntry>();

        public static Dictionary<Type, object> library = new Dictionary<Type, object>();

        public static AssetRegistries assetRegistries;

        public AssetRegistries()
        {
        }

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

        public static object ParseXML(string xmlName)
        {
            //parse the XML and create the registries
            string path = Paths.XMLDOCUMENTS + "\\" + xmlName;
            AssetRegistries registries = new AssetRegistries();
            XElement root = XElement.Load(path);
            XNamespace ns = root.GetDefaultNamespace();
            foreach (var dictElem in root.Elements(ns + "Dictionary"))
            {
                string name = dictElem.Attribute("Name").Value;
                string keyTypeStr = dictElem.Attribute("KeyType").Value;
                string valueTypeStr = dictElem.Attribute("ValueType").Value;

                Type keyType, valueType;
                if (AnyXMLType.typeMap.ContainsKey(keyTypeStr))
                {
                    keyType = AnyXMLType.typeMap[keyTypeStr];
                }
                else
                {
                    keyType = AnyXMLType.FindType(keyTypeStr);
                }

                if (AnyXMLType.typeMap.ContainsKey(valueTypeStr))
                {
                    valueType = AnyXMLType.typeMap[valueTypeStr];
                }
                else
                {
                    valueType = AnyXMLType.FindType(valueTypeStr);
                }

                //keyType = AnyXMLType.typeMap[keyTypeStr];
                //Type valueType = AnyXMLType.typeMap[valueTypeStr];

                Type dictType = typeof(Dictionary<,>).MakeGenericType(keyType, valueType);
                object dictInstance = Activator.CreateInstance(dictType);
                AddLibraryEntry(dictInstance, valueType);
            }
            return registries;
        }
    }
}
