using ArctisAurora.Core.Filing.Serialization;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Data;
using Silk.NET.Maths;
using ArctisAurora.EngineWork.ComponentBehaviour;
using ArctisAurora.EngineWork.ECS.RenderingComponents.Vulkan;
using ArctisAurora.EngineWork.Rendering;
using ArctisAurora.EngineWork.Rendering.MeshSubComponents;

namespace ArctisAurora.Core.ECS.EngineEntity
{
    [A_XSDType("Entity", "EntityRegistry")]
    [@Serializable]
    public class Entity
    {
        //variables
        [@Serializable]
        bool enabled = true;
        [@Serializable]
        [A_XSDElementProperty("Name", "EntityRegistry", "Identifier for FindByName lookups. Not required to be unique.")]
        public string name = "entity";

        [@Serializable]
        public List<EntityComponent> _components = new List<EntityComponent>();
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
                EntityRegistry.AddToGroup("EntitiesToUpdate", this);
                foreach (var child in children)
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

        #region ---- data pool ----
        // Which pool this entity's row lives in, and therefore which component columns it has.
        // Overridden by subclasses (e.g. controls use "UIElements"). Resolved during construction,
        // so it must not touch derived fields.
        protected virtual string PoolName => "Entities";
        private DataPool _pool = null!;
        internal DataHandle dataHandle;
        public DataPool Pool => _pool;

        // rows this entity holds in pools other than its own
        private DataHandle[]? _extraHandles;

        // Binds the entity to its pool and takes its row. An override adds rows in other pools
        // through AllocateIn, and seeds whatever columns its pool declares.
        protected virtual void AllocatePooledData()
        {
            _pool = DataManager.Get(PoolName);
            dataHandle = _pool.Allocate(this);
        }

        // A row in a second pool, freed with the entity.
        protected DataHandle AllocateIn(string poolName)
        {
            DataHandle handle = DataManager.Get(poolName).Allocate(this);

            if (_extraHandles == null)
                _extraHandles = new[] { handle };
            else
            {
                Array.Resize(ref _extraHandles, _extraHandles.Length + 1);
                _extraHandles[^1] = handle;
            }
            return handle;
        }

        // Releases one extra row ahead of the entity's own teardown.
        protected void FreeIn(DataHandle handle)
        {
            if (_extraHandles == null) return;

            int index = Array.IndexOf(_extraHandles, handle);
            if (index < 0) return;

            DataManager.Get(handle.PoolId).Free(handle);
            for (int i = index; i < _extraHandles.Length - 1; i++)
                _extraHandles[i] = _extraHandles[i + 1];
            Array.Resize(ref _extraHandles, _extraHandles.Length - 1);
        }

        // Every row the entity holds, in every pool. A handle names its own pool, so nothing has
        // to remember which.
        internal void FreePooledData()
        {
            _pool.Free(dataHandle);

            if (_extraHandles == null) return;
            for (int i = 0; i < _extraHandles.Length; i++)
                DataManager.Get(_extraHandles[i].PoolId).Free(_extraHandles[i]);
            _extraHandles = null;
        }

        private bool _destroyed = false;

        // Tear this entity (and its whole subtree) down. Deferred: the subtree is enqueued now
        // and actually unregistered + pool-freed at the next frame boundary (EntityRegistry.
        // ProcessDestroys -> DataManager.FrameEdge), so it is safe to call from inside OnTick or
        // an input handler without mutating the live iteration lists or moving pool memory.
        public void Destroy()
        {
            if (_destroyed) return;
            if (parent != null)
            {
                parent.children.Remove(this);   // detach the subtree root from the live tree
                parent = null;
            }
            EnqueueSubtree(this);
        }

        private static void EnqueueSubtree(Entity entity)
        {
            entity._destroyed = true;
            EntityRegistry.EnqueueDestroy(entity);
            foreach (Entity child in entity.children)
                EnqueueSubtree(child);
        }
        #endregion

        public Entity()
        {
            AllocatePooledData();
            EntityRegistry.AddToGroup("Entities", this);
            EntityRegistry.EnqueueStart(this);
        }

        public Entity(string name)
        {
            this.name = name;
            AllocatePooledData();
            EntityRegistry.AddToGroup("Entities", this);
            EntityRegistry.EnqueueStart(this);
        }

        #region ---- lifecycle ----
        // driven by EntityRegistry's queues, never by the mutation that caused them
        [NonSerializable]
        private bool _started = false;
        [NonSerializable]
        private bool _notifiedEnabled = false;
        [NonSerializable]
        private bool _enableQueued = false;

        internal bool tickable => _notifiedEnabled && !_destroyed;

        // Runs the queued OnStart once, then queues the entity's first enable notification.
        internal void BeginLife()
        {
            if (_started || _destroyed) return;

            _started = true;
            OnStart();
            QueueEnableChange();
        }

        // Fires OnEnable/OnDisable only when the flag actually moved since the last notification.
        internal void ApplyEnableChange()
        {
            _enableQueued = false;
            if (_destroyed || !_started || enabled == _notifiedEnabled) return;

            _notifiedEnabled = enabled;
            if (_notifiedEnabled) OnEnable();
            else OnDisable();
        }

        private void QueueEnableChange()
        {
            if (_enableQueued) return;

            _enableQueued = true;
            EntityRegistry.EnqueueEnableChange(this);
        }

        // One start per component, whether it is attached before or after the entity's own OnStart.
        private static void StartComponent(EntityComponent component)
        {
            if (component.started) return;

            component.started = true;
            component.OnStart();
        }
        #endregion

        public virtual void OnStart()
        {
            foreach (EntityComponent c in _components)
            {
                StartComponent(c);
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
            }
            _components.Clear();
        }

        internal void IsEnabled(bool state)
        {
            if (enabled == state) return;

            enabled = state;
            QueueEnableChange();
        }

        public EntComp CreateComponent<EntComp>() where EntComp : EntityComponent, new()
        {
            EntComp component;
            if (typeof(EntComp).Name == typeof(MeshComponent).Name)
            {
                switch (Renderer.PrimaryRendererType)
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
                StartComponent(component);
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

        public virtual void RemoveChild(Entity entity)
        {
            if (!children.Remove(entity)) return;
            entity.parent = null;
        }

        // Moves a live subtree to another parent, attaching through that parent's own AddChild.
        public void SetParent(Entity newParent)
        {
            if (newParent == parent) return;
            if (newParent == this || IsAncestorOf(newParent))
                throw new Exception("Cannot parent an entity to itself or to its own descendant");

            parent?.RemoveChild(this);
            newParent.AddChild(this);
        }

        private bool IsAncestorOf(Entity entity)
        {
            Entity current = entity.parent;
            while (current != null)
            {
                if (current == this) return true;
                current = current.parent;
            }
            return false;
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

        // First entity in this subtree, itself included, carrying the name.
        public virtual Entity FindByName(string querryName)
        {
            if (name == querryName) return this;

            foreach (Entity ent in children)
            {
                Entity found = ent.FindByName(querryName);
                if (found != null) return found;
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
            for (int i = 0; i < _components.Count; i++)
            {
                if (_components[i] is not EntComp match) continue;

                _components.RemoveAt(i);
                return match;
            }
            return null;
        }

        public virtual void Invalidate()
        {
            foreach(EntityComponent c in _components)
            {
                c.OnInvalidate();
            }
            EntityRegistry.AddToGroup("EntitiesToUpdate", this);
        }
    }
}