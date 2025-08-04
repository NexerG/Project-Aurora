using ArctisAurora.EngineWork.Renderer;
using Silk.NET.Maths;

namespace ArctisAurora.EngineWork.EngineEntity
{
    public class Transform
    {
        public Vector3D<float> position = new Vector3D<float>(0, 0, 0);
        public Vector3D<float> rotation = new Vector3D<float>(0, 0, 0);
        public Vector3D<float> scale = new Vector3D<float>(1, 1, 1);
        internal Entity parent;
        internal bool _changed = false;

        internal Transform(Entity e)
        {
            parent = e;
        }

        public void SetRotationFromQuaternion(Quaternion<float> q)
        {
            //Vector3.
            VulkanRenderer._rendererInstance.AddEntityToUpdate(parent);
        }

        public void SetRotationFromVector3(Vector3D<float> _r)
        {
            rotation = _r;
            _changed = true;
            VulkanRenderer._rendererInstance.AddEntityToUpdate(parent);
        }

        public Quaternion<float> GetQuaternion()
        {
            float eulerRadiansX = DegreesToRadians(rotation.X);
            float eulerRadiansY = DegreesToRadians(rotation.Y);
            float eulerRadiansZ = DegreesToRadians(rotation.Z);

            Quaternion<float> q = Quaternion<float>.CreateFromYawPitchRoll(eulerRadiansX, eulerRadiansY, eulerRadiansZ);
            return q;
        }
        public Vector3D<float> GetEntityRotation()
        {
            return rotation;
        }

        public Vector3D<float> CalculateRotationFromQuaternion()
        {
            return rotation;
        }

        public void SetWorldPosition(Vector3D<float> newPos)
        {
            position = newPos;
            _changed = true;
            VulkanRenderer._rendererInstance.AddEntityToUpdate(parent);
        }

        public void SetLocalPosition(Vector3D<float> newPos)
        {
            _changed = true;
            VulkanRenderer._rendererInstance.AddEntityToUpdate(parent);
        }

        public Vector3D<float> GetEntityPosition()
        {
            return position;
        }

        public Vector3D<float> GetScale()
        {
            return scale;
        }

        public void SetLocalScale(Vector3D<float> s)
        {
            scale = s;
            _changed = true;
            VulkanRenderer._rendererInstance.AddEntityToUpdate(parent);
        }

        public void SetWorldScale(Vector3D<float> s)
        {
            scale = s;
            _changed = true;
            VulkanRenderer._rendererInstance.AddEntityToUpdate(parent);
        }

        private float DegreesToRadians(float degrees)
        {
            return degrees * (MathF.PI / 180.0f);
        }
    }
}