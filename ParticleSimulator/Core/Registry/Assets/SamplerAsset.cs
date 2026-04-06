using ArctisAurora.Core.Filing.Serialization;
using ArctisAurora.EngineWork.Registry;
using ArctisAurora.EngineWork.Rendering;
using Silk.NET.Vulkan;
using System.Xml.Linq;

namespace ArctisAurora.Core.Registry.Assets
{
    [A_XSDType("SamplerFilter", "Rendering")]
    public enum SamplerFilter { Nearest, Linear }

    [A_XSDType("SamplerAddressMode", "Rendering")]
    public enum SamplerAddressMode { Repeat, MirroredRepeat, ClampToEdge, ClampToBorder }

    [A_XSDType("SamplerMipmapMode", "Rendering")]
    public enum SamplerMipmapMode { Nearest, Linear }

    [A_XSDType("SamplerAsset", "Rendering")]
    public unsafe class SamplerAsset : AbstractAsset
    {
        [A_XSDElementProperty("Name", "Rendering")]
        public string name { get; set; } = "default";

        [A_XSDElementProperty("MagFilter", "Rendering")]
        public SamplerFilter magFilter { get; set; } = SamplerFilter.Nearest;

        [A_XSDElementProperty("MinFilter", "Rendering")]
        public SamplerFilter minFilter { get; set; } = SamplerFilter.Nearest;

        [A_XSDElementProperty("AddressModeU", "Rendering")]
        public SamplerAddressMode addressModeU { get; set; } = SamplerAddressMode.Repeat;

        [A_XSDElementProperty("AddressModeV", "Rendering")]
        public SamplerAddressMode addressModeV { get; set; } = SamplerAddressMode.Repeat;

        [A_XSDElementProperty("AddressModeW", "Rendering")]
        public SamplerAddressMode addressModeW { get; set; } = SamplerAddressMode.Repeat;

        [A_XSDElementProperty("Anisotropy", "Rendering")]
        public bool anisotropyEnable { get; set; } = true;

        [A_XSDElementProperty("MipmapMode", "Rendering")]
        public SamplerMipmapMode mipmapMode { get; set; } = SamplerMipmapMode.Nearest;

        public Sampler handle { get; private set; }

        public SamplerAsset()
        { }

        public SamplerAsset(string name)
        {
            Dictionary<string, SamplerAsset> d = AssetRegistries.GetRegistryByValueType<string, SamplerAsset>(typeof(SamplerAsset));
            d.Add(name, this);
        }

        public override void LoadAsset(AbstractAsset asset, string name, string path)
        {
            Dictionary<string, SamplerAsset> dSamplers = AssetRegistries.GetRegistryByValueType<string, SamplerAsset>(typeof(SamplerAsset));
            string samplerPath = Paths.XMLDOCUMENTS_SAMPLERS + $"\\{name}.xml";
            XElement samplerRoot = XElement.Load(samplerPath);
            XNamespace sns = samplerRoot.GetDefaultNamespace();
            foreach (XElement elem in samplerRoot.Elements(sns + "SamplerAsset"))
            {
                SamplerAsset sa = new SamplerAsset();
                sa.name = elem.Attribute("Name").Value;
                if (elem.Attribute("MagFilter") != null) sa.magFilter = Enum.Parse<SamplerFilter>(elem.Attribute("MagFilter").Value);
                if (elem.Attribute("MinFilter") != null) sa.minFilter = Enum.Parse<SamplerFilter>(elem.Attribute("MinFilter").Value);
                if (elem.Attribute("AddressModeU") != null) sa.addressModeU = Enum.Parse<SamplerAddressMode>(elem.Attribute("AddressModeU").Value);
                if (elem.Attribute("AddressModeV") != null) sa.addressModeV = Enum.Parse<SamplerAddressMode>(elem.Attribute("AddressModeV").Value);
                if (elem.Attribute("AddressModeW") != null) sa.addressModeW = Enum.Parse<SamplerAddressMode>(elem.Attribute("AddressModeW").Value);
                if (elem.Attribute("Anisotropy") != null) sa.anisotropyEnable = bool.Parse(elem.Attribute("Anisotropy").Value);
                if (elem.Attribute("MipmapMode") != null) sa.mipmapMode = Enum.Parse<SamplerMipmapMode>(elem.Attribute("MipmapMode").Value);
                sa.CreateVulkanSampler();
                dSamplers.Add(sa.name, sa);
            }
        }

        public override void LoadDefault()
        {
            Console.WriteLine("Failed to load default Sampler asset - Fault NOT IMPLEMENTED");
        }

        public override void LoadAll(string path)
        {
            Dictionary<string, SamplerAsset> dSamplers = AssetRegistries.GetRegistryByValueType<string, SamplerAsset>(typeof(SamplerAsset));

            string[] files = Directory.GetFiles(Paths.XMLDOCUMENTS_SAMPLERS, "*.xml");
            for (int i = 0; i < files.Length; i++)
            {
                XElement samplerRoot = XElement.Load(files[i]);
                XNamespace sns = samplerRoot.GetDefaultNamespace();
                SamplerAsset sa = new SamplerAsset();
                foreach (XAttribute attr in samplerRoot.Attributes())
                {
                    if (attr.Name == "Name") sa.name = attr.Value;
                    if (attr.Name == "MagFilter") sa.magFilter = Enum.Parse<SamplerFilter>(attr.Value);
                    if (attr.Name == "MinFilter") sa.minFilter = Enum.Parse<SamplerFilter>(attr.Value);
                    if (attr.Name == "AddressModeU") sa.addressModeU = Enum.Parse<SamplerAddressMode>(attr.Value);
                    if (attr.Name == "AddressModeV") sa.addressModeV = Enum.Parse<SamplerAddressMode>(attr.Value);
                    if (attr.Name == "AddressModeW") sa.addressModeW = Enum.Parse<SamplerAddressMode>(attr.Value);
                    if (attr.Name == "Anisotropy") sa.anisotropyEnable = bool.Parse(attr.Value);
                    if (attr.Name == "MipmapMode") sa.mipmapMode = Enum.Parse<SamplerMipmapMode>(attr.Value);
                }
                sa.CreateVulkanSampler();
                dSamplers.Add(sa.name, sa);
            }
        }

        public void CreateVulkanSampler()
        {
            Renderer.vk.GetPhysicalDeviceProperties(Renderer.gpu, out PhysicalDeviceProperties props);

            SamplerCreateInfo info = new SamplerCreateInfo()
            {
                SType = StructureType.SamplerCreateInfo,
                MagFilter = (Filter)magFilter,
                MinFilter = (Filter)minFilter,
                AddressModeU = (Silk.NET.Vulkan.SamplerAddressMode)addressModeU,
                AddressModeV = (Silk.NET.Vulkan.SamplerAddressMode)addressModeV,
                AddressModeW = (Silk.NET.Vulkan.SamplerAddressMode)addressModeW,
                AnisotropyEnable = anisotropyEnable,
                MaxAnisotropy = anisotropyEnable ? props.Limits.MaxSamplerAnisotropy : 1f,
                BorderColor = BorderColor.IntOpaqueBlack,
                UnnormalizedCoordinates = false,
                CompareEnable = false,
                CompareOp = CompareOp.Always,
                MipmapMode = (Silk.NET.Vulkan.SamplerMipmapMode)mipmapMode,
            };

            Sampler sampler;
            Result r = Renderer.vk.CreateSampler(Renderer.logicalDevice, ref info, null, &sampler);
            if (r != Result.Success)
                throw new Exception("Failed to create sampler: " + r);
            handle = sampler;
        }
    }
}
