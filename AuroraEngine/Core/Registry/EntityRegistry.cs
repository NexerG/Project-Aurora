using ArctisAurora.Core.Filing.Serialization;
using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.UISystem.Controls;
using System.Collections;
using System.Xml.Linq;

using ArctisAurora.EngineWork;

namespace ArctisAurora.Core.Registry
{
    public class EntityGroup
    {
        public string name;
        public Type elementType;
        internal object _list;
        public Action onChanged;

        public int count => ((IList)_list).Count;

        public EntityGroup(string name, Type elementType)
        {
            this.name = name;
            this.elementType = elementType;
            _list = Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType));
        }

        public void Add(object item)
        {
            ((IList)_list).Add(item);
            onChanged?.Invoke();
        }

        public void Remove(object item)
        {
            ((IList)_list).Remove(item);
            onChanged?.Invoke();
        }

        public void Clear()
        {
            ((IList)_list).Clear();
        }

        public void RemoveRange(int start, int count)
        {
            IList list = (IList)_list;
            for (int i = start + count - 1; i >= start; i--)
                list.RemoveAt(i);
        }

        public List<T> As<T>()
        {
            return (List<T>)_list;
        }

        public IList AsList()
        {
            return (IList)_list;
        }
    }

    [A_XSDType("Entry", "EntityRegistry")]
    public class EntityRegistryEntry
    {
        [A_XSDElementProperty("ListName", "EntityRegistry")]
        public string name { get; set; } = string.Empty;

        [A_XSDElementProperty("EntityType", "EntityRegistry")]
        public AnyXMLType entryType { get; set; }
    }

    [A_XSDType("EntityRegistries", "EntityRegistry")]
    public class EntityRegistry : IXMLParser<EntityRegistry>
    {
        public static EntityRegistry manager;

        [A_XSDElementProperty("List", "EntityRegistry")]
        public static List<EntityRegistryEntry> entries { get; set; }
        private static Dictionary<string, EntityGroup> _groups = new Dictionary<string, EntityGroup>();
        
        public EntityRegistry()
        {
            if (manager == null)
            {
                manager = this;
            }
            else
            {
                throw new Exception("EntityManager already exists!");
            }
        }

        public static void Register(object item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));

            Type t = item.GetType();
            foreach (var kvp in _groups)
            {
                if (kvp.Value.elementType.IsAssignableFrom(t))
                    kvp.Value.Add(item);
            }
        }

        public static void Unregister(object item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));

            Type t = item.GetType();
            foreach (var kvp in _groups)
            {
                if (kvp.Value.elementType.IsAssignableFrom(t))
                    kvp.Value.Remove(item);
            }
        }

        // Deferred lifecycle queues, all drained by popping from Engine.Interpolate: the entity
        // constructor enqueues a start, Destroy() pushes the subtree, IsEnabled queues a transition.
        // Popping is what lets a callback create or destroy an entity — the new work lands in the
        // same drain instead of mutating a list someone is enumerating.
        private static readonly Queue<Entity> _toStart = new Queue<Entity>();
        private static readonly Stack<Entity> _toDestroy = new Stack<Entity>();
        private static readonly Queue<Entity> _enableChanges = new Queue<Entity>();

        public static void EnqueueStart(Entity entity) => _toStart.Enqueue(entity);
        public static void EnqueueDestroy(Entity entity) => _toDestroy.Push(entity);
        public static void EnqueueEnableChange(Entity entity) => _enableChanges.Enqueue(entity);

        // Creation order, so a parent starts before the children it built.
        public static void ProcessStarts()
        {
            while (_toStart.Count > 0)
                _toStart.Dequeue().BeginLife();
        }

        // Unregister + free every queued entity, leaves first. Group removal fires the groups'
        // onChanged (e.g. "Controls" -> the UI module marks itself dirty). Pool.Free is deferred
        // inside the pool, so the freed slots are actually compacted at the next FrameEdge().
        public static void ProcessDestroys()
        {
            while (_toDestroy.Count > 0)
            {
                Entity entity = _toDestroy.Pop();
                Unregister(entity);
                entity.OnDestroy();
                entity.Pool.Free(entity.dataHandle);
            }
        }

        // Fires the OnEnable/OnDisable pair away from the code that flipped the flag.
        public static void ProcessEnableChanges()
        {
            while (_enableChanges.Count > 0)
                _enableChanges.Dequeue().ApplyEnableChange();
        }

        public static void AddToGroup(string groupName, object item)
        {
            if (_groups.TryGetValue(groupName, out var group))
                group.Add(item);
        }

        public static void RemoveFromGroup(string groupName, object item)
        {
            if (_groups.TryGetValue(groupName, out var group))
                group.Remove(item);
        }

        public static EntityGroup GetGroup(string name)
        {
            _groups.TryGetValue(name, out var group);
            return group;
        }

        public static EntityRegistry ParseXML(string xmlName)
        {
            string path = Paths.Doc(xmlName);
            EntityRegistry registry = new EntityRegistry();
            XElement root = XElement.Load(path);
            XNamespace ns = root.GetDefaultNamespace();

            foreach (XElement listElem in root.Elements(ns + "List"))
            {
                string listName = listElem.Attribute("ListName").Value;
                string typeStr = listElem.Attribute("EntityType").Value;

                Type entType;
                if (AnyXMLType.typeMap.ContainsKey(typeStr))
                    entType = AnyXMLType.typeMap[typeStr];
                else
                    entType = AnyXMLType.FindType(typeStr);

                if (!_groups.ContainsKey(listName))
                    _groups.Add(listName, new EntityGroup(listName, entType));
            }

            return registry;
        }

        [A_XSDActionDependency("EntityRegistry.ParseXML", "Bootstrap")]
        public static void PrepareRegistry()
        {
            ParseXML("EntityRegistry.xml");
        }
    }
}
