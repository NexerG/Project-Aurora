using ArctisAurora.Core.Registry;
using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Xml.Linq;

namespace ArctisAurora.Core.UISystem.Controls.Text.Document
{
    // XML load/save for the document model. The on-disk note format is engine XML (not markdown, not
    // JSON). This mirrors the engine's attribute-driven reflection rather than hand-mapping each type:
    //   - element name  -> Type        via AnyXMLType.FindType ([A_XSDType] Name)
    //   - XML attribute  <-> scalar     via [A_XSDElementProperty] members
    //   - nested element <-> child      attached to the parent's matching List<> field by element type
    // so new blocks / inlines / run styles round-trip automatically once they carry the attributes.
    public static class DocumentXml
    {
        public static RichTextDocument Load(string path)
        {
            XDocument xml = XDocument.Load(path);
            return (RichTextDocument)ParseElement(xml.Root);
        }

        public static void Save(RichTextDocument document, string path)
        {
            XElement root = WriteElement(document);
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            new XDocument(new XDeclaration("1.0", "utf-8", null), root).Save(path);
        }

        #region ---- parse ----
        private static object ParseElement(XElement element)
        {
            Type type = AnyXMLType.FindType(element.Name.LocalName)
                ?? throw new Exception($"Unknown document element '{element.Name.LocalName}'.");
            object node = Activator.CreateInstance(type);
            ApplyAttributes(element, node);
            foreach (XElement childElement in element.Elements())
                AttachChild(node, ParseElement(childElement));
            return node;
        }

        private static void ApplyAttributes(XElement element, object node)
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

        private static void AttachChild(object parent, object child)
        {
            // Blocks and runs are now VulkanControls, so they inherit Entity's generic `children`
            // (List<Entity>) / `_components` lists alongside the document model's own typed lists
            // (`blocks`, `inlines`). Pick the most-specific accepting list so a <Run> lands in
            // `inlines` (element type TextRun) rather than the inherited `children` (element type Entity).
            FieldInfo list = ChildListFields(parent.GetType())
                .Where(f => f.FieldType.GetGenericArguments()[0].IsAssignableFrom(child.GetType()))
                .OrderByDescending(f => InheritanceDepth(f.FieldType.GetGenericArguments()[0]))
                .FirstOrDefault()
                ?? throw new Exception($"{parent.GetType().Name} has no child list accepting {child.GetType().Name}.");
            ((IList)list.GetValue(parent)).Add(child);
        }

        private static int InheritanceDepth(Type type)
        {
            int depth = 0;
            for (Type b = type; b != null; b = b.BaseType) depth++;
            return depth;
        }
        #endregion

        #region ---- write ----
        private static XElement WriteElement(object node)
        {
            A_XSDTypeAttribute typeMeta = node.GetType().GetCustomAttribute<A_XSDTypeAttribute>(false)
                ?? throw new Exception($"Type {node.GetType().Name} is missing [A_XSDType].");
            XElement element = new XElement(typeMeta.Name);

            foreach (MemberInfo member in ScalarMembers(node.GetType()))
            {
                A_XSDElementPropertyAttribute meta = member.GetCustomAttribute<A_XSDElementPropertyAttribute>();
                object value = GetMember(member, node);
                if (value == null) continue;
                element.SetAttributeValue(meta.Name, Convert.ToString(value, CultureInfo.InvariantCulture));
            }

            foreach (FieldInfo list in ChildListFields(node.GetType()))
                foreach (object child in (IEnumerable)list.GetValue(node))
                    element.Add(WriteElement(child));

            return element;
        }
        #endregion

        #region ---- reflection helpers ----
        private static IEnumerable<MemberInfo> ScalarMembers(Type type) =>
            type.GetMembers(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.GetCustomAttribute<A_XSDElementPropertyAttribute>() != null);

        private static IEnumerable<FieldInfo> ChildListFields(Type type) =>
            type.GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Where(f => f.FieldType.IsGenericType
                            && f.FieldType.GetGenericTypeDefinition() == typeof(List<>));

        private static Type MemberType(MemberInfo m) =>
            m is PropertyInfo p ? p.PropertyType : ((FieldInfo)m).FieldType;

        private static void SetMember(MemberInfo m, object target, object value)
        {
            if (m is PropertyInfo p) p.SetValue(target, value);
            else ((FieldInfo)m).SetValue(target, value);
        }

        private static object GetMember(MemberInfo m, object target) =>
            m is PropertyInfo p ? p.GetValue(target) : ((FieldInfo)m).GetValue(target);
        #endregion
    }
}
