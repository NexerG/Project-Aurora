using ArctisAurora.EngineWork.Rendering;
using ArctisAurora.EngineWork.Serialization;
using Silk.NET.Maths;

namespace ArctisAurora.Core.ECS.EngineEntity
{
    public class Transform
    {
        [@Serializable]
        public Vector3D<float> position = new Vector3D<float>(0, 0, 0);
        [@Serializable]
        public Vector3D<float> rotation = new Vector3D<float>(0, 0, 0);
        [@Serializable]
        public Vector3D<float> scale = new Vector3D<float>(1, 1, 1);

        [NonSerializable]
        internal Entity parent;

        [@Serializable]
        internal bool _changed = false;

        public Transform() {}

        internal Transform(Entity e)
        {
            parent = e;
        }

        public virtual void SetRotationFromQuaternion(Quaternion<float> q)
        {
            parent.MarkDirty();
        }

        public virtual void SetRotationFromVector3(Vector3D<float> _r)
        {
            rotation = _r;
            _changed = true;
            parent.MarkDirty();
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

        public virtual void SetWorldPosition(Vector3D<float> newPos)
        {
            position = newPos;
            _changed = true;
            parent.MarkDirty();
        }

        public virtual void MoveToPosition(Vector3D<float> newPos)
        {
            SetWorldPosition(newPos);
            Vector3D<float> delta = newPos - position;
            foreach (Entity child in parent.children)
            {
                child.transform.MoveLocalPosition(delta);
            }
            parent.MarkDirty();
        }

        public virtual void SetLocalPosition(Vector3D<float> delta)
        {
            _changed = true;
            position += delta;
            /*foreach (Entity child in parent.children)
            {
                child.transform.SetLocalPosition(position);
            }*/
            parent.MarkDirty();
        }

        public virtual void MoveLocalPosition(Vector3D<float> delta)
        {
            position += delta;
            foreach (Entity child in parent.children)
            {
                child.transform.MoveLocalPosition(delta);
            }
            _changed = true;
            parent.MarkDirty();
        }

        public Vector3D<float> GetEntityPosition()
        {
            return position;
        }

        public Vector3D<float> GetScale()
        {
            return scale;
        }

        public virtual void SetLocalScale(Vector3D<float> s)
        {
            scale = s;
            _changed = true;
            parent.MarkDirty();
        }

        public virtual void SetWorldScale(Vector3D<float> s)
        {
            scale = s;
            _changed = true;
            parent.MarkDirty();
        }

        private float DegreesToRadians(float degrees)
        {
            return degrees * (MathF.PI / 180.0f);
        }
    }
}