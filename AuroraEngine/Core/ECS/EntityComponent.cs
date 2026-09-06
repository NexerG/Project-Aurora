using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.Filing.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ArctisAurora.EngineWork.ComponentBehaviour
{
    [@Serializable]
    public class EntityComponent
    {
        [NonSerializable]
        public Entity parent;
        [NonSerializable]
        internal bool started;

        // A component attaches to any entity; one that reads a transform requires its entity to
        // carry one, which is what the cast asserts.
        protected ref Core.Data.TransformData transform => ref ((TransformEntity)parent).transform;

        public virtual void OnStart() //runs on creation of the component in the world
        {

        }

        public virtual void OnEnable()
        {

        }

        public virtual void OnDisable()
        {

        }

        public virtual void OnTick() //runs on every frame
        {

        }

        public virtual void OnDestroy() //executes on destruction of the component
        {

        }

        public virtual void OnInvalidate()
        {

        }
    }
}
