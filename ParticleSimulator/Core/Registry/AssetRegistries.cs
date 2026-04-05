using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Filing.Serialization;
using ArctisAurora.EngineWork.Rendering;
using Assimp;
using System.Reflection;
using System.Xml.Linq;
using static ArctisAurora.Core.UISystem.Controls.VulkanControl;

namespace ArctisAurora.EngineWork.Registry
{
    public abstract class Asset()
    {
        public abstract void LoadAsset(Asset asset, string name, string path);

        public abstract void LoadDefault();
    }

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

    [A_XSDType("AssetRegistries", "AssetRegistry")]
    public class AssetRegistries : IXMLParser<AssetRegistries>
    {
        [A_XSDElementProperty("Dictionary", "AssetRegistry")]
        public static List<AssetRegistryEntry> registries { get; set; }

        public static Dictionary<Type, object> library = new Dictionary<Type, object>();
        public static Dictionary<string, object> libraryByName = new Dictionary<string, object>();

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
            }
            throw new Exception("Asset not found");
        }

        public static AssetRegistries ParseXML(string xmlName)
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

        [A_XSDActionDependency("AssetRegistries.PrepareDefaultAssets", "Bootstrap")]
        internal static void PrepareDefaultAssets()
        {
            Dictionary<string, AVulkanMesh> dMeshes = GetRegistryByValueType<string, AVulkanMesh>(typeof(AVulkanMesh));
            AVulkanMesh mesh = AVulkanMesh.LoadDefault();
            dMeshes.Add("default", mesh);

            AVulkanMesh UIMesh = new AVulkanMesh();
            MeshImporter importer = new MeshImporter();
            Scene scene1 = importer.ImportFBX("C:\\Users\\gmgyt\\Desktop\\VienetinisPlaneRetry.fbx");
            UIMesh.LoadCustomMesh(scene1);
            dMeshes.Add("uidefault", UIMesh);

            // load default font
            FontAsset font = new FontAsset("default");
            font.LoadDefault();

            TextureAsset texture = new TextureAsset("default");
            texture.LoadDefault();

            TextureAsset invisible = new TextureAsset("invisible");
            invisible.LoadInvisible();

            // load default style
            Dictionary<string, ControlStyle> dStyles = GetRegistryByValueType<string, ControlStyle>(typeof(ControlStyle));
            ControlStyle style = new ControlStyle();
            style.tint = new Silk.NET.Maths.Vector3D<float>(1, 1, 1);
            dStyles.Add("default", style);
        }
    }
}