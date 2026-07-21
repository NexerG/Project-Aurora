using ArctisAurora.Core.Registry;

namespace ArctisAurora.Core.Data
{
    // Schema-carrier types: they exist so XSDGenerator emits PoolsTypeSchema.xsd and
    // Pools.xml is authorable/validated. Parsing is manual in DataManager.
    // PoolGrowthType lives in its own category, NOT "Pools": an enum in the "Pools" category
    // would emit a category simple type named "Pools" that collides with the "Pools" manifest
    // complex type. This one enum is used both as the schema type and at runtime by DataPool.
    [A_XSDType("PoolGrowthType", "DataPools")]
    public enum PoolGrowthType { Multiplicative, Additive }


    [A_XSDType("Component", "DataPools")]
    public class PoolComponent
    {
        [A_XSDElementProperty("Type", "DataPools")]
        public AnyXMLType type { get; set; }
    }

    [A_XSDType("Pool", "DataPools")]
    public class PoolDefinition
    {
        [A_XSDElementProperty("Name", "DataPools")]
        public string name { get; set; } = string.Empty;
        [A_XSDElementProperty("Capacity", "DataPools")]
        public int capacity { get; set; }
        [A_XSDElementProperty("Ordered", "DataPools")]
        public bool ordered { get; set; }
        [A_XSDElementProperty("SortAction", "DataPools")]
        public string sortAction { get; set; } = string.Empty;
        [A_XSDElementProperty("Growth", "DataPools")]
        public PoolGrowthType growth { get; set; }
        [A_XSDElementProperty("GrowthValue", "DataPools")]
        public int growthValue { get; set; }
        [A_XSDElementProperty("Component", "DataPools")]
        public List<PoolComponent> components { get; set; } = new();
    }

    [A_XSDType("EnginePools", "DataPools")]
    public class PoolManifest
    {
        [A_XSDElementProperty("Pool", "DataPools")]
        public List<PoolDefinition> pools { get; set; } = new();
    }
}
