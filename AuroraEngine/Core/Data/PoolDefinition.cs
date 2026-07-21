using ArctisAurora.Core.Registry;

namespace ArctisAurora.Core.Data
{
    // Schema-carrier types: they exist so XSDGenerator emits PoolsTypeSchema.xsd and
    // Pools.xml is authorable/validated. Actual parsing is manual in DataManager.

    [A_XSDType("PoolGrowthMode", "Pools")]
    public enum PoolGrowthMode { Multiplicative, Additive }

    [A_XSDType("Component", "Pools")]
    public class PoolComponent
    {
        [A_XSDElementProperty("Type", "Pools")]
        public AnyXMLType type { get; set; }
    }

    [A_XSDType("Pool", "Pools")]
    public class PoolDefinition
    {
        [A_XSDElementProperty("Name", "Pools")]
        public string name { get; set; } = string.Empty;
        [A_XSDElementProperty("Capacity", "Pools")]
        public int capacity { get; set; }
        [A_XSDElementProperty("Ordered", "Pools")]
        public bool ordered { get; set; }
        [A_XSDElementProperty("SortAction", "Pools")]
        public string sortAction { get; set; } = string.Empty;
        [A_XSDElementProperty("Growth", "Pools")]
        public PoolGrowthMode growth { get; set; }
        [A_XSDElementProperty("GrowthValue", "Pools")]
        public int growthValue { get; set; }
        [A_XSDElementProperty("Component", "Pools")]
        public List<PoolComponent> components { get; set; } = new();
    }

    [A_XSDType("Pools", "Pools")]
    public class PoolManifest
    {
        [A_XSDElementProperty("Pool", "Pools")]
        public List<PoolDefinition> pools { get; set; } = new();
    }
}
