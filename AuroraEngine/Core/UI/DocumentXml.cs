using ArctisAurora.Core.Filing.Serialization;
using ArctisAurora.Core.Registry;
using System.Globalization;
using System.Reflection;
using System.Xml.Linq;

namespace ArctisAurora.Core.UI
{
    // Load and save for the new stack's notes, over the same file format the outgoing DocumentXml
    // reads: <Document> holding <DocumentLayout> and <Block>s of <Run>s.
    //
    // Scalars still go through the engine's attribute-driven reflection, but the element names are
    // written by hand rather than resolved from [A_XSDType]: "Document", "Block" and "Run" belong to
    // the outgoing types until landing 6d, and a run here is a span on its block rather than an
    // object the reader can attach a child to.
    public static class DocumentXml
    {
        #region ---- parse ----
        public static RichTextDocument Load(string path) =>
            Parse(XDocument.Load(path).Root ?? throw new Exception($"Note '{path}' is empty."));

        // Builds a note from a <Document> tree, whichever file format produced it.
        public static RichTextDocument Parse(XElement root)
        {
            RichTextDocument document = new RichTextDocument();
            XmlReflection.ApplyAttributes(root, document);

            foreach (XElement element in root.Elements())
                switch (element.Name.LocalName)
                {
                    case "DocumentLayout": ReadLayout(element, document.layout); break;
                    case "Block": document.blocks.Add(ReadBlock(element)); break;
                    default: throw new Exception($"Unknown document element '{element.Name.LocalName}'.");
                }

            return document;
        }

        private static void ReadLayout(XElement element, DocumentLayout layout)
        {
            XmlReflection.ApplyAttributes(element, layout);

            foreach (XElement child in element.Elements())
            {
                TextStyle style = new TextStyle();
                XmlReflection.ApplyAttributes(child, style);
                layout.textStyles.Add(style);
            }
        }

        // A block carries few attributes of its own; everything else about it is its runs. Read by
        // name rather than through ApplyAttributes, which would also apply every control attribute
        // the block inherits and a note has no business carrying.
        private static BlockControl ReadBlock(XElement element)
        {
            BlockControl block = new BlockControl();

            XAttribute styling = element.Attribute("StylingType");
            if (styling != null && Enum.TryParse(styling.Value, true, out TextStyleType type))
                block.stylingType = type;

            XAttribute list = element.Attribute("List");
            if (list != null && Enum.TryParse(list.Value, true, out ListKind kind))
                block.listKind = kind;
            block.listLevel = (int?)element.Attribute("Level") ?? 0;
            block.isChecked = (bool?)element.Attribute("Checked") ?? false;

            foreach (XElement child in element.Elements())
            {
                Run run = new Run();
                XmlReflection.ApplyAttributes(child, run);
                block.AppendRun(run);
            }

            // A block with no runs still needs one span, or it can hold neither a caret nor a style.
            if (block.spans.Count == 0) block.AppendRun(new Run());

            return block;
        }
        #endregion

        #region ---- write ----
        public static void Save(RichTextDocument document, string path)
        {
            XNamespace ns = XSDGenerator.NamespaceFor("UI");
            XElement root = ToXml(document);

            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            // Bind the note to its schema so an editor can validate it on open. Resolved relative to
            // the note rather than fixed, because a vault sits wherever the user put it.
            string schemaFile = Path.Combine(Paths.XMLSCHEMAS, "UITypeSchema.xsd");
            string relative = Path.GetRelativePath(string.IsNullOrEmpty(dir) ? "." : dir, schemaFile).Replace('\\', '/');

            XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";
            root.SetAttributeValue(XNamespace.Xmlns + "xsi", xsi.NamespaceName);
            root.SetAttributeValue(xsi + "schemaLocation", $"{ns.NamespaceName} {relative}");

            new XDocument(new XDeclaration("1.0", "utf-8", null), root).Save(path);
        }

        // The note as a <Document> tree, for any file format to write out.
        public static XElement ToXml(RichTextDocument document)
        {
            XNamespace ns = XSDGenerator.NamespaceFor("UI");
            XElement root = WriteScalars(ns + "Document", document);

            XElement layout = WriteScalars(ns + "DocumentLayout", document.layout);
            foreach (TextStyle style in document.layout.textStyles)
                layout.Add(WriteScalars(ns + "TextStyle", style));
            if (layout.HasAttributes || layout.HasElements) root.Add(layout);

            foreach (BlockControl block in document.blocks)
            {
                XElement element = new XElement(ns + "Block");
                if (block.stylingType != TextStyleType.Text)
                    element.SetAttributeValue("StylingType", block.stylingType.ToString());
                if (block.listKind != ListKind.None)
                    element.SetAttributeValue("List", block.listKind.ToString());
                if (block.listLevel > 0)
                    element.SetAttributeValue("Level", block.listLevel);
                if (block.isChecked)
                    element.SetAttributeValue("Checked", "true");

                foreach (Run run in block.Runs())
                    element.Add(WriteScalars(ns + "Run", run));

                root.Add(element);
            }

            return root;
        }

        // Only what differs from a fresh instance, mirroring the reader: absent attribute -> default.
        private static XElement WriteScalars(XName name, object node)
        {
            XElement element = new XElement(name);
            Dictionary<MemberInfo, object> defaults = XmlReflection.Defaults(node.GetType());

            foreach (MemberInfo member in XmlReflection.ScalarMembers(node.GetType()))
            {
                object value = XmlReflection.GetMember(member, node);
                if (value == null) continue;
                if (Equals(value, defaults[member])) continue;

                A_XSDElementPropertyAttribute meta = member.GetCustomAttribute<A_XSDElementPropertyAttribute>();
                element.SetAttributeValue(meta.Name, Format(value));
            }

            return element;
        }

        // xs:boolean has no True — a note carrying one fails its own schema, and Convert.ToString
        // spells a bool the C# way.
        private static string Format(object value) =>
            value is bool flag ? (flag ? "true" : "false") : Convert.ToString(value, CultureInfo.InvariantCulture);
        #endregion
    }
}
