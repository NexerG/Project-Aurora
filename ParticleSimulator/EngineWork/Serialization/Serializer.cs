using System.Reflection;

namespace ArctisAurora.EngineWork.Serialization
{
    internal class Serializer
    {
        public static void Serialize<T>(T obj, string path)
        {
            bool isSerializable = typeof(T).IsDefined(typeof(Serializable), false);
            if (!isSerializable)
            {
                throw new InvalidOperationException($"Type {typeof(T)} is not marked as serializable.");
            }

            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
            }

            using FileStream stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite);
            using BinaryWriter writer = new BinaryWriter(stream);

            Type type = obj.GetType();
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            foreach (var field in fields)
            {
                Type fieldType = field.FieldType;
                object value = field.GetValue(obj);

                if (fieldType.IsPrimitive || fieldType == typeof(string))
                {
                    Console.WriteLine($"Primitive: {field.Name} = {value}");
                }
                else if (fieldType.IsValueType && !fieldType.IsPrimitive)
                {
                    Console.WriteLine($"Struct: {field.Name}");
                    RecursiveSerialize(value, writer); // Recurse into struct
                }
                else if (fieldType.IsArray)
                {
                    Array array = (Array)value;
                    Type elementType = fieldType.GetElementType();

                    Console.WriteLine($"Array: {field.Name} of {elementType}");

                    foreach (var element in array)
                    {
                        if (element != null)
                            RecursiveSerialize(element, writer); // recurse into each array element
                    }
                }
                else if (!fieldType.IsPrimitive && fieldType != typeof(string))
                {
                    Console.WriteLine($"Reference Type: {field.Name}");
                    if (value != null)
                        RecursiveSerialize(value, writer);
                }
            }

            /*var formatter = new DataContractSerializer(typeof(T));
            using (var stream = new FileStream(path, FileMode.Create))
            {
                formatter.WriteObject(stream, obj);
            }*/
        }

        private static void RecursiveSerialize(object obj, BinaryWriter writer)
        {
            if (obj == null) return;

            Type type = obj.GetType();
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            foreach (var field in fields)
            {
                Type fieldType = field.FieldType;
                object value = field.GetValue(obj);
                if (fieldType.IsPrimitive || fieldType == typeof(string))
                {
                    //writer.Write(value.ToString());
                    Console.WriteLine($"Primitive: {field.Name} = {value}");
                }
                else if (fieldType.IsValueType && !fieldType.IsPrimitive)
                {
                    Console.WriteLine($"Struct: {field.Name}");
                    RecursiveSerialize(value, writer); // Recurse into struct
                }
                else
                {
                    Console.WriteLine($"Reference Type: {field.Name}");
                    if (value != null)
                        RecursiveSerialize(value, writer); // Recurse into reference type
                }
            }
        }
    }
}
