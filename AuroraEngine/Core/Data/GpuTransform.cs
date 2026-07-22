using ArctisAurora.Core.Registry;
using Silk.NET.Maths;
using System.Runtime.InteropServices;

namespace ArctisAurora.Core.Data
{
    // Packed, GPU-ready transform: the baked position*scale matrix mirrored from TransformData.
    // Lives as a pool column so it stays index-aligned with TransformData through the pool's
    // grow / compact / resequence, instead of a parallel array the renderer manages by hand.
    // A blittable wrapper exists only so the column can be declared in Pools.xml — Matrix4X4<float>
    // itself is not an [A_XSDType] and can't be annotated. Layout is identical to Matrix4X4<float>[],
    // so the GPU upload is unchanged.
    [StructLayout(LayoutKind.Sequential), A_XSDType("GpuTransform", "DataPools")]
    public struct GpuTransform
    {
        public Matrix4X4<float> matrix;
    }
}
