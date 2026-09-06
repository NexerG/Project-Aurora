using ArctisAurora.Core.Data;
using Silk.NET.Maths;

namespace ArctisAurora.Core.ECS.EngineEntity
{
    // An entity whose pool declares a TransformData column. Everything positional in the world
    // derives from this; an entity that carries no transform derives from Entity and pays nothing
    // for one, in the object or in the pool.
    public class TransformEntity : Entity
    {
        // This entity's pooled transform (position/rotation/scale) — a ref into the dense array.
        // Direct writes (transform.position = ...) are allowed and fast but do NOT mark the pool
        // dirty; use the Set* helpers below when the change must be re-uploaded.
        public ref TransformData transform => ref Pool.GetRef<TransformData>(dataHandle);

        public TransformEntity() : base() { }

        public TransformEntity(string name) : base(name) { }

        protected override void AllocatePooledData()
        {
            base.AllocatePooledData();
            transform.scale = new Vector3D<float>(1, 1, 1);   // preserve the old default scale
        }

        public void SetPosition(Vector3D<float> position)
        {
            transform.position = position;
            Pool.MarkContentDirty(dataHandle);
        }

        public void SetScale(Vector3D<float> scale)
        {
            transform.scale = scale;
            Pool.MarkContentDirty(dataHandle);
        }

        public void SetRotation(Vector3D<float> rotation)
        {
            transform.rotation = rotation;
            Pool.MarkContentDirty(dataHandle);
        }

        public void SetTransform(TransformData value)
        {
            transform = value;
            Pool.MarkContentDirty(dataHandle);
        }
    }
}
