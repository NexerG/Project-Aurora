using ArctisAurora.EngineWork.AssetRegistry;
using ArctisAurora.EngineWork.Rendering;
using ArctisAurora.EngineWork.Serialization;
using Assimp;
using System.Xml.Linq;
using static ArctisAurora.EngineWork.Rendering.UI.Controls.VulkanControl;

namespace ArctisAurora.EngineWork
{
    public sealed class A_BootstrapAttribute : Attribute
    {
        public BoostrappingMode Mode { get; }
        //public string Name { get; }
        public A_BootstrapAttribute(BoostrappingMode mode)
        {
            Mode = mode;
        }

        public A_BootstrapAttribute()
        {
            Mode = BoostrappingMode.Assets;
        }
    }


    public enum BoostrappingMode
    {
        Assets,
        Functions,
    }

    internal static class Bootstrapper
    {
        private static readonly Dictionary<string, Type> Aliases = new()
        {
            ["string"] = typeof(string),
            ["int"] = typeof(int),
            ["float"] = typeof(float),
            ["double"] = typeof(double),
            ["bool"] = typeof(bool),
            ["byte"] = typeof(byte),
            ["char"] = typeof(char),
            ["decimal"] = typeof(decimal),
            ["long"] = typeof(long),
            ["short"] = typeof(short),
            ["object"] = typeof(object)
        };

        public static void PrepareRegistries()
        {
            //scan the EngineRegistries.xml and create the registries that will be used to quick store/access assets
            string path = Paths.REGISTRIES + "\\EngineRegistries.xml";
            XElement root = XElement.Load(path);
            XNamespace ns = root.GetDefaultNamespace();

            foreach (var dictElem in root.Elements(ns+"Dictionary"))
            {
                string name = dictElem.Attribute("name")?.Value ?? throw new Exception("Missing name");
                string keyTypeName = dictElem.Attribute("keyType")?.Value ?? throw new Exception("Missing keyType");
                string valueTypeName = dictElem.Attribute("valueType")?.Value ?? throw new Exception("Missing valueType");

                if (Aliases.ContainsKey(keyTypeName.ToLower()))
                {
                    keyTypeName = Aliases[keyTypeName.ToLower()].FullName!;
                }
                if (Aliases.ContainsKey(valueTypeName.ToLower()))
                {
                    valueTypeName = Aliases[valueTypeName.ToLower()].FullName!;
                }

                Type keyType = FindType(keyTypeName) ?? throw new Exception("Type not found");
                Type valueType = FindType(valueTypeName) ?? throw new Exception("Type not found");

                Type dictType = typeof(Dictionary<,>).MakeGenericType(keyType, valueType);
                object dictInstance = Activator.CreateInstance(dictType)!;

                AssetRegistries.CreateEntry(valueType);

                //scan the registry type xml for pre-stored assets
                string registryPath = Paths.REGISTRIES + "\\Preloads\\" + name + ".xml";
                if (File.Exists(registryPath))
                {
                    XElement registryRoot = XElement.Load(registryPath);
                    foreach (var assetElem in registryRoot.Elements(ns + "Asset"))
                    {
                        //string assetName = assetElem.Attribute("name")?.Value ?? throw new Exception("Missing asset name");
                        //string assetPath = assetElem.Attribute("path")?.Value ?? throw new Exception("Missing asset path");
                    }
                }
            }
        }



        private static Type? FindType(string typeName)
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

        public static void PreprareAssets()
        {

        }

        public static void PreprareDefaultAssets()
        {
            // this should load the default config and register them to the AssetRegistry
            // but for now it'll just be a hardcoded list of assets

            // load default mesh
            AVulkanMesh mesh = AVulkanMesh.LoadDefault();
            AssetRegistries.meshes.Add("default", mesh);

            AVulkanMesh UIMesh = new AVulkanMesh();
            MeshImporter importer = new MeshImporter();
            Scene scene1 = importer.ImportFBX("C:\\Users\\gmgyt\\Desktop\\VienetinisPlane.fbx");
            UIMesh.LoadCustomMesh(scene1);
            AssetRegistries.meshes.Add("uidefault", UIMesh);

            // load default font
            FontAsset font = new FontAsset("default");
            font.LoadDefault();

            TextureAsset texture = new TextureAsset("default");
            texture.LoadDefault();

            // load default style
            ControlStyle style = new ControlStyle();
            style.tintDefault = new Silk.NET.Maths.Vector3D<float>(1, 1, 1);
            style.tintHover = new Silk.NET.Maths.Vector3D<float>(1, 1, 1);
            style.tintClick = new Silk.NET.Maths.Vector3D<float>(1, 1, 1);
            AssetRegistries.styles.Add("default", style);
        }

        public static void RegisterFunctions()
        {


        }
    }
}