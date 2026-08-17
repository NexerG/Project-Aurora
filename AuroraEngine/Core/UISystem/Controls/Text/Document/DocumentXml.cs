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
            XElement root = WriteElement(document, document.layout);

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
        private static XElement WriteElement(object node, DocumentLayout layout)
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
                if (IsResolved(node, member, defaults, layout)) continue;
                element.SetAttributeValue(meta.Name, Format(value));
            }

            // Before the child lists, matching the order the schema declares them in the sequence.
            // A member with nothing to say is left out rather than written empty, so a save does not
            // grow the note by an element that reads back as the default it already had.
            foreach (MemberInfo member in XmlReflection.ComplexMembers(node.GetType()))
            {
                object value = XmlReflection.GetMember(member, node);
                if (value == null) continue;

                XElement child = WriteElement(value, layout);
                if (child.HasAttributes || child.HasElements) element.Add(child);
            }

            // only XSD-typed children are content; a run's children are glyphs
            foreach (FieldInfo list in XmlReflection.ChildListFields(node.GetType()))
                foreach (object child in (IEnumerable)list.GetValue(node))
                    if (child.GetType().GetCustomAttribute<A_XSDTypeAttribute>(false) != null)
                        element.Add(WriteElement(child, layout));

            return element;
        }

        // xs:boolean has no True — a note carrying one fails its own schema, and Convert.ToString
        // spells a bool the C# way.
        private static string Format(object value) =>
            value is bool flag ? (flag ? "true" : "false") : Convert.ToString(value, CultureInfo.InvariantCulture);

        // Values a setter computed rather than an author writing them. Both would round-trip to the
        // same picture today and pin the note to it forever after — the run would keep the size the
        // styles file happened to say on the day it was saved, and the colour the enum happened to
        // mean.
        private static bool IsResolved(object node, MemberInfo member, Dictionary<MemberInfo, object> defaults, DocumentLayout layout)
        {
            // ApplyLayout writes the styling scheme's size into every run when the note is loaded.
            if (node is TextRun run && member.Name == nameof(TextControl.fontSize))
            {
                TextStyleType type = run.stylingType == TextStyleType.Inherit
                    ? (run.parent as ContentBlock)?.stylingType ?? TextStyleType.Text
                    : run.stylingType;

                return run.fontSize == layout.FontSizeFor(type);
            }

            // ControlColor's setter resolves into ColorHex, so a control that named a colour holds
            // both. Only skip the hex when the enum is itself being written — a control sitting on
            // the enum's default value has it omitted, and dropping the hex too would lose the
            // colour entirely.
            if (node is VulkanControl control && member.Name == nameof(VulkanControl.controlColorHex))
            {
                MemberInfo colour = XmlReflection.ScalarMembers(node.GetType())
                    .FirstOrDefault(m => m.Name == nameof(VulkanControl.controlColor));

                return colour != null
                    && !Equals(control.controlColor, defaults[colour])
                    && control.controlColorHex == VulkanControl.EnumColorToHex(control.controlColor);
            }

            return false;
        }
        #endregion
    }
}
