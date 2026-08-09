using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.Filing.Serialization;
using ArctisAurora.Core.Registry;
using System.Collections;
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
            XmlReflection.ApplyAttributes(element, node);
            foreach (XElement childElement in element.Elements())
            {
                object child = ParseElement(childElement);
                if (!AssignComplexMember(node, child))
                    AttachChild(node, child);
            }
            return node;
        }

        // A complex [A_XSDElementProperty] member arrives as a nested element rather than an
        // attribute, since an attribute can only carry a simple value. Matched by type, which is why
        // the member is named after its type — see RichTextDocument.layout.
        private static bool AssignComplexMember(object parent, object child)
        {
            MemberInfo member = XmlReflection.ComplexMembers(parent.GetType())
                .FirstOrDefault(m => XmlReflection.MemberType(m).IsAssignableFrom(child.GetType()));
            if (member == null) return false;

            XmlReflection.SetMember(member, parent, child);
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

            FieldInfo list = XmlReflection.ChildListFields(parent.GetType())
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

            // Only what differs from a fresh instance, mirroring the reader: absent attribute -> default.
            Dictionary<MemberInfo, object> defaults = XmlReflection.Defaults(node.GetType());

            foreach (MemberInfo member in XmlReflection.ScalarMembers(node.GetType()))
            {
                A_XSDElementPropertyAttribute meta = member.GetCustomAttribute<A_XSDElementPropertyAttribute>();
                object value = XmlReflection.GetMember(member, node);
                if (value == null) continue;
                if (Equals(value, defaults[member])) continue;
                element.SetAttributeValue(meta.Name, Convert.ToString(value, CultureInfo.InvariantCulture));
            }

            // Before the child lists, matching the order the schema declares them in the sequence.
            foreach (MemberInfo member in XmlReflection.ComplexMembers(node.GetType()))
            {
                object value = XmlReflection.GetMember(member, node);
                if (value != null) element.Add(WriteElement(value));
            }

            // only XSD-typed children are content; a run's children are glyphs
            foreach (FieldInfo list in XmlReflection.ChildListFields(node.GetType()))
                foreach (object child in (IEnumerable)list.GetValue(node))
                    if (child.GetType().GetCustomAttribute<A_XSDTypeAttribute>(false) != null)
                        element.Add(WriteElement(child));

            return element;
        }
        #endregion
    }
}
