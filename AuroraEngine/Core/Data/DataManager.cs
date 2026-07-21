using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Filing.Serialization;
using System.Reflection;
using System.Xml.Linq;

namespace ArctisAurora.Core.Data
{
    public readonly record struct DataHandle(ushort PoolId, int StableId, int Version);

    // Static owner of all data pools. Pools are declared in Pools.xml and composed from
    // C#-defined component structs at bootstrap. Lookup by name or by id; FrameEdge drains
    // structural changes across every pool between frames.
    public static class DataManager
    {
        private static readonly List<DataPool> _pools = new();
        private static readonly Dictionary<string, DataPool> _byName = new();

        public static IReadOnlyList<DataPool> Pools => _pools;

        public static DataPool Get(string name) => _byName[name];
        public static DataPool Get(ushort id) => _pools[id];
        public static bool TryGet(string name, out DataPool pool) => _byName.TryGetValue(name, out pool);

        [A_XSDActionDependency("DataManager.ParseXML", "Bootstrap")]
        public static void ParseXML()
        {
            LoadManifest("Pools.xml");
        }

        private static void LoadManifest(string xmlName)
        {
            string path = Paths.Doc(xmlName);
            XElement root = XElement.Load(path);
            XNamespace ns = root.GetDefaultNamespace();

            foreach (XElement poolElem in root.Elements(ns + "Pool"))
            {
                string name = poolElem.Attribute("Name").Value;
                int capacity = int.Parse(poolElem.Attribute("Capacity").Value);
                bool ordered = bool.Parse(poolElem.Attribute("Ordered")?.Value ?? "false");
                string sortAction = poolElem.Attribute("SortAction")?.Value;
                PoolGrowth growth = Enum.Parse<PoolGrowth>(poolElem.Attribute("Growth")?.Value ?? "Multiplicative");
                int growthValue = int.Parse(poolElem.Attribute("GrowthValue")?.Value ?? "2");

                List<Type> componentTypes = new();
                foreach (XElement compElem in poolElem.Elements(ns + "Component"))
                {
                    string typeName = compElem.Attribute("Type").Value;
                    Type t = AnyXMLType.typeMap.TryGetValue(typeName, out Type mapped)
                        ? mapped
                        : AnyXMLType.FindType(typeName);
                    if (t == null)
                        throw new Exception($"[DataManager] Pool '{name}' component type '{typeName}' not found.");
                    componentTypes.Add(t);
                }

                DataPool pool = new((ushort)_pools.Count, name, capacity, ordered, growth, growthValue, componentTypes);
                if (!string.IsNullOrEmpty(sortAction))
                    pool.SortProvider = ResolveSortProvider(sortAction);

                Register(pool);
            }
        }

        public static void Register(DataPool pool)
        {
            _pools.Add(pool);
            _byName[pool.Name] = pool;
        }

        public static void FrameEdge()
        {
            for (int i = 0; i < _pools.Count; i++)
                _pools[i].FrameEdge();
        }

        // Binds a "PoolSort" action name to a static IReadOnlyList<int> Method(DataPool).
        // Returns null if none is registered yet (e.g. before the UI sort provider exists).
        private static Func<DataPool, IReadOnlyList<int>> ResolveSortProvider(string actionName)
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (Type type in asm.GetTypes())
                {
                    foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                    {
                        A_XSDActionDependencyAttribute attr = method.GetCustomAttribute<A_XSDActionDependencyAttribute>();
                        if (attr == null || attr.Category != "PoolSort" || attr.Name != actionName)
                            continue;
                        try
                        {
                            return (Func<DataPool, IReadOnlyList<int>>)Delegate.CreateDelegate(
                                typeof(Func<DataPool, IReadOnlyList<int>>), method);
                        }
                        catch
                        {
                            Console.WriteLine($"[DataManager] Sort action '{actionName}' has an incompatible signature.");
                            return null;
                        }
                    }
                }
            }
            Console.WriteLine($"[DataManager] Sort action '{actionName}' not found — pool will keep insertion order.");
            return null;
        }
    }
}
