using ArctisAurora.Core.Registry;
using System.Numerics;
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
        public Vector3 position;
        public Vector3 scale;
        public Vector3 rotation;

        // ---- position ----
        public void SetWorldPosition(Vector3 worldPosition) => position = worldPosition;
        public void MoveToPosition(Vector3 newPosition) => position = newPosition;
        public void SetLocalPosition(Vector3 delta) => position += delta;
        public void MoveLocalPosition(Vector3 localOffset) => position += localOffset;
        public Vector3 GetEntityPosition() => position;

        // ---- scale ----
        public void SetWorldScale(Vector3 s) => scale = s;
        public void SetLocalScale(Vector3 s) => scale = s;
        public Vector3 GetScale() => scale;

        // ---- rotation ----
        public void SetRotationFromVector3(Vector3 r) => rotation = r;
        public Vector3 GetEntityRotation() => rotation;
        public Vector3 CalculateRotationFromQuaternion() => rotation;

        public Quaternion GetQuaternion()
        {
            float x = DegreesToRadians(rotation.X);
            float y = DegreesToRadians(rotation.Y);
            float z = DegreesToRadians(rotation.Z);
            return Quaternion.CreateFromYawPitchRoll(x, y, z);
        }

        private static float DegreesToRadians(float degrees) => degrees * (MathF.PI / 180.0f);
    }
}
