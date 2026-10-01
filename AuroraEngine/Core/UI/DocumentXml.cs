using ArctisAurora.Core.ECS.EngineEntity;
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
        public static RichTextDocument Load(string path)
        {
            XElement root = XDocument.Load(path).Root ?? throw new Exception($"Note '{path}' is empty.");
            ResolvePictures(root, Path.GetDirectoryName(Path.GetFullPath(path))!);
            return Parse(root);
        }

        // Picture paths in a note's tree: relative to its folder on disk, absolute in memory.
        internal static void ResolvePictures(XElement root, string folder)
        {
            foreach (XAttribute image in Pictures(root))
                image.Value = Path.GetFullPath(Path.Combine(folder, image.Value));
        }

        internal static void RelativePictures(XElement root, string folder)
        {
            foreach (XAttribute image in Pictures(root))
                image.Value = Path.GetRelativePath(folder, image.Value).Replace('\\', '/');
        }

        private static List<XAttribute> Pictures(XElement root) =>
            root.Descendants().Where(e => e.Name.LocalName == "Run").Select(e => e.Attribute("Image")).OfType<XAttribute>().ToList();

        // Builds a note from a <Document> tree, whichever file format produced it.
        public static RichTextDocument Parse(XElement root)
        {
            RichTextDocument document = new RichTextDocument();
            XmlReflection.ApplyAttributes(root, document, tolerant: true);

            foreach (XElement element in root.Elements())
                switch (element.Name.LocalName)
                {
                    case "DocumentLayout": ReadLayout(element, document.layout); break;
                    case "Block": document.blocks.Add(ReadBlock(element)); break;
                    case "Table": document.blocks.Add(ReadTable(element)); break;
                    default: throw new Exception($"Unknown document element '{element.Name.LocalName}'.");
                }

            return document;
        }

        private static void ReadLayout(XElement element, DocumentLayout layout)
        {
            XmlReflection.ApplyAttributes(element, layout, tolerant: true);

            foreach (XElement child in element.Elements())
            {
                if (child.Name.LocalName == "Page")
                {
                    layout.page = new PageLayout();
                    XmlReflection.ApplyAttributes(child, layout.page, tolerant: true);
                    continue;
                }

                if (child.Name.LocalName == "ListLevel")
                {
                    ListLevel level = new ListLevel();
                    XmlReflection.ApplyAttributes(child, level, tolerant: true);
                    layout.listLevels.Add(level);
                    continue;
                }

                TextStyle style = new TextStyle();
                XmlReflection.ApplyAttributes(child, style, tolerant: true);
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
            XAttribute marker = element.Attribute("Marker");
            if (marker != null && Enum.TryParse(marker.Value, true, out ListMarker style))
                block.listMarker = style;

            foreach (XElement child in element.Elements())
            {
                Run run = new Run();
                XmlReflection.ApplyAttributes(child, run, tolerant: true);
                block.AppendRun(run);
            }

            // A block with no runs still needs one span, or it can hold neither a caret nor a style.
            if (block.spans.Count == 0) block.AppendRun(new Run());

            return block;
        }

        // <Column Width>s, then <Row>s of <Cell>s of <Block>s; a row wider than the columns adds columns.
        private static TableControl ReadTable(XElement element)
        {
            List<float> widths = new List<float>();
            foreach (XElement column in element.Elements().Where(e => e.Name.LocalName == "Column"))
                widths.Add((float?)column.Attribute("Width") ?? defaultColumnWidth);

            List<List<List<BlockControl>>> rows = new List<List<List<BlockControl>>>();
            foreach (XElement row in element.Elements().Where(e => e.Name.LocalName == "Row"))
            {
                List<List<BlockControl>> cells = new List<List<BlockControl>>();
                foreach (XElement cell in row.Elements().Where(e => e.Name.LocalName == "Cell"))
                    cells.Add(cell.Elements().Where(e => e.Name.LocalName == "Block").Select(ReadBlock).ToList());

                while (widths.Count < cells.Count) widths.Add(defaultColumnWidth);
                rows.Add(cells);
            }

            TableControl table = new TableControl(widths);
            foreach (List<List<BlockControl>> cells in rows)
                table.AddRow(cells);

            return table;
        }

        private const float defaultColumnWidth = 150f;
        #endregion

        #region ---- write ----
        public static void Save(RichTextDocument document, string path)
        {
            XNamespace ns = XSDGenerator.NamespaceFor("UI");
            XElement root = ToXml(document);
            RelativePictures(root, Path.GetDirectoryName(Path.GetFullPath(path))!);

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
            if (document.layout.page != null)
                layout.Add(WriteScalars(ns + "Page", document.layout.page));
            foreach (ListLevel level in document.layout.listLevels)
                layout.Add(WriteScalars(ns + "ListLevel", level));
            if (layout.HasAttributes || layout.HasElements) root.Add(layout);

            foreach (Control entry in document.blocks)
                root.Add(entry is TableControl table ? WriteTable(ns, table) : WriteBlock(ns, (BlockControl)entry));

            return root;
        }

        private static XElement WriteBlock(XNamespace ns, BlockControl block)
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
            if (block.listMarker.HasValue)
                element.SetAttributeValue("Marker", block.listMarker.Value.ToString());

            foreach (Run run in block.Runs())
                element.Add(WriteScalars(ns + "Run", run));

            return element;
        }

        private static XElement WriteTable(XNamespace ns, TableControl table)
        {
            XElement element = new XElement(ns + "Table");
            foreach (float width in table.widths)
                element.Add(new XElement(ns + "Column", new XAttribute("Width", Format(width))));

            XElement row = null;
            foreach (StackPanelControl cell in table.Cells())
            {
                if (cell.gridColumn == 0)
                {
                    row = new XElement(ns + "Row");
                    element.Add(row);
                }

                XElement written = new XElement(ns + "Cell");
                foreach (Entity entry in cell.children)
                    if (entry is BlockControl block) written.Add(WriteBlock(ns, block));
                row.Add(written);
            }

            return element;
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
