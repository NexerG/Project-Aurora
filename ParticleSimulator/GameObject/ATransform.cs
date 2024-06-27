using OpenTK.Mathematics;
using Silk.NET.Maths;

namespace ArctisAurora.GameObject
{
    internal class ATransform
    {
        internal Vector3D<float> position = new Vector3D<float>(0, 0, 0);
        internal Vector3D<float> rotation = new Vector3D<float>(0, 0, 0);
        internal Vector3D<float> scale = new Vector3D<float>(1, 1, 1);
        internal Entity parent;

        internal ATransform(Entity e)
        {
            parent = e;
        }

        internal void SetRotationFromQuaternion(Quaternion<float> q)
        {
            //Vector3.
        }

        internal Quaternion<float> GetQuaternion()
        {
            float eulerRadiansX = MathHelper.DegreesToRadians(rotation.X);
            float eulerRadiansY = MathHelper.DegreesToRadians(rotation.Y);
            float eulerRadiansZ = MathHelper.DegreesToRadians(rotation.Z);

            Quaternion<float> q = Quaternion<float>.CreateFromYawPitchRoll(eulerRadiansX, eulerRadiansY, eulerRadiansZ);
            return q;
        }
        internal Vector3D<float> GetEntityRotation()
        {
            return rotation;
        }

        internal Vector3D<float> CalculateRotationFromQuaternion()
        {
            return rotation;
        }

        internal void SetWorldPosition(Vector3D<float> newPos)
        {
            position = newPos;
        }

        internal void SetLocalPosition(Vector3D<float> newPos)
        {

        }

        internal Vector3D<float> GetEntityPosition()
        {
            return position;
        }

        internal Vector3D<float> GetScale()
        {
            return scale;
        }

        internal void SetLocalScale(Vector3D<float> s)
        {
            scale = s;
        }

        internal void SetWorldScale(Vector3D<float> s)
        {
            scale = s;
        }
    }
}