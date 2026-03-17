using ArctisAurora.EngineWork;
using ArctisAurora.EngineWork.AssetRegistry;
using ArctisAurora.EngineWork.Rendering.UI.Controls;
using System.Reflection;
using Windows.ApplicationModel.VoiceCommands;

namespace ArctisAurora.Core.AssetRegistry
{
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = true)]
    public sealed class A_ActiveContextAttribute : Attribute
    {
        public string name;
        public A_ActiveContextAttribute(string name)
        {
            this.name = name;
        }
    }
    public record ContextEntry(Type valueType, Func<object?> Get, Action<object?> set);

    [A_XSDType("ActiveContext", "Context")]
    public sealed class Context : IBootstrap
    {

        public static readonly Dictionary<string, object> activeContexts =
            AssetRegistries.GetRegistryByName<string, object>("ActiveContexts");

        public static void Register(string name, Type valueType, Func<object?> get, Action<object?> set) =>
            activeContexts[name] = new ContextEntry(valueType, get, set);

        public static T? Get<T>(string name) where T : class =>
            activeContexts.TryGetValue(name, out var entry) ? (entry as ContextEntry)?.Get() as T : null;

        public static void Set(string name, object? value)
        {
            if (activeContexts.TryGetValue(name, out var entry))
            {
                (entry as ContextEntry).set(value);
            }
        }

        public static void Clear(string name) => Set(name, null);

        [A_BootstrapStage(BootstrapStage.PostGPUAPI)]
        public static void Bootstrap(BootstrapStage? stage)
        {
            var generalAsm = AppDomain.CurrentDomain.GetAssemblies();
            var members = generalAsm.SelectMany(a => a.GetTypes())
                .SelectMany(t => t.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                    .Where(m => m.IsDefined(typeof(A_ActiveContextAttribute), false))
                    .Select(m => (Member: m, Attr: m.GetCustomAttribute<A_ActiveContextAttribute>()!)));

            foreach (var (member, attr) in members)
            {
                if (member is PropertyInfo prop)
                {
                    Register(
                        attr.name,
                        prop.PropertyType,
                        () => prop.GetValue(null),
                        v => prop.SetValue(null, v)
                    );
                }
                else if (member is FieldInfo field)
                {
                    Register(
                        attr.name,
                        field.FieldType,
                        () => field.GetValue(null),
                        v => field.SetValue(null, v)
                    );
                }
            }
        }

        public object GetContextByName(string name)
        {
            activeContexts.TryGetValue(name, out var context);
            return context;
        }

        /*public T GetContextOfType(Type t)
        {
            activeContexts.TryGetValue(t, out object value);
            return (T)value;
        }*/
    }
}