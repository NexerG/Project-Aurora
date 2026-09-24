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
    // Marks a property an animation may drive.
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class A_Animatable : Attribute
    {
        public readonly Type? column;
        public readonly string? field;
        public readonly string? changed;

        public A_Animatable() { }

        // A property stored in field of a pool column: written in place, then changed runs on Main.
        public A_Animatable(Type column, string field, string changed)
        {
            this.column = column;
            this.field = field;
            this.changed = changed;
        }
    }

    // A compiled getter and setter pair for one animatable property, carried as a Vector4.
    internal sealed class AnimatableProperty
    {
        private static readonly Dictionary<(Type, string), AnimatableProperty> cache = new();

        // C# property name, the same whichever name resolved it
        public readonly string name;
        public readonly Func<object, Vector4> get;
        public readonly Action<object, Vector4> set;

        // pool field written in place, width 0 for a setter-only property
        public readonly Type? column;
        public readonly ushort offset;
        public readonly byte width;
        public readonly Action<object>? changed;

        private AnimatableProperty(string name, Func<object, Vector4> get, Action<object, Vector4> set, Type? column, ushort offset, byte width, Action<object>? changed)
        {
            this.name = name;
            this.get = get;
            this.set = set;
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
            ParameterExpression value = Expression.Parameter(typeof(Vector4));
            MemberExpression member = Expression.Property(Expression.Convert(target, property.DeclaringType!), property);

            A_Animatable attribute = property.GetCustomAttribute<A_Animatable>()!;
            ushort offset = 0;
            byte width = 0;
            Action<object>? changed = null;
            if (attribute.column != null)
                (offset, width, changed) = InPlace(type, property, attribute);

            AnimatableProperty built = new AnimatableProperty(property.Name,
                Expression.Lambda<Func<object, Vector4>>(ToVector(member), target).Compile(),
                Expression.Lambda<Action<object, Vector4>>(Expression.Assign(member, FromVector(value, property.PropertyType)), target, value).Compile(),
                attribute.column, offset, width, changed);
            cache[(type, name)] = built;
            return built;
        }

        // Where a pool-stored property sits in its column row, and the call that follows a write.
        private static (ushort, byte, Action<object>) InPlace(Type type, PropertyInfo property, A_Animatable attribute)
        {
            Type column = attribute.column!;
            FieldInfo field = column.GetField(attribute.field!) ?? throw new Exception($"{column.Name} has no field '{attribute.field}'.");
            if (!typeof(Entity).IsAssignableFrom(type))
                throw new Exception($"{type.Name}.{property.Name} is stored in {column.Name}, but {type.Name} is not an Entity with a pool row.");
            if (field.FieldType != property.PropertyType)
                throw new Exception($"{type.Name}.{property.Name} is {property.PropertyType.Name} but {column.Name}.{field.Name} is {field.FieldType.Name}.");

            int managedSize = (int)typeof(Unsafe).GetMethod(nameof(Unsafe.SizeOf))!.MakeGenericMethod(column).Invoke(null, null)!;
            if (Marshal.SizeOf(column) != managedSize)
                throw new Exception($"{column.Name} marshals to a different size than it has in memory, so its field offsets cannot be trusted.");

            Type owner = property.DeclaringType!;
            MethodInfo method = owner.GetMethod(attribute.changed!, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, Type.EmptyTypes)
                ?? throw new Exception($"{owner.Name} has no parameterless method '{attribute.changed}'.");
            ParameterExpression target = Expression.Parameter(typeof(object));
            Action<object> changed = Expression.Lambda<Action<object>>(Expression.Call(Expression.Convert(target, owner), method), target).Compile();

            return ((ushort)Marshal.OffsetOf(column, field.Name), (byte)(Marshal.SizeOf(field.FieldType) / sizeof(float)), changed);
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

        private static Expression FromVector(Expression v, Type type)
        {
            MemberExpression x = Expression.Field(v, "X"), y = Expression.Field(v, "Y"), z = Expression.Field(v, "Z"), w = Expression.Field(v, "W");

            if (type == typeof(float)) return x;
            if (type == typeof(Vector2)) return Expression.New(typeof(Vector2).GetConstructor(new[] { typeof(float), typeof(float) })!, x, y);
            if (type == typeof(Vector4)) return v;
            if (type == typeof(Thickness))
                return Expression.New(typeof(Thickness).GetConstructor(new[] { typeof(float), typeof(float), typeof(float), typeof(float) })!, x, y, z, w);
            throw new Exception($"{type.Name} cannot be animated.");
        }
    }
}
