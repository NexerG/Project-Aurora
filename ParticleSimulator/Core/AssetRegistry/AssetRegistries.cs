using ArctisAurora.Core.AssetRegistry;
using ArctisAurora.EngineWork.Rendering;
using ArctisAurora.EngineWork.Serialization;
using Assimp;
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

    public interface IActiveObject
    {

    }

    public enum RegistryStage
    {
        PreXML,
        PostXML,
        RegisterTypes,
        LoadAssets
    }

    [A_XSDType("Entry", "Registry")]
    public class AssetRegistryEntry
    {
        [A_XSDElementProperty("Name")]
        public string name { get; set; }
        [A_XSDElementProperty("KeyType")]
        public List<AnyXMLType> keyType { get; set; } = new List<AnyXMLType>();
        [A_XSDElementProperty("ValueType")]
        public List<AnyXMLType> valueType { get; set; } = new List<AnyXMLType>();
    }

    [A_XSDElement("AssetRegistries", "Registry", "Registry")]
    public class AssetRegistries : IXMLParser<AssetRegistries>, IBootstrap
    {
        [A_XSDElementProperty("Dictionary", "Registry")]
        public static List<AssetRegistryEntry> registries { get; set; }

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

                //keyType = AnyXMLType.typeMap[keyTypeStr];
                //Type valueType = AnyXMLType.typeMap[valueTypeStr];

                Type dictType = typeof(Dictionary<,>).MakeGenericType(keyType, valueType);
                object dictInstance = Activator.CreateInstance(dictType);
                AddLibraryEntry(dictInstance, valueType);
            }
            return registries;
        }
        
        [A_BootstrapStage(BootstrapStage.PostGPUAPI)]
        [A_BootstrapStage(BootstrapStage.PreGPUAPI)]
        public static void Bootstrap(BootstrapStage? stage)
        {
            var asm = AppDomain.CurrentDomain.GetAssemblies();
            switch (stage)
            {
                case BootstrapStage.PreGPUAPI:
                    InstantiateRegistries();
                    RegisterSerializableTypes(asm);
                    break;
                case BootstrapStage.PostGPUAPI:
                    PrepareDefaultAssets();
                    break;
                default:
                    break;
            }
        }

        private static void RegisterSerializableTypes(Assembly[] asm)
        {
            var types = asm.SelectMany(a => a.GetTypes()).Where(t => t.GetCustomAttribute<@Serializable>() != null).ToList();

            Dictionary<uint, Type> serializableTypes = GetRegistry<uint, Type>(typeof(Type));
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

        private static void InstantiateRegistries()
        {
            assetRegistries = ParseXML("Registry.xml");
        }

        private static void PrepareDefaultAssets()
        {
            Dictionary<string, AVulkanMesh> dMeshes = GetRegistry<string, AVulkanMesh>(typeof(AVulkanMesh));
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
            Dictionary<string, ControlStyle> dStyles = GetRegistry<string, ControlStyle>(typeof(ControlStyle));
            ControlStyle style = new ControlStyle();
            style.tint = new Silk.NET.Maths.Vector3D<float>(1, 1, 1);
            dStyles.Add("default", style);
        }
    }
}