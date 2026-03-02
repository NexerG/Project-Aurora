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

    public sealed class AnyXMLType
    {
        public static readonly Dictionary<string, Type> typeMap = BuildTypeMap();
        private static Dictionary<string, Type> BuildTypeMap()
        {
            Dictionary<string, Type> map = new Dictionary<string, Type>();
            map.Add("xs:string", typeof(string));
            map.Add("xs:int", typeof(int));
            map.Add("xs:float", typeof(float));
            map.Add("xs:double", typeof(double));
            map.Add("xs:boolean", typeof(bool));
            map.Add("xs:byte", typeof(byte));
            map.Add("xs:short", typeof(short));
            map.Add("xs:long", typeof(long));
            map.Add("xs:unsignedInt", typeof(uint));
            map.Add("xs:unsignedShort", typeof(ushort));
            map.Add("xs:unsignedLong", typeof(ulong));
            map.Add("xs:char", typeof(string));
            map.Add("xs:decimal", typeof(decimal));
            map.Add("Action", typeof(Action));
            map.Add("Type", typeof(Type));

            return map;
        }
        public static Type? FindType(string typeName)
        {
            var type = Type.GetType(typeName);
            if (type != null)
            {
                return type;
            }

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = assembly.GetTypes().FirstOrDefault(t => t.Name == typeName || t.FullName == typeName);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }

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

        public void ParseXML(string xmlName)
        {
            //parse the XML and create the registries
            string path = Paths.XMLDOCUMENTS + "\\" + xmlName;
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
        }
    }
}
