using ArctisAurora.Core.Registry;

namespace ArctisAurora.Core.Threading
{
    [A_XSDType("Step", "Systems")]
    public class FrameStepDefinition
    {
        [A_XSDElementProperty("Action", "Systems")]
        public string action { get; set; } = string.Empty;

        [A_XSDElementProperty("Edge", "Systems")]
        public string edge { get; set; } = string.Empty;

        [A_XSDElementProperty("Pinned", "Systems")]
        public bool pinned { get; set; }

        [A_XSDElementProperty("Reads", "Systems")]
        public string reads { get; set; } = string.Empty;

        [A_XSDElementProperty("Writes", "Systems")]
        public string writes { get; set; } = string.Empty;
    }

    [A_XSDType("Dedicated", "Systems")]
    public class DedicatedDefinition
    {
        [A_XSDElementProperty("System", "Systems")]
        public string system { get; set; } = string.Empty;
    }

    [A_XSDType("FrameGraph", "Systems")]
    public class FrameGraphDefinition
    {
        [A_XSDElementProperty("Dedicated", "Systems")]
        public List<DedicatedDefinition> dedicated { get; set; } = new();

        [A_XSDElementProperty("Step", "Systems")]
        public List<FrameStepDefinition> steps { get; set; } = new();
    }
}
