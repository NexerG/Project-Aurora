using ArctisAurora.EngineWork.AssetRegistry;
using Microsoft.Extensions.DependencyModel;
using Microsoft.VisualBasic.FileIO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using System.Windows.Forms.VisualStyles;
using Windows.ApplicationModel.Activation;
using Windows.ApplicationModel.VoiceCommands;

namespace ArctisAurora.EngineWork.Serialization
{
    internal interface IDeserialize
    {
        public void Deserialize(string path);
    }

    internal sealed class Serializer
    {
        public static void SerializeAll<T>(T obj, string path)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
            }

            using FileStream stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite);
            using BinaryWriter writer = new BinaryWriter(stream);

            RecursiveSerializeAll(obj, writer);
            writer.Flush();
            writer.Close();
        }

        private static void RecursiveSerializeAll<T>(T obj, BinaryWriter writer)
        {

            Type type = obj.GetType();
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            foreach (var field in fields)
            {
                Type fieldType = field.FieldType;
                //Console.WriteLine(obj.ToString() + "---" +fieldType.ToString());
                object value = field.GetValue(obj);

                if (field.IsDefined(typeof(NonSerializable), false)) // skip non-serializable fields
                {
                    //Console.Write("--- SKIPPED");
                    continue;
                }

                if (fieldType.IsPrimitive || fieldType == typeof(string))
                {
                    writer.Write(ConvertToBytes(value));
                }
                else if (fieldType.IsValueType && !fieldType.IsPrimitive)
                {
                    //if(fieldType.IsDefined(typeof(Serializable), false))
                    RecursiveSerializeAll(value, writer); // Recurse into struct
                }
                else if (fieldType.IsArray)
                {
                    Array array = (Array)value;
                    //Type elementType = fieldType.GetElementType();

                    foreach (var element in array)
                    {
                        if (element != null)
                            RecursiveSerializeAll(element, writer); // recurse into each array element
                    }
                }
                else if (!fieldType.IsPrimitive && fieldType != typeof(string)) //its a class so we do it again
                {
                    if (value != null)
                        RecursiveSerializeAll(value, writer);
                }
            }
        }



        public static void SerializeAttributed<T>(T obj, string path)
        {
            /*bool isSerializable = typeof(T).IsDefined(typeof(Serializable), false);
            if (!isSerializable)
            {
                throw new InvalidOperationException($"Type {typeof(T)} is not marked as serializable.");
            }*/

            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
            }

            using FileStream stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite);
            using BinaryWriter writer = new BinaryWriter(stream);

            RecursiveSerialize(obj, writer);
            writer.Flush();
            writer.Close();
            stream.Close();
        }

        private static void RecursiveSerialize<T>(T obj, BinaryWriter writer)
        {
            //if (obj == null) return;

            Type type = obj.GetType();
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            foreach (var field in fields)
            {
                Type fieldType = field.FieldType;
                object value = field.GetValue(obj);

                if (field.IsDefined(typeof(NonSerializable), false))
                {
                    continue;
                }

                if (fieldType.IsPrimitive)
                {
                    writer.Write(ConvertToBytes(value));
                }
                else if (fieldType == typeof(string))
                {
                    string strValue = value as string ?? string.Empty;
                    byte[] stringBytes = System.Text.Encoding.UTF8.GetBytes(strValue);
                    writer.Write(stringBytes.Length); // write string length
                    writer.Write(stringBytes); // write string bytes
                }
                else if (fieldType.IsValueType && !fieldType.IsPrimitive)
                {
                    //if(fieldType.IsDefined(typeof(Serializable), false))
                    /*var attribute = fieldType.GetCustomAttribute<@Serializable>();
                    if (attribute != null)
                    {
                        attribute.ID = Serializable.GenerateID(field.Name);
                        writer.Write(ConvertToBytes(attribute.ID));
                    }*/
                    RecursiveSerialize(value, writer); // Recurse into struct
                }
                else if (fieldType.IsArray)
                {
                    Array array = (Array)value;
                    int length = array.Length;
                    foreach (var element in array)
                    {
                        if (element == null)
                        {
                            length--;
                        }
                    }
                    writer.Write(length); // write array length

                    foreach (var element in array)
                    {
                        if (element != null)
                        {
                            var attribute = fieldType.GetElementType()!.GetCustomAttribute<@Serializable>();
                            if (attribute != null)
                            {
                                var elementType = element.GetType();
                                attribute.ID = Serializable.GenerateID(elementType.Name);
                                byte[] b = ConvertToBytes(attribute.ID);
                                writer.Write(b);
                            }
                            else
                            {
                                throw new Exception("Array element type is not marked as serializable. Array elements being serialized MUST be marked as [@Serializable]");
                            }
                            RecursiveSerialize(element, writer); // recurse into each array element
                        }
                    }
                }
                else if (!fieldType.IsPrimitive && fieldType != typeof(string))
                {
                    if (value != null && field.IsDefined(typeof(Serializable), false))
                        RecursiveSerialize(value, writer);
                }
            }
        }

        public static void Deserialize<T>(string path, ref T obj)
        {
            FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read);
            RecursiveDeserialize<T>(stream, ref obj);
            stream.Close();
        }

        private static T RecursiveDeserialize<T>(FileStream stream, ref T obj)
        {
            Type t = typeof(T);
            var fields = t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (var field in fields)
            {
                if (field.IsDefined(typeof(NonSerializable), false))
                {
                    continue;
                }

                Type fieldtype = field.FieldType;
                if(fieldtype.IsPrimitive)
                {
                    // Read primitive or string value
                    byte[] buffer = ConvertToBytes(fieldtype);
                    stream.Read(buffer, 0, buffer.Length);
                    object value = Type.GetTypeCode(fieldtype) switch
                    {
                        TypeCode.Boolean => BitConverter.ToBoolean(buffer, 0),
                        TypeCode.Byte => buffer[0],
                        TypeCode.Char => BitConverter.ToChar(buffer, 0),
                        TypeCode.Double => BitConverter.ToDouble(buffer, 0),
                        TypeCode.Int16 => BitConverter.ToInt16(buffer, 0),
                        TypeCode.Int32 => BitConverter.ToInt32(buffer, 0),
                        TypeCode.Int64 => BitConverter.ToInt64(buffer, 0),
                        TypeCode.SByte => (sbyte)buffer[0],
                        TypeCode.Single => BitConverter.ToSingle(buffer, 0),
                        TypeCode.UInt16 => BitConverter.ToUInt16(buffer, 0),
                        TypeCode.UInt32 => BitConverter.ToUInt32(buffer, 0),
                        TypeCode.UInt64 => BitConverter.ToUInt64(buffer, 0),
                        _ => throw new NotSupportedException($"Type {fieldtype} is not a supported primitive type."),
                    };
                    field.SetValueDirect(__makeref(obj), value);
                }
                if(fieldtype == typeof(string))
                {
                    // Read string length and value
                    byte[] lengthBuffer = new byte[sizeof(int)];
                    stream.Read(lengthBuffer, 0, lengthBuffer.Length);
                    int length = BitConverter.ToInt32(lengthBuffer, 0);
                    byte[] stringBuffer = new byte[length];
                    stream.Read(stringBuffer, 0, stringBuffer.Length);
                    string value = System.Text.Encoding.UTF8.GetString(stringBuffer);
                    field.SetValue(obj, value);
                }
                else if (fieldtype.IsValueType && !fieldtype.IsPrimitive)
                {
                    // Recurse into struct
                    object subObj = Activator.CreateInstance(fieldtype);
                    var method = typeof(Serializer)
                                .GetMethod(nameof(RecursiveDeserialize), BindingFlags.NonPublic | BindingFlags.Static)!
                                .MakeGenericMethod(fieldtype);

                    subObj = method.Invoke(null, new object[] { stream, subObj })!;
                    field.SetValue(obj, subObj);
                }
                else if (fieldtype.IsArray)
                {
                    // Read array length and recurse into each element
                    Dictionary<uint, Type> dict = AssetRegistries.GetRegistry<uint, Type>(typeof(Type));

                    byte[] lengthBuffer = new byte[sizeof(int)];
                    stream.Read(lengthBuffer, 0, lengthBuffer.Length);
                    int length = BitConverter.ToInt32(lengthBuffer, 0);
                    Array array = Array.CreateInstance(fieldtype.GetElementType(), length);

                    for (int i = 0; i < length; i++)
                    {
                        byte[] bytes = new byte[sizeof(uint)];
                        stream.Read(bytes, 0, bytes.Length);
                        uint typeID = BitConverter.ToUInt32(bytes, 0);
                        Type elementType = dict[typeID];
                        object element = Activator.CreateInstance(elementType)!;

                        var method = typeof(Serializer)
                            .GetMethod(nameof(RecursiveDeserialize), BindingFlags.NonPublic | BindingFlags.Static)!
                            .MakeGenericMethod(elementType);
                        element = method.Invoke(null, new object[] { stream, element })!;
                        array.SetValue(element, i);
                    }
                    field.SetValue(obj, array);
                }
                else if (!fieldtype.IsPrimitive && fieldtype != typeof(string))
                {
                    // Recurse into class
                    object subObj = Activator.CreateInstance(fieldtype)!;
                    var method = typeof(Serializer)
                        .GetMethod(nameof(RecursiveDeserialize), BindingFlags.NonPublic | BindingFlags.Static)!
                        .MakeGenericMethod(fieldtype);
                    subObj = method.Invoke(null, new object[] { stream, subObj })!;
                    field.SetValue(obj, subObj);
                }
            }
            return obj;
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

        private static byte[] ConvertToBytes(Type type)
        {
            switch (Type.GetTypeCode(type))
            {
                case TypeCode.Boolean:
                    return new byte[sizeof(bool)];
                case TypeCode.Byte:
                    return new byte[sizeof(byte)];
                case TypeCode.Char:
                    return new byte[sizeof(char)];
                case TypeCode.Double:
                    return new byte[sizeof(double)];
                case TypeCode.Int16:
                    return new byte[sizeof(short)];
                case TypeCode.Int32:
                    return new byte[sizeof(int)];
                case TypeCode.Int64:
                    return new byte[sizeof(long)];
                case TypeCode.SByte:
                    return new byte[1];
                case TypeCode.Single:
                    return new byte[sizeof(float)];
                case TypeCode.UInt16:
                    return new byte[sizeof(ushort)];
                case TypeCode.UInt32:
                    return new byte[sizeof(uint)];
                case TypeCode.UInt64:
                    return new byte[sizeof(ulong)];
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
