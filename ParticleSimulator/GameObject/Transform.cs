using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;

namespace ArctisAurora.GameObject
{
    internal class Transform
    {
        internal Vector3 position = new Vector3(0,0,0);
        internal Vector3 rotation = new Vector3(0,0,0);
        internal Vector3 scale = new Vector3(1,1,1);
        internal Entity parent;

        internal Transform(Entity e)
        {
            parent = e;
        }

        internal void SetRotationFromQuaternion(Quaternion q)
        {
            //Vector3.
        }

        internal Quaternion GetQuaternion()
        {
            float eulerRadiansX = MathHelper.DegreesToRadians(rotation.X);
            float eulerRadiansY = MathHelper.DegreesToRadians(rotation.Y);
            float eulerRadiansZ = MathHelper.DegreesToRadians(rotation.Z);

            Quaternion q = Quaternion.FromEulerAngles(eulerRadiansX, eulerRadiansY, eulerRadiansZ);
            return q;
        }
        internal Vector3 GetEntityRotation()
        {
            return rotation;
        }

        internal Vector3 CalculateRotationFromQuaternion()
        {
            return rotation;
        }

        internal void SetWorldPosition(Vector3 newPos)
        {
            position = newPos;
        }

        internal void SetLocalPosition(Vector3 newPos)
        {

        }

        internal Vector3 GetEntityPosition()
        {
            return position;
        }

        internal Vector3 GetScale()
        {
            return scale;
        }

        internal void SetLocalScale(Vector3 s)
        {
            scale = s;
        }

        internal void SetWorldScale(Vector3 s)
        {
            scale = s;
        }
    }
}