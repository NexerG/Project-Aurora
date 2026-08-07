using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Filing.Serialization;
using ArctisAurora.EngineWork.Rendering;
using System.Collections;
using System.Reflection;
using System.Xml.Linq;
using static ArctisAurora.Core.UISystem.Controls.VulkanControl;
using ArctisAurora.Core.Registry.Assets;

namespace ArctisAurora.EngineWork.Registry
{
    [A_XSDType("AssetEntry", "AssetRegistry")]
    public class AssetRegistryEntry
    {
        [A_XSDElementProperty("Name", "AssetRegistry")]
        public string name { get; set; } = string.Empty;
        [A_XSDElementProperty("KeyType", "AssetRegistry")]
        public AnyXMLType keyType { get; set; }
        [A_XSDElementProperty("ValueType", "AssetRegistry")]
        public AnyXMLType valueType { get; set; }
    }

    [A_XSDType("Asset", "AssetRegistry")]
    public class AssetManifestEntry
    {
        [A_XSDElementProperty("Type", "AssetRegistry")]
        public string type { get; set; } = string.Empty;
        [A_XSDElementProperty("Name", "AssetRegistry")]
        public string name { get; set; } = string.Empty;
        [A_XSDElementProperty("Source", "AssetRegistry")]
        public string source { get; set; } = string.Empty;
    }

    [A_XSDType("AssetRegistries", "AssetRegistry")]
    public class AssetRegistries : IXMLParser<AssetRegistries>
    {
        [A_XSDElementProperty("Dictionary", "AssetRegistry")]
        public static List<AssetRegistryEntry> registries { get; set; }

        public static Dictionary<Type, object> library = new Dictionary<Type, object>();
        public static Dictionary<string, object> libraryByName = new Dictionary<string, object>();

        private static readonly HashSet<string> reportedMisses = new HashSet<string>();

        public static AssetRegistries assetRegistries;

        public AssetRegistries()
        {
        }

        public static void AddLibraryEntry(string name, object dict, Type t)
        {
            if (library.TryGetValue(t, out var _))
            {
                return;
            }
            library.Add(t, dict);
            libraryByName.Add(name, dict);
        }

        public static Dictionary<key, type> GetRegistryByValueType<key, type>(Type t)
        {
            if (library.TryGetValue(t, out var dict))
            {
                return (Dictionary<key, type>)dict;
            }
            return null;
        }

        public static Dictionary<key, type> GetRegistryByKeyType<key, type>(Type t)
        {
            var match = library.FirstOrDefault(kvp => kvp.Value.GetType().GetGenericArguments()[0] == t);
            return match as Dictionary<key, type>;
        }

        public static Dictionary<key, type> GetRegistryByName<key, type>(string name)
        {
            libraryByName.TryGetValue(name, out var dict);
            return (Dictionary<key, type>)dict;
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
                RequestLoad(t, name);
                if (d.TryGetValue("default", out var fallback))
                {
                    return fallback;
                }
            }
            throw new Exception("Asset not found");
        }

        // Reports a miss once.
        private static void RequestLoad(Type type, string name)
        {
            if (reportedMisses.Add(type.Name + ":" + name))
                Console.WriteLine($"Asset '{name}' ({type.Name}) is not loaded - falling back to default.");
        }

        public static AssetRegistries ParseXML(string xmlName)
        {
            //parse the XML and create the registries
            string path = Paths.Doc(xmlName);
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

                Type dictType = typeof(Dictionary<,>).MakeGenericType(keyType, valueType);
                object dictInstance = Activator.CreateInstance(dictType);

                AddLibraryEntry(name, dictInstance, valueType);
            }
            return registries;
        }
        
        [A_XSDActionDependency("AssetRegistries.RegisterSerializableTypes", "Bootstrap")]
        internal static void RegisterSerializableTypes()
        {
            var asm = AppDomain.CurrentDomain.GetAssemblies();
            var types = asm.SelectMany(a => a.GetTypes()).Where(t => t.GetCustomAttribute<@Serializable>() != null).ToList();

            Dictionary<uint, Type> serializableTypes = GetRegistryByValueType<uint, Type>(typeof(Type));
            foreach (var t in types)
            {
                var attr = t.GetCustomAttribute<@Serializable>();
                if (attr != null)
                {
                    attr.ID = Serializable.GenerateID(t.Name);
                    serializableTypes.Add((uint)attr.ID, t);
                }
            }
        }

        [A_XSDActionDependency("AssetRegistries.InstantiateRegistries", "Bootstrap")]
        internal static void InstantiateRegistries()
        {
            assetRegistries = ParseXML("Registry.xml");
        }

        // Assets with no file behind them; everything file-backed comes from the manifest.
        [A_XSDActionDependency("AssetRegistries.PrepareDefaultAssets", "Bootstrap")]
        internal static void PrepareDefaultAssets()
        {
            Dictionary<string, AVulkanMesh> dMeshes = GetRegistryByValueType<string, AVulkanMesh>(typeof(AVulkanMesh));
            dMeshes.Add("default", AVulkanMesh.LoadDefault());

            Dictionary<string, ControlStyle> dStyles = GetRegistryByValueType<string, ControlStyle>(typeof(ControlStyle));
            ControlStyle style = new ControlStyle();
            style.tint = new Silk.NET.Maths.Vector3D<float>(1, 1, 1);
            dStyles.Add("default", style);

            SamplerAsset sampler = new SamplerAsset("default");
            sampler.LoadDefault();
        }

        [A_XSDActionDependency("AssetRegistries.PreloadAssets", "Bootstrap")]
        internal static void PreloadAssets()
        {
            Dictionary<(string, string), AssetManifestEntry> entries = new Dictionary<(string, string), AssetManifestEntry>();
            foreach (string file in VirtualFileSystem.EnumerateAll("XML/Assets", "*.xml"))
            {
                XElement root = XElement.Load(file);
                XNamespace ns = root.GetDefaultNamespace();
                foreach (XElement element in root.Elements(ns + "Asset"))
                {
                    AssetManifestEntry entry = new AssetManifestEntry();
                    XmlReflection.ApplyAttributes(element, entry);
                    entries.TryAdd((entry.type, entry.name), entry);
                }
            }

            foreach (AssetManifestEntry entry in entries.Values)
            {
                Type valueType = AnyXMLType.FindType(entry.type)
                    ?? throw new Exception($"Asset manifest declares unknown type '{entry.type}'.");
                if (!library.TryGetValue(valueType, out object dict))
                    throw new Exception($"No registry is declared for asset type '{entry.type}'.");

                AbstractAsset asset = (AbstractAsset)Activator.CreateInstance(valueType);
                asset.Load(entry.name, entry.source);
                ((IDictionary)dict).Add(entry.name, asset);
            }
        }

        [A_XSDActionDependency("AssetRegistries.PrepareAllAssets", "Bootstrap")]
        internal static void PrepareAllAssets()
        {
            // samplers
            SamplerAsset sampler = new SamplerAsset();
            sampler.LoadAll(Paths.XMLDOCUMENTS_SAMPLERS);
        }
    }
}