using ArctisAurora.EngineWork.ComponentBehaviour;
using ArctisAurora.EngineWork.ECS.RenderingComponents.Vulkan;
using ArctisAurora.EngineWork.Rendering;
using ArctisAurora.EngineWork.Rendering.MeshSubComponents;

namespace ArctisAurora.EngineWork.EngineEntity
{
    public class Entity
    {
        //variables
        bool enabled = true;
        public Transform transform;
        public string name = "entity";

        public List<EntityComponent> _components = new List<EntityComponent>();
        public List<Entity> _children = new List<Entity>();

        public Entity()
        {
            transform = new Transform(this);
            EntityManager.AddEntity(this);
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

        public Ent CreateChildEntity<Ent>() where Ent : Entity, new()
        {
            Ent entity = new Ent();
            _children.Add(entity);
            return entity;
        }

        public Entity GetChildEntityByName(string querryName)
        {
            foreach(Entity ent in _children)
            {
                if(ent.name == querryName)
                {
                    return ent;
                }
            }
            return null;
        }

        public List<Entity> GetAllChildrenEntitiesByName(string querryName)
        {
            List<Entity> _childrenByName = new List<Entity>();
            foreach(Entity ent in _children)
            {
                if(ent.name == querryName)
                {
                    _childrenByName.Add(ent);
                }
            }
            return _childrenByName;
        }

        public List<Entity> GetAllChildrenEntities()
        {
            return _children;
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
    }
}