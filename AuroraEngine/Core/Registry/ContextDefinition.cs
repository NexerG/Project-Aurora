namespace ArctisAurora.Core.Registry
{
    [A_XSDType("Context", "Context")]
    public class ContextDefinition
    {
        [A_XSDElementProperty("Name", "Context", "Name this context is registered and looked up under.")]
        public string name = "";

        [A_XSDElementProperty("Type", "Context", "XSD type name a value held by this context must be assignable to.")]
        public string type = "";

        [A_XSDElementProperty("From", "Context", "Context this one is derived from, as the nearest ancestor of that value.")]
        public string from = "";
    }

    [A_XSDType("Contexts", "Context", typeof(ContextDefinition), Description = "Root container for declared active contexts")]
    public class ContextMap { }
}
