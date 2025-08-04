using ArctisAurora.EngineWork.EngineEntity;
using ArctisAurora.EngineWork.Renderer.MeshSubComponents;
using Silk.NET.Maths;
using Silk.NET.Vulkan;

namespace ArctisAurora.EngineWork.Renderer.UI.Controls
{
    internal class VulkanControl : Entity
    {
        internal MCUI meshComponent;

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

        public VulkanControl()
        {
            EntityManager.AddControl(this);
        }
    }
}
