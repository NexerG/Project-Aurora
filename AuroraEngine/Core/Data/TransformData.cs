using ArctisAurora.Core.Registry;
using Silk.NET.Maths;
using System.Runtime.InteropServices;

namespace ArctisAurora.Core.Data
{
    // Per-entity spatial data, pooled. Unmanaged/blittable: safe for a pool column and for
    // future direct GPU upload. Methods are pure data ops — they can't cascade to child
    // entities or mark the pool dirty (the struct has no entity/pool access); callers handle
    // hierarchy propagation and MarkContentDirty.
    [StructLayout(LayoutKind.Sequential), A_XSDType("TransformData", "DataPools")]
    public struct TransformData
    {
        public Vector3D<float> position;
        public Vector3D<float> scale;
        public Vector3D<float> rotation;

        // ---- position ----
        public void SetWorldPosition(Vector3D<float> worldPosition) => position = worldPosition;
        public void MoveToPosition(Vector3D<float> newPosition) => position = newPosition;
        public void SetLocalPosition(Vector3D<float> delta) => position += delta;
        public void MoveLocalPosition(Vector3D<float> localOffset) => position += localOffset;
        public Vector3D<float> GetEntityPosition() => position;

        // ---- scale ----
        public void SetWorldScale(Vector3D<float> s) => scale = s;
        public void SetLocalScale(Vector3D<float> s) => scale = s;
        public Vector3D<float> GetScale() => scale;

        // ---- rotation ----
        public void SetRotationFromVector3(Vector3D<float> r) => rotation = r;
        public Vector3D<float> GetEntityRotation() => rotation;
        public Vector3D<float> CalculateRotationFromQuaternion() => rotation;

        public Quaternion<float> GetQuaternion()
        {
            float x = DegreesToRadians(rotation.X);
            float y = DegreesToRadians(rotation.Y);
            float z = DegreesToRadians(rotation.Z);
            return Quaternion<float>.CreateFromYawPitchRoll(x, y, z);
        }

        private static float DegreesToRadians(float degrees) => degrees * (MathF.PI / 180.0f);
    }
}
