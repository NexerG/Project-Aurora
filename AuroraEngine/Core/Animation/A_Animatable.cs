using ArctisAurora.Core.Registry;
using ArctisAurora.Core.UI;
using System.Linq.Expressions;
using System.Numerics;
using System.Reflection;

namespace ArctisAurora.Core.Animation
{
    // Marks a property an animation may drive.
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class A_Animatable : Attribute { }

    // A compiled getter and setter pair for one animatable property, carried as a Vector4.
    internal sealed class AnimatableProperty
    {
        private static readonly Dictionary<(Type, string), AnimatableProperty> cache = new();

        public readonly Func<object, Vector4> get;
        public readonly Action<object, Vector4> set;

        private AnimatableProperty(Func<object, Vector4> get, Action<object, Vector4> set)
        {
            this.get = get;
            this.set = set;
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

            AnimatableProperty built = new AnimatableProperty(
                Expression.Lambda<Func<object, Vector4>>(ToVector(member), target).Compile(),
                Expression.Lambda<Action<object, Vector4>>(Expression.Assign(member, FromVector(value, property.PropertyType)), target, value).Compile());
            cache[(type, name)] = built;
            return built;
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
