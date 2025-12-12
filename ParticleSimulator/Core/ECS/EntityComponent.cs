using ArctisAurora.EngineWork.EngineEntity;
using ArctisAurora.EngineWork.Serialization;
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
