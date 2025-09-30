using ArctisAurora.EngineWork.AssetRegistry;
using ArctisAurora.EngineWork.EngineEntity;
using ArctisAurora.EngineWork.Rendering.Helpers;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using System.Runtime.InteropServices;
using System.Xml.Serialization;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace ArctisAurora.EngineWork.Rendering.UI.Controls
{

    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class A_VulkanControlAttribute : Attribute
    {
        public string Name { get; }
        public A_VulkanControlAttribute(string name)
        {
            Name = name;
        }
    }

    public interface IControlChild
    {
        public void AddChild(VulkanControl control);
    }

    public unsafe class VulkanControl : Entity, IControlChild
    {
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct ControlStyle
        {
            [XmlAttribute("Style")]
            public Vector3D<float> tintDefault;
            public Vector3D<float> tintHover;
            public Vector3D<float> tintClick;
            //public Sampler image;
            //public Sampler mask;

            public static ControlStyle Default()
            {
                return AssetRegistries.styles.GetValueOrDefault("default");
            }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct QuadUVs
        {
            public Vector2D<float> uv1;
            public Vector2D<float> uv2;
            public Vector2D<float> uv3;
            public Vector2D<float> uv4;

            public QuadUVs(Vector2D<float> uv1, Vector2D<float> uv2, Vector2D<float> uv3, Vector2D<float> uv4)
            {
                this.uv1 = uv1;
                this.uv2 = uv2;
                this.uv3 = uv3;
                this.uv4 = uv4;
            }

            public QuadUVs(Vector2D<float>[] uvs)
            {
                uv1 = uvs[0];
                uv2 = uvs[1];
                uv3 = uvs[2];
                uv4 = uvs[3];
            }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct QuadOffsets
        {
            public Vector3D<float> offset1; // bot left
            public Vector3D<float> offset2; // bot right
            public Vector3D<float> offset3; // top right
            public Vector3D<float> offset4; // top left

            public QuadOffsets(Vector3D<float> offset1, Vector3D<float> offset2, Vector3D<float> offset3, Vector3D<float> offset4)
            {
                this.offset1 = offset1;
                this.offset2 = offset2;
                this.offset3 = offset3;
                this.offset4 = offset4;
            }

            public QuadOffsets(Vector3D<float>[] offsets)
            {
                offset1 = offsets[0];
                offset2 = offsets[1];
                offset3 = offsets[2];
                offset4 = offsets[3];
            }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct QuadData
        {
            public QuadUVs uvs;
            public QuadOffsets offsets;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct ControlData()
        {
            public QuadData quadData;
            public ControlStyle style;
        }

        public Vector2D<float> px = new Vector2D<float>(72, 72);


        public ControlData controlData;
        public Buffer controlDataBuffer;
        public DeviceMemory controlDataBufferMemory;

        public Sampler maskSampler;
        public TextureAsset maskAsset;

        public Sampler colorSampler;
        public TextureAsset colorAsset;

        public VulkanControl? child;

        public VulkanControl()
        {
            controlData = new ControlData();
            controlData.style = ControlStyle.Default();
            controlData.quadData = new QuadData();

            maskAsset = AssetRegistries.textures.GetValueOrDefault("default");
            transform.SetWorldScale(new Vector3D<float>(1, px.X, px.Y));

            ControlData tempData = controlData;
            AVulkanBufferHandler.CreateBuffer(ref tempData, ref controlDataBuffer, ref controlDataBufferMemory, BufferUsageFlags.StorageBufferBit);
            CreateSampler();
            EntityManager.AddControl(this);
        }

        public override void OnStart()
        {
            base.OnStart();
        }

        private void CreateSampler()
        {
            Renderer.vk.GetPhysicalDeviceProperties(Renderer.gpu, out PhysicalDeviceProperties _properties);
            SamplerCreateInfo _createInfo = new SamplerCreateInfo()
            {
                SType = StructureType.SamplerCreateInfo,
                MagFilter = Filter.Nearest,
                MinFilter = Filter.Nearest,
                AddressModeU = SamplerAddressMode.Repeat,
                AddressModeV = SamplerAddressMode.Repeat,
                AddressModeW = SamplerAddressMode.Repeat,
                AnisotropyEnable = true,
                MaxAnisotropy = _properties.Limits.MaxSamplerAnisotropy,
                BorderColor = BorderColor.IntOpaqueBlack,
                UnnormalizedCoordinates = false,
                CompareEnable = false,
                CompareOp = CompareOp.Always,
                MipmapMode = SamplerMipmapMode.Nearest
            };

            fixed (Sampler* _textureSamplerPtr = &maskSampler)
            {
                Result r = Renderer.vk.CreateSampler(Renderer.logicalDevice, ref _createInfo, null, _textureSamplerPtr);
                if (r != Result.Success)
                {
                    throw new Exception("Failed to create a texture sampler with error: " + r);
                }
            }
        }

        internal void UpdateControlData()
        {
            AVulkanBufferHandler.UpdateBuffer(ref controlData, ref controlDataBuffer, ref controlDataBufferMemory, BufferUsageFlags.StorageBufferBit);
        }

        public void AddChild(VulkanControl control)
        {
            if(child != null) throw new Exception("Control can only have one child");
            child = control;
        }
    }
}