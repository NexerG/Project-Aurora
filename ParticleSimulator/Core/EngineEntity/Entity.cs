using ArctisAurora.EngineWork.ComponentBehaviour;
using ArctisAurora.EngineWork.ECS.RenderingComponents.Vulkan;
using ArctisAurora.EngineWork.Rendering;
using ArctisAurora.EngineWork.Rendering.MeshSubComponents;
using ArctisAurora.EngineWork.Serialization;
using Windows.Web.UI;

namespace ArctisAurora.EngineWork.EngineEntity
{
    [@Serializable]
    public class Entity
    {
        //variables
        [@Serializable]
        bool enabled = true;
        [@Serializable]
        public Transform transform;
        [@Serializable]
        public string name = "entity";

        [@Serializable]
        public List<EntityComponent> _components = new List<EntityComponent>();
        //public List<Entity> _children = new List<Entity>();
        [NonSerializable]
        public Entity parent;

        private bool _isDirty = true;
        [NonSerializable]
        public bool isDirty
        {
            get => _isDirty;
            set
            {
                _isDirty = value;
                EntityManager.AddEntityToUpdate(this);
                foreach(var child in children)
                {
                    child.isDirty = true;
                }
            }
        }

        public bool MarkDirty()
        {
            isDirty = true;
            return isDirty;
        }

        [@Serializable]
        public List<Entity> children = new List<Entity>();

        public Entity()
        {
            transform = new Transform(this);
            EntityManager.AddEntity(this);
            EntityManager.EntityCreated(this);
        }

        public Entity(string name)
        {
            this.name = name;
            transform = new Transform(this);
        }

        public virtual void OnStart()
        {
            foreach (EntityComponent c in _components)
            {
                c.OnStart();
            }
        }

        public virtual void OnEnable()
        {
            foreach (EntityComponent c in _components)
            {
                c.OnEnable();
            }
        }

        public virtual void OnDisable()
        {
            foreach (EntityComponent c in _components)
            {
                c.OnDisable();
            }
        }

        public virtual void OnTick()
        {
            foreach(EntityComponent c in _components)
            {
                c.OnTick();
            }
        }

        public virtual void OnDestroy()
        {
            foreach(EntityComponent c in _components)
            {
                c.OnDestroy();
                _components.Remove(c);
            }
        }

        internal void IsEnabled(bool state)
        {
            if(enabled != state)
            {
                enabled = state;
                if(enabled)
                    OnEnable();
                else OnDisable();
            }
        }

        public EntComp CreateComponent<EntComp>() where EntComp : EntityComponent, new()
        {
            EntComp component;
            if (typeof(EntComp).Name == typeof(MeshComponent).Name)
            {
                switch (Renderer.renderingModules[0].rendererType)
                {
                    case ERendererTypes.Rasterizer:
                        component = (EntComp)(object)new MCRaster();
                        break;
                    case ERendererTypes.UITemp:
                        component = (EntComp)(object)new MCUI();
                        break;
                    case ERendererTypes.Pathtracer:
                        component = (EntComp)(object)new MCRaytracing();
                        break;
                    default:
                        component = new EntComp();
                        break;
                }
            }
            else {
                component = new EntComp();
            }

            /*EntComp component = typeof(EntComp).Name == typeof(MeshComponent).Name ?
                (VulkanRenderer._rendererType == ERendererTypes.Pathtracer
                    ? (EntComp)(object)new MCRaytracing() 
                    : (VulkanRenderer._rendererType == ERendererTypes.RadianceCascades
                        ? (EntComp)(object)new MCRaster() 
                        : (EntComp)(object)new MCRaster()))
            :  new EntComp();*/
            /*EntComp _component;
            if (typeof(EntComp).Name == "MeshComponent")
            {
                _component = (EntComp)(object)(VulkanRenderer._rendererType == RendererTypes.Pathtracer ?
                new MCRaytracing() : new MCRaster());
            }*/
            if (!_components.Contains(component))
            {
                _components.Add(component);
                component.parent = this;
                component.OnStart();
                return component;
            }
            else return null;
        }

        public EntComp GetComponent<EntComp>() where EntComp : EntityComponent
        {
            foreach(EntityComponent comp in _components)
            {
                if (comp is EntComp)
                    return (EntComp)comp;
            }
            return null;
        }

        public virtual void AddChild(Entity entity)
        {
            children.Add(entity);
            entity.parent = this;
        }

        public virtual Ent CreateChildEntity<Ent>() where Ent : Entity, new()
        {
            Ent entity = new Ent();
            children.Add(entity);
            return entity;
        }

        public virtual Entity GetChildEntityByName(string querryName)
        {
            foreach(Entity ent in children)
            {
                if(ent.name == querryName)
                {
                    return ent;
                }
            }
            return null;
        }

        public virtual List<Entity> GetAllChildrenEntitiesByName(string querryName)
        {
            List<Entity> _childrenByName = new List<Entity>();
            foreach(Entity ent in children)
            {
                if(ent.name == querryName)
                {
                    _childrenByName.Add(ent);
                }
            }
            return _childrenByName;
        }

        public virtual List<Entity> GetAllChildrenEntities()
        {
            return children;
        }

        public EntComp RemoveComponent<EntComp>() where EntComp : EntityComponent
        {
            foreach (EntityComponent ec in _components)
            {
                if(ec is EntComp)
                {
                    _components.Remove(ec);
                    break;
                }
            }
            return null;
        }

        public virtual void Invalidate()
        {
            foreach(EntityComponent c in _components)
            {
                c.OnInvalidate();
            }
            EntityManager.AddEntityToUpdate(this);
        }
    }
}