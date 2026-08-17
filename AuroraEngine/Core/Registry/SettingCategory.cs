using ArctisAurora.Core.Filing.Serialization;
using System.Reflection;

namespace ArctisAurora.Core.Registry
{
    [A_XSDType("SettingScope", "Settings")]
    public enum SettingScope
    {
        App, User
    }

    // One setting is one type, so it is one element carrying its own typed attributes. Scope and
    // OnChanged are the setting's own, not part of its value.
    [A_XSDType("Setting", "Settings", IsAbstract = true)]
    public abstract class Setting
    {
        [A_XSDElementProperty("Scope", "Settings")]
        public SettingScope scope { get; set; } = SettingScope.User;

        [A_XSDElementProperty("OnChanged", "Settings")]
        public string onChanged { get; set; } = "";

        // the values the last Apply handed to OnChanged
        internal Dictionary<MemberInfo, object> applied = new Dictionary<MemberInfo, object>();

        // Everything the setting means, which is every scalar it declares itself.
        internal static IEnumerable<MemberInfo> ValueMembers(Type type) =>
            XmlReflection.ScalarMembers(type).Where(m => m.DeclaringType != typeof(Setting));
    }

    // A settings group whose members are Settings, one element each. Declaring one is a field on
    // the category and nothing else — the children are the category's own Setting-typed fields.
    [A_XSDType("SettingCategory", "Settings", AllowedChildren = typeof(Setting), IsAbstract = true)]
    public abstract class SettingCategory : ISettingsGroup
    {
        private List<Setting> declared;

        // Collected on first use, so the derived class's field initializers have already run.
        public List<Setting> settings
        {
            get
            {
                if (declared != null) return declared;

                declared = new List<Setting>();
                foreach (FieldInfo member in GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                    if (member.GetValue(this) is Setting setting)
                        declared.Add(setting);
                return declared;
            }
        }

        public Setting Find(string name)
        {
            foreach (Setting setting in settings)
                if (string.Equals(NameOf(setting), name, StringComparison.OrdinalIgnoreCase))
                    return setting;
            return null;
        }

        public static string NameOf(Setting setting) =>
            setting.GetType().GetCustomAttribute<A_XSDTypeAttribute>(false)?.Name
            ?? throw new Exception($"Setting {setting.GetType().Name} is missing [A_XSDType].");
    }
}
