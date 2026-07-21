using ArctisAurora.Core.Registry;
using Silk.NET.Maths;
using System.Runtime.InteropServices;

namespace ArctisAurora.Core.Data
{
    // Per-entity spatial data, pooled. UI ignores rotation (MCUI bakes scale*translation),
    // so only position + scale are kept for now. Unmanaged/blittable: safe for a pool column
    // and for future direct GPU upload.
    [StructLayout(LayoutKind.Sequential), A_XSDType("TransformData", "Pools")]
    public struct TransformData
    {
        public Vector3D<float> position;
        public Vector3D<float> scale;
    }
}
