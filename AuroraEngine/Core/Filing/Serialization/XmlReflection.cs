using ArctisAurora.Core.Registry;
using ArctisAurora.Core.UISystem.Controls;
using System.ComponentModel;
using System.Reflection;
using System.Xml.Linq;

namespace ArctisAurora.Core.Filing.Serialization
{
    // Attribute-driven XML reflection, shared by every reader that maps engine XML onto types:
    //   - XML attribute  <-> scalar [A_XSDElementProperty] member
    //   - nested element <-> complex [A_XSDType] member, or an entry in a List<> child field
    public static class XmlReflection
    {
        public static void ApplyAttributes(XElement element, object node)
        {
            foreach (MemberInfo member in ScalarMembers(node.GetType()))
            {
                A_XSDElementPropertyAttribute meta = member.GetCustomAttribute<A_XSDElementPropertyAttribute>();
                XAttribute attribute = element.Attributes().FirstOrDefault(
                    a => string.Equals(a.Name.LocalName, meta.Name, StringComparison.OrdinalIgnoreCase));
                if (attribute == null) continue;

                Type memberType = MemberType(member);
                object value = TypeDescriptor.GetConverter(memberType).ConvertFromInvariantString(attribute.Value);
                SetMember(member, node, value);
            }
        }

        private static IEnumerable<MemberInfo> AnnotatedMembers(Type type) =>
            type.GetMembers(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.GetCustomAttribute<A_XSDElementPropertyAttribute>() != null);

        // The same three-way split XSDGenerator makes. A collection is a repeated child element and a
        // complex [A_XSDType] is a single one; only what is left can be an XML attribute. Annotating
        // a List member used to land it here too, and it was written out as its own type name.
        private static bool IsComplexMember(Type type) =>
            !type.IsEnum && type.GetCustomAttribute<A_XSDTypeAttribute>(false) != null;

        private static bool IsListMember(Type type) =>
            type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>);

        public static IEnumerable<MemberInfo> ScalarMembers(Type type) =>
            AnnotatedMembers(type).Where(m => !IsComplexMember(MemberType(m)) && !IsListMember(MemberType(m))
                                              && !IsControlChrome(m));

        // Members declared on VulkanControl or above; a note stores content, not control chrome.
        private static bool IsControlChrome(MemberInfo member) =>
            member.DeclaringType.IsAssignableFrom(typeof(VulkanControl));

        public static IEnumerable<MemberInfo> ComplexMembers(Type type) =>
            AnnotatedMembers(type).Where(m => IsComplexMember(MemberType(m)));

        public static IEnumerable<FieldInfo> ChildListFields(Type type) =>
            type.GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Where(f => f.FieldType.IsGenericType
                            && f.FieldType.GetGenericTypeDefinition() == typeof(List<>));

        public static Type MemberType(MemberInfo m) =>
            m is PropertyInfo p ? p.PropertyType : ((FieldInfo)m).FieldType;

        public static void SetMember(MemberInfo m, object target, object value)
        {
            if (m is PropertyInfo p) p.SetValue(target, value);
            else ((FieldInfo)m).SetValue(target, value);
        }

        public static object GetMember(MemberInfo m, object target) =>
            m is PropertyInfo p ? p.GetValue(target) : ((FieldInfo)m).GetValue(target);
    }
}
