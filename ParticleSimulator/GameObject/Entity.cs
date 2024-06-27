using ArctisAurora.EngineWork.ComponentBehaviour;

namespace ArctisAurora.GameObject
{
    internal class Entity
    {
        //variables
        bool enabled = true;
        internal Transform transform;
        internal string name = "entity";

        internal List<EntityComponent> _components = new List<EntityComponent>();
        internal List<Entity> _children = new List<Entity>();

        public Entity()
        {
            transform = new Transform(this);
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

        internal EntComp CreateComponent<EntComp>() where EntComp : EntityComponent, new()
        {
            EntComp component = new EntComp();
            if (!_components.Contains(component))
            {
                _components.Add(component);
                component.parent = this;
                component.OnStart();
                return component;
            }
            else return null;
        }
        internal EntComp GetComponent<EntComp>() where EntComp : EntityComponent
        {
            foreach(EntityComponent comp in _components)
            {
                if (comp is EntComp)
                    return (EntComp)comp;
            }
            return null;
        }

        internal Ent CreateChildEntity<Ent>() where Ent : Entity, new()
        {
            Ent entity = new Ent();
            _children.Add(entity);
            return entity;
        }

        internal Entity GetChildEntityByName(string querryName)
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

        internal List<Entity> GetAllChildrenEntitiesByName(string querryName)
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

        internal List<Entity> GetAllChildrenEntities()
        {
            return _children;
        }
        internal EntComp RemoveComponent<EntComp>() where EntComp : EntityComponent
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