using ArctisAurora.EngineWork.Renderer.UI;
using System.Reflection;
using System.Runtime.InteropServices;
using static ArctisAurora.EngineWork.Renderer.UI.AuroraFont;

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

            RecursiveSerialize(obj, writer);
            writer.Flush();
            writer.Close();
        }

        private static void RecursiveSerialize<T>(T obj, BinaryWriter writer)
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
                    writer.Write(ConvertToBytes(value));
                    //Write(value, writer);
                }
                else if (fieldType.IsValueType && !fieldType.IsPrimitive)
                {
                    RecursiveSerialize(value, writer); // Recurse into struct
                }
                else if (fieldType.IsArray)
                {
                    Array array = (Array)value;
                    Type elementType = fieldType.GetElementType();

                    foreach (var element in array)
                    {
                        if (element != null)
                            RecursiveSerialize(element, writer); // recurse into each array element
                    }
                }
                else if (!fieldType.IsPrimitive && fieldType != typeof(string))
                {
                    if (value != null)
                        RecursiveSerialize(value, writer);
                }
            }
        }

        private static byte[] ConvertToBytes(Object value)
        {
            Type type = value.GetType();
            if (value is null)
            {
                return ReturnDefault(type);
            }

            switch (Type.GetTypeCode(type))
            {
                case TypeCode.Boolean:
                    return BitConverter.GetBytes((bool)value);
                case TypeCode.Byte:
                    return new[] { (byte)value };
                case TypeCode.Char:
                    return BitConverter.GetBytes((char)value);
                case TypeCode.Double:
                    return BitConverter.GetBytes((double)value);
                case TypeCode.Int16:
                    return BitConverter.GetBytes((short)value);
                case TypeCode.Int32:
                    return BitConverter.GetBytes((int)value);
                case TypeCode.Int64:
                    return BitConverter.GetBytes((long)value);
                case TypeCode.SByte:
                    return new[] { unchecked((byte)(sbyte)value) };
                case TypeCode.Single:
                    return BitConverter.GetBytes((float)value);
                case TypeCode.UInt16:
                    return BitConverter.GetBytes((ushort)value);
                case TypeCode.UInt32:
                    return BitConverter.GetBytes((uint)value);
                case TypeCode.UInt64:
                    return BitConverter.GetBytes((ulong)value);
                case TypeCode.String:
                    return System.Text.Encoding.UTF8.GetBytes((string)value ?? string.Empty);
                case TypeCode.Decimal:
                    // Decimal.GetBits returns four Int32 values
                    int[] bits = decimal.GetBits((decimal)value);
                    byte[] bytes = new byte[bits.Length * sizeof(int)];
                    for (int i = 0; i < bits.Length; i++)
                    {
                        Array.Copy(BitConverter.GetBytes(bits[i]), 0, bytes, i * sizeof(int), sizeof(int));
                    }
                    return bytes;
                default:
                    throw new NotSupportedException($"Type {type} is not a supported primitive type.");
            }
        }

        private static byte[] ReturnDefault(Type type)
        {
            switch(Type.GetTypeCode(type))
            {
                case TypeCode.Boolean:
                    return BitConverter.GetBytes(default(bool));
                case TypeCode.Byte:
                    return new byte[] { 0 };
                case TypeCode.Char:
                    return BitConverter.GetBytes(default(char));
                case TypeCode.Double:
                    return BitConverter.GetBytes(default(double));
                case TypeCode.Int16:
                    return BitConverter.GetBytes((default(Int16)));
                case TypeCode.Int32:
                    return BitConverter.GetBytes(default(Int32));
                case TypeCode.Int64:
                    return BitConverter.GetBytes(default(Int64));
                case TypeCode.SByte:
                    return new byte[] { 0 };
                case TypeCode.Single:
                    return BitConverter.GetBytes(default(Single));
                case TypeCode.UInt16:
                    return BitConverter.GetBytes(default(UInt16));
                case TypeCode.UInt32:
                    return BitConverter.GetBytes(default(UInt32));
                case TypeCode.UInt64:
                    return BitConverter.GetBytes(default(UInt64));
                case TypeCode.String:
                    return System.Text.Encoding.UTF8.GetBytes(string.Empty);
                case TypeCode.Decimal:
                    return new byte[16];
                default:
                    throw new NotSupportedException($"Type {type} is not a supported primitive type.");
            }
        }
    }
}
