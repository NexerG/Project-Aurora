using ArctisAurora.Core.Filing.Serialization;
using ArctisAurora.Core.UISystem.Controls;
using System.Collections;
using System.Xml.Linq;

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
        
        public static VulkanControl uiTree
        {
            get => field;
            set => field = value;
        }

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
            string path = Paths.XMLDOCUMENTS + "\\" + xmlName;
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
