using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace ArctisAurora.EngineWork.Serialization
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public class Serializable : Attribute
    {
        public uint? ID { get; set; }

        public Serializable()
        {
            // This attribute can be used to mark types that should be serialized.
        }

        public static uint GenerateID(string name)
        {
            using var md5 = MD5.Create();
            byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(name));
            return BitConverter.ToUInt32(hash, 0);
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