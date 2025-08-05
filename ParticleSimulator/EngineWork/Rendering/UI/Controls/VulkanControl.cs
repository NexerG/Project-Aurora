using ArctisAurora.EngineWork.ECS.RenderingComponents.Vulkan;
using ArctisAurora.EngineWork.EngineEntity;
using ArctisAurora.EngineWork.Rendering.Helpers;
using ArctisAurora.EngineWork.Rendering.MeshSubComponents;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace ArctisAurora.EngineWork.Rendering.UI.Controls
{
    internal class VulkanControl : Entity
    {
        public struct ControlStyle
        {
            public Vector3D<float> tint;
            public Sampler image;
            public Sampler mask;
        }

        internal Vector3D<float> tintDefault;
        internal Vector3D<float> tintHover;
        internal Vector3D<float> tintClick;
        internal int pointCount;
        internal List<Bezier> contour = new List<Bezier>();

        internal ControlStyle style;

        internal Vector2D<float>[] quadUV = new[]
        {
            new Vector2D<float>(0, 0),
            new Vector2D<float>(1, 0),
            new Vector2D<float>(0, 1),
            new Vector2D<float>(1, 1),
        };
        internal Buffer uvBuffer;
        internal DeviceMemory uvBufferMemory;

        public VulkanControl()
        {
            AVulkanBufferHandler.CreateBuffer(ref quadUV, ref uvBuffer, ref uvBufferMemory, BufferUsageFlags.StorageBufferBit);
            CreateComponent<MeshComponent>();
            EntityManager.AddControl(this);
        }
    }
}
