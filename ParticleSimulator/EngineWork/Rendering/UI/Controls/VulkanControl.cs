using ArctisAurora.EngineWork.AssetRegistry;
using ArctisAurora.EngineWork.EngineEntity;
using ArctisAurora.EngineWork.Rendering.Helpers;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using System.Runtime.InteropServices;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace ArctisAurora.EngineWork.Rendering.UI.Controls
{
    internal class VulkanControl : Entity
    {
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct ControlStyle
        {
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
            public Vector2D<float> uv1 = new Vector2D<float>(0, 0);
            public Vector2D<float> uv2 = new Vector2D<float>(1, 0);
            public Vector2D<float> uv3 = new Vector2D<float>(0, 1);
            public Vector2D<float> uv4 = new Vector2D<float>(1, 1);

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

        internal Vector2D<float> px = new Vector2D<float>(72, 72);

        internal ControlData controlData;
        internal Buffer controlDataBuffer;
        internal DeviceMemory controlDataBufferMemory;

        public VulkanControl()
        {
            controlData = new ControlData();
            controlData.style = ControlStyle.Default();
            controlData.quadData = new QuadData();
            AVulkanBufferHandler.CreateBuffer(ref controlData, ref controlDataBuffer, ref controlDataBufferMemory, BufferUsageFlags.StorageBufferBit);
        }

        public override void OnStart()
        {
            base.OnStart();
            EntityManager.AddControl(this);
        }
    }
}