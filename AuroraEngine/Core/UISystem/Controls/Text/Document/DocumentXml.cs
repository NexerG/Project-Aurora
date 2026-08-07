using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.Filing.Serialization;
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

        // Editor-wide layout defaults, which are a <DocumentLayout> document in their own right
        // rather than a special format — the same element a note embeds to override them.
        public static DocumentLayout LoadLayout(string path) =>
            (DocumentLayout)ParseElement(XDocument.Load(path).Root);

        public static void Save(RichTextDocument document, string path)
        {
            A_XSDTypeAttribute typeMeta = document.GetType().GetCustomAttribute<A_XSDTypeAttribute>(false);
            XElement root = WriteElement(document);

            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            // Bind the note to its schema so an editor can validate it on open. Resolved relative to
            // the note rather than fixed, because a vault sits wherever the user put it instead of at
            // a known depth beside the schema folder.
            string schemaFile = Path.Combine(Paths.XMLSCHEMAS, $"{typeMeta.Category}TypeSchema.xsd");
            string relative = Path.GetRelativePath(string.IsNullOrEmpty(dir) ? "." : dir, schemaFile).Replace('\\', '/');

            XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";
            root.SetAttributeValue(XNamespace.Xmlns + "xsi", xsi.NamespaceName);
            root.SetAttributeValue(xsi + "schemaLocation", $"{root.Name.NamespaceName} {relative}");

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
            {
                object child = ParseElement(childElement);
                if (!AssignComplexMember(node, child))
                    AttachChild(node, child);
            }
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

        // A complex [A_XSDElementProperty] member arrives as a nested element rather than an
        // attribute, since an attribute can only carry a simple value. Matched by type, which is why
        // the member is named after its type — see RichTextDocument.layout.
        private static bool AssignComplexMember(object parent, object child)
        {
            MemberInfo member = ComplexMembers(parent.GetType())
                .FirstOrDefault(m => MemberType(m).IsAssignableFrom(child.GetType()));
            if (member == null) return false;

            SetMember(member, parent, child);
            return true;
        }

        private static void AttachChild(object parent, object child)
        {
            // control children go through AddChild, for the parent pointer and tree order
            if (parent is VulkanControl control && child is Entity entity)
            {
                control.AddChild(entity);
                return;
            }

            FieldInfo list = ChildListFields(parent.GetType())
                .FirstOrDefault(f => f.FieldType.GetGenericArguments()[0].IsAssignableFrom(child.GetType()))
                ?? throw new Exception($"{parent.GetType().Name} has no child list accepting {child.GetType().Name}.");
            ((IList)list.GetValue(parent)).Add(child);
        }
        #endregion

        #region ---- write ----
        private static XElement WriteElement(object node)
        {
            A_XSDTypeAttribute typeMeta = node.GetType().GetCustomAttribute<A_XSDTypeAttribute>(false)
                ?? throw new Exception($"Type {node.GetType().Name} is missing [A_XSDType].");

            // Elements carry their category's namespace, the same one the schema declares as its
            // targetNamespace. Without it a saved note matches no schema at all — and re-saving a
            // hand-authored note silently stripped the xmlns it came with.
            XNamespace ns = XSDGenerator.NamespaceFor(typeMeta.Category);
            XElement element = new XElement(ns + typeMeta.Name);

            foreach (MemberInfo member in ScalarMembers(node.GetType()))
            {
                A_XSDElementPropertyAttribute meta = member.GetCustomAttribute<A_XSDElementPropertyAttribute>();
                object value = GetMember(member, node);
                if (value == null) continue;
                element.SetAttributeValue(meta.Name, Convert.ToString(value, CultureInfo.InvariantCulture));
            }

            // Before the child lists, matching the order the schema declares them in the sequence.
            foreach (MemberInfo member in ComplexMembers(node.GetType()))
            {
                object value = GetMember(member, node);
                if (value != null) element.Add(WriteElement(value));
            }

            // only XSD-typed children are content; a run's children are glyphs
            foreach (FieldInfo list in ChildListFields(node.GetType()))
                foreach (object child in (IEnumerable)list.GetValue(node))
                    if (child.GetType().GetCustomAttribute<A_XSDTypeAttribute>(false) != null)
                        element.Add(WriteElement(child));

            return element;
        }
        #endregion

        #region ---- reflection helpers ----
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

        private static IEnumerable<MemberInfo> ScalarMembers(Type type) =>
            AnnotatedMembers(type).Where(m => !IsComplexMember(MemberType(m)) && !IsListMember(MemberType(m))
                                              && !IsControlChrome(m));

        // Members declared on VulkanControl or above; a note stores content, not control chrome.
        private static bool IsControlChrome(MemberInfo member) =>
            member.DeclaringType.IsAssignableFrom(typeof(VulkanControl));

        private static IEnumerable<MemberInfo> ComplexMembers(Type type) =>
            AnnotatedMembers(type).Where(m => IsComplexMember(MemberType(m)));

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
