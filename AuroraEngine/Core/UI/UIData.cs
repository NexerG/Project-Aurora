using ArctisAurora.Core.Registry;
using Silk.NET.Maths;
using System.Runtime.InteropServices;

namespace ArctisAurora.Core.UI
{
    // Which of the three quad kinds a VulkanControl row draws.
    public enum VulkanControlType : uint
    {
        MTSDFControl,
        PanelControl,
        ImageControl
    }

    public struct LayoutRect
    {
        public float x;
        public float y;
        public float width;
        public float height;

        public LayoutRect(float x, float y, float width, float height)
        {
            this.x = x;
            this.y = y;
            this.width = width;
            this.height = height;
        }

        public float Right => x + width;
        public float Bottom => y + height;

        public static LayoutRect Empty => new LayoutRect(0, 0, 0, 0);
    }

    public struct Thickness
    {
        public float top;
        public float right;
        public float bottom;
        public float left;

        public Thickness(float uniform)
        {
            top = right = bottom = left = uniform;
        }

        public float totalHorizontal => left + right;
        public float totalVertical => top + bottom;

        public static Thickness Zero => new Thickness(0);
    }

    public struct QuadUVs
    {
        public Vector2D<float> uv1;
        public Vector2D<float> uv2;
        public Vector2D<float> uv3;
        public Vector2D<float> uv4;
    }

    // Measure and arrange, CPU only — never mirrored to the GPU.
    [StructLayout(LayoutKind.Sequential, Pack = 1), A_XSDType("ArrangeData", "DataPools")]
    public struct ArrangeData
    {
        // authored size
        public float width;
        public float height;
        public float preferredWidth;
        public float preferredHeight;
        public float minWidth;
        public float minHeight;
        public float widthStar;
        public float heightStar;

        // authored box
        public Thickness margin;
        public Thickness padding;

        // authored placement
        public float horizontalPosition;
        public float verticalPosition;
        public byte horizontalAlignment;
        public byte verticalAlignment;
        public byte dockMode;
        public byte flags;
        public short gridColumn;
        public short gridRow;

        // arrange output
        public LayoutRect arranged;
        public LayoutRect clip;
        public Vector2D<float> desired;

        // collision and insert caches
        public LayoutRect subtreeBounds;
        public int subtreeCount;
    }

    // Arrange's GPU-side output, one row per drawn quad.
    [StructLayout(LayoutKind.Sequential, Pack = 1), A_XSDType("ControlGeometry", "DataPools")]
    public struct ControlGeometry
    {
        public Matrix4X4<float> matrix;
        public Vector4D<float> clip;
        public Vector4D<float> gradientRect;
    }

    // Paint, one row per drawn quad. XSD-named VulkanControlData so it does not collide with the
    // outgoing UISystem.Controls.VulkanControl, whose [A_XSDType] name is resolved by name alone.
    [StructLayout(LayoutKind.Sequential, Pack = 1), A_XSDType("VulkanControlData", "DataPools")]
    public struct VulkanControl
    {
        public VulkanControlType type;
        public QuadUVs uvs;
        public Vector4D<float> tint;
        public uint textureIndex;
        public Vector4D<float> cornerRadius;
        // stroke, in design pixels — against the MSDF silhouette on MTSDFControl, the rounded box otherwise
        public Vector3D<float> edgeColor;
        public float edgeThickness;
        public uint gradientIndex;
    }
}
