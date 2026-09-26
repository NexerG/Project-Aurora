using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.UI;
using System.Linq.Expressions;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ArctisAurora.Core.Animation
{
    // What layout re-runs after an animated write.
    public enum LayoutChange : byte
    {
        None, Measure, Arrange
    }

    // Marks a property an animation may drive.
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class A_Animatable : Attribute
    {
        public readonly Type column;
        public readonly string field;
        public readonly LayoutChange changed;

        // A property stored in field of a pool column: written in place, then changed marks layout dirty.
        public A_Animatable(Type column, string field, LayoutChange changed = LayoutChange.None)
        {
            this.column = column;
            this.field = field;
            this.changed = changed;
        }
    }

    // A compiled getter for one animatable property, carried as a Vector4.
    internal sealed class AnimatableProperty
    {
        private static readonly Dictionary<(Type, string), AnimatableProperty> cache = new();

        // C# property name, the same whichever name resolved it
        public readonly string name;
        public readonly Func<object, Vector4> get;

        // pool field written in place
        public readonly Type column;
        public readonly ushort offset;
        public readonly byte width;
        public readonly LayoutChange changed;

        private AnimatableProperty(string name, Func<object, Vector4> get, Type column, ushort offset, byte width, LayoutChange changed)
        {
            this.name = name;
            this.get = get;
            this.column = column;
            this.offset = offset;
            this.width = width;
            this.changed = changed;
        }

        // The [A_Animatable] property whose XML name is name, else whose C# name is.
        public static AnimatableProperty Of(Type type, string name)
        {
            if (cache.TryGetValue((type, name), out AnimatableProperty? found)) return found;

            PropertyInfo[] animatable = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetCustomAttribute<A_Animatable>() != null).ToArray();
            PropertyInfo property = animatable.FirstOrDefault(p => p.GetCustomAttribute<A_XSDElementPropertyAttribute>()?.Name == name)
                ?? animatable.FirstOrDefault(p => p.Name == name)
                ?? throw new Exception($"{type.Name} has no animatable property '{name}'.");

            ParameterExpression target = Expression.Parameter(typeof(object));
            MemberExpression member = Expression.Property(Expression.Convert(target, property.DeclaringType!), property);

            A_Animatable attribute = property.GetCustomAttribute<A_Animatable>()!;
            (ushort offset, byte width) = InPlace(type, property, attribute);

            AnimatableProperty built = new AnimatableProperty(property.Name,
                Expression.Lambda<Func<object, Vector4>>(ToVector(member), target).Compile(),
                attribute.column, offset, width, attribute.changed);
            cache[(type, name)] = built;
            return built;
        }

        // Where a pool-stored property sits in its column row.
        private static (ushort, byte) InPlace(Type type, PropertyInfo property, A_Animatable attribute)
        {
            Type column = attribute.column;
            FieldInfo field = column.GetField(attribute.field) ?? throw new Exception($"{column.Name} has no field '{attribute.field}'.");
            if (!typeof(Entity).IsAssignableFrom(type))
                throw new Exception($"{type.Name}.{property.Name} is stored in {column.Name}, but {type.Name} is not an Entity with a pool row.");
            if (field.FieldType != property.PropertyType)
                throw new Exception($"{type.Name}.{property.Name} is {property.PropertyType.Name} but {column.Name}.{field.Name} is {field.FieldType.Name}.");

            int managedSize = (int)typeof(Unsafe).GetMethod(nameof(Unsafe.SizeOf))!.MakeGenericMethod(column).Invoke(null, null)!;
            if (Marshal.SizeOf(column) != managedSize)
                throw new Exception($"{column.Name} marshals to a different size than it has in memory, so its field offsets cannot be trusted.");

            ushort offset = (ushort)Marshal.OffsetOf(column, field.Name);
            byte width = (byte)(Marshal.SizeOf(field.FieldType) / sizeof(float));
            return (offset, width);
        }

        private static Expression ToVector(Expression e)
        {
            ConstantExpression zero = Expression.Constant(0f);
            ConstructorInfo four = typeof(Vector4).GetConstructor(new[] { typeof(float), typeof(float), typeof(float), typeof(float) })!;

            if (e.Type == typeof(float)) return Expression.New(four, e, zero, zero, zero);
            if (e.Type == typeof(Vector2))
                return Expression.New(typeof(Vector4).GetConstructor(new[] { typeof(Vector2), typeof(float), typeof(float) })!, e, zero, zero);
            if (e.Type == typeof(Vector4)) return e;
            if (e.Type == typeof(Thickness))
                return Expression.New(four, Expression.Field(e, "top"), Expression.Field(e, "right"), Expression.Field(e, "bottom"), Expression.Field(e, "left"));
            throw new Exception($"{e.Type.Name} cannot be animated.");
        }
    }
}
