namespace ArctisAurora.EngineWork.Serialization
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    public class Serializable : Attribute
    {
        public Serializable()
        {
            // This attribute can be used to mark types that should be serialized.
        }
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    public class NonSerializable : Attribute
    {
        public NonSerializable()
        {
            // This attribute can be used to mark types that should NOT be serialized.
        }
    }

}
