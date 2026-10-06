using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.Filing.Serialization;
using ArctisAurora.Core.Registry;
using System.Globalization;
using System.Numerics;
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
                    case "Footnote":
                    case "Float": document.blocks.AddRange(ReadInsert(element)); break;
                    default: throw new Exception($"Unknown document element '{element.Name.LocalName}'.");
                }

            return document;
        }

        // A <Footnote Id> or <Float Kind Placement>: its blocks and tables, sharing one PageInsert.
        private static IEnumerable<Control> ReadInsert(XElement element)
        {
            PageInsert insert = element.Name.LocalName == "Footnote"
                ? new PageInsert { footnote = (string?)element.Attribute("Id") ?? "" }
                : new PageInsert { floatKind = (string?)element.Attribute("Kind") ?? "figure", placement = (string?)element.Attribute("Placement") ?? "tbp" };
            foreach (XElement child in element.Elements())
                switch (child.Name.LocalName)
                {
                    case "Block":
                        BlockControl block = ReadBlock(child);
                        block.insert = insert;
                        yield return block;
                        break;
                    case "Table":
                        TableControl table = ReadTable(child);
                        table.insert = insert;
                        yield return table;
                        break;
                }
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
                    foreach (XElement named in child.Elements().Where(e => e.Name.LocalName == "PageStyle"))
                    {
                        PageStyle pageStyle = new PageStyle();
                        XmlReflection.ApplyAttributes(named, pageStyle, tolerant: true);
                        foreach (XElement slot in named.Elements().Where(e => e.Name.LocalName == "Slot"))
                        {
                            RunningSlot read = new RunningSlot();
                            XmlReflection.ApplyAttributes(slot, read, tolerant: true);
                            pageStyle.slots.Add(read);
                        }
                        layout.page.styles.Add(pageStyle);
                    }
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

            XAttribute align = element.Attribute("Align");
            if (align != null && Enum.TryParse(align.Value, true, out TextAlignment alignment))
                block.alignment = alignment;
            block.firstIndent = (float?)element.Attribute("Indent") ?? 0f;
            block.spaceBefore = (float?)element.Attribute("SpaceBefore");
            block.pageBreak = ReadPageBreak(element);
            block.pageStyle = (string?)element.Attribute("PageStyle");
            block.markLeft = (string?)element.Attribute("MarkLeft");
            block.markRight = (string?)element.Attribute("MarkRight");
            block.language = (string?)element.Attribute("Language");
            block.codeWrap = (bool?)element.Attribute("Wrap") ?? false;

            XAttribute list = element.Attribute("List");
            if (list != null && Enum.TryParse(list.Value, true, out ListKind kind))
                block.listKind = kind;
            block.listLevel = (int?)element.Attribute("Level") ?? 0;
            block.isChecked = (bool?)element.Attribute("Checked") ?? false;
            XAttribute marker = element.Attribute("Marker");
            if (marker != null && Enum.TryParse(marker.Value, true, out ListMarker style))
                block.listMarker = style;
            block.listStart = (int?)element.Attribute("Start");

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
        internal static TableControl ReadTable(XElement element)
        {
            List<float> widths = new List<float>();
            foreach (XElement column in element.Elements().Where(e => e.Name.LocalName == "Column"))
                widths.Add((float?)column.Attribute("Width") ?? defaultColumnWidth);

            List<(List<List<BlockControl>> cells, List<int> spans)> rows = new List<(List<List<BlockControl>>, List<int>)>();
            Dictionary<(int row, int column), CellRules> rules = new Dictionary<(int, int), CellRules>();
            foreach (XElement row in element.Elements().Where(e => e.Name.LocalName == "Row"))
            {
                List<List<BlockControl>> cells = new List<List<BlockControl>>();
                List<int> spans = new List<int>();
                foreach (XElement cell in row.Elements().Where(e => e.Name.LocalName == "Cell"))
                {
                    if (cell.Attribute("RuleAbove") != null || cell.Attribute("RuleBelow") != null)
                        rules[(rows.Count, spans.Sum())] = ReadCellRules(cell);
                    cells.Add(cell.Elements().Where(e => e.Name.LocalName == "Block").Select(ReadBlock).ToList());
                    spans.Add(Math.Max(1, (int?)cell.Attribute("ColumnSpan") ?? 1));
                }

                while (widths.Count < spans.Sum()) widths.Add(defaultColumnWidth);
                rows.Add((cells, spans));
            }

            TableControl table = new TableControl(widths)
            {
                showBorders = (bool?)element.Attribute("Borders") ?? true,
                spaceBefore = (float?)element.Attribute("SpaceBefore"),
                alignment = Enum.TryParse((string?)element.Attribute("Align"), true, out TextAlignment align) ? align : TextAlignment.Left,
                pageBreak = ReadPageBreak(element),
                cellPadding = ReadPadding((string?)element.Attribute("Padding"))
            };
            foreach ((List<List<BlockControl>> cells, List<int> spans) in rows)
                table.AddRow(cells, spans);

            List<XElement> columns = element.Elements().Where(e => e.Name.LocalName == "Column").ToList();
            for (int c = 0; c < columns.Count; c++)
            {
                table.leftRules[c] = ReadRule((string?)columns[c].Attribute("RuleLeft")).kind;
                table.rightRules[c] = ReadRule((string?)columns[c].Attribute("RuleRight")).kind;
            }
            foreach (StackPanelControl cell in table.Cells())
                if (rules.TryGetValue((cell.gridRow, cell.gridColumn), out CellRules found)) table.cellRules[cell] = found;
            table.ApplyInsets();

            return table;
        }

        private static CellRules ReadCellRules(XElement cell)
        {
            (TableRule above, float aboveWidth) = ReadRule((string?)cell.Attribute("RuleAbove"));
            (TableRule below, float belowWidth) = ReadRule((string?)cell.Attribute("RuleBelow"));
            return new CellRules
            {
                above = above,
                aboveWidth = aboveWidth,
                aboveTrim = Enum.TryParse((string?)cell.Attribute("TrimAbove"), true, out RuleTrim a) ? a : RuleTrim.None,
                below = below,
                belowWidth = belowWidth,
                belowTrim = Enum.TryParse((string?)cell.Attribute("TrimBelow"), true, out RuleTrim b) ? b : RuleTrim.None
            };
        }

        // "Kind" or "Kind width", the width in px.
        private static (TableRule kind, float width) ReadRule(string? value)
        {
            string[] parts = (value ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || !Enum.TryParse(parts[0], true, out TableRule kind)) return (TableRule.None, 0f);
            float width = parts.Length > 1 && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float w) ? w : 0f;
            return (kind, width);
        }

        // "across down", px.
        private static Vector2? ReadPadding(string? value)
        {
            string[] parts = (value ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2
                || !float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float across)
                || !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float down)) return null;
            return new Vector2(across, down);
        }

        private static PageBreak ReadPageBreak(XElement element) =>
            Enum.TryParse((string?)element.Attribute("PageBreak"), true, out PageBreak value) ? value : PageBreak.None;

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
            {
                XElement page = WriteScalars(ns + "Page", document.layout.page);
                foreach (PageStyle style in document.layout.page.styles)
                {
                    XElement named = WriteScalars(ns + "PageStyle", style);
                    named.Add(style.slots.Select(s => WriteScalars(ns + "Slot", s)));
                    page.Add(named);
                }
                layout.Add(page);
            }
            foreach (ListLevel level in document.layout.listLevels)
                layout.Add(WriteScalars(ns + "ListLevel", level));
            if (layout.HasAttributes || layout.HasElements) root.Add(layout);

            XElement? group = null;
            PageInsert? grouped = null;
            foreach (Control entry in document.blocks)
            {
                PageInsert? insert = entry is TableControl t ? t.insert : ((BlockControl)entry).insert;
                if (insert != grouped)
                {
                    grouped = insert;
                    group = insert == null ? null
                        : insert.footnote != null ? new XElement(ns + "Footnote", new XAttribute("Id", insert.footnote))
                        : new XElement(ns + "Float", new XAttribute("Kind", insert.floatKind ?? "figure"), new XAttribute("Placement", insert.placement));
                    if (group != null) root.Add(group);
                }
                (group ?? root).Add(entry is TableControl table ? WriteTable(ns, table) : WriteBlock(ns, (BlockControl)entry));
            }

            return root;
        }

        private static XElement WriteBlock(XNamespace ns, BlockControl block)
        {
            XElement element = new XElement(ns + "Block");
            if (block.stylingType != TextStyleType.Text)
                element.SetAttributeValue("StylingType", block.stylingType.ToString());
            if (block.alignment != TextAlignment.Left)
                element.SetAttributeValue("Align", block.alignment.ToString());
            if (block.firstIndent != 0f)
                element.SetAttributeValue("Indent", Format(block.firstIndent));
            if (block.spaceBefore.HasValue)
                element.SetAttributeValue("SpaceBefore", Format(block.spaceBefore.Value));
            if (block.pageBreak != PageBreak.None)
                element.SetAttributeValue("PageBreak", block.pageBreak.ToString());
            if (block.pageStyle != null)
                element.SetAttributeValue("PageStyle", block.pageStyle);
            if (block.markLeft != null)
                element.SetAttributeValue("MarkLeft", block.markLeft);
            if (block.markRight != null)
                element.SetAttributeValue("MarkRight", block.markRight);
            if (!string.IsNullOrEmpty(block.language))
                element.SetAttributeValue("Language", block.language);
            if (block.codeWrap && block.stylingType == TextStyleType.Code)
                element.SetAttributeValue("Wrap", "true");
            if (block.listKind != ListKind.None)
                element.SetAttributeValue("List", block.listKind.ToString());
            if (block.listLevel > 0)
                element.SetAttributeValue("Level", block.listLevel);
            if (block.isChecked)
                element.SetAttributeValue("Checked", "true");
            if (block.listMarker.HasValue)
                element.SetAttributeValue("Marker", block.listMarker.Value.ToString());
            if (block.listStart.HasValue)
                element.SetAttributeValue("Start", block.listStart.Value);

            foreach (Run run in block.Runs())
                element.Add(WriteScalars(ns + "Run", run));

            return element;
        }

        internal static XElement WriteTable(TableControl table) => WriteTable(XSDGenerator.NamespaceFor("UI"), table);

        // A table of empty cells, every column one width.
        internal static XElement NewTable(int rows, int columns, float width)
        {
            XNamespace ns = XSDGenerator.NamespaceFor("UI");
            return new XElement(ns + "Table",
                Enumerable.Range(0, columns).Select(_ => new XElement(ns + "Column", new XAttribute("Width", Format(width)))),
                Enumerable.Range(0, rows).Select(_ => new XElement(ns + "Row",
                    Enumerable.Range(0, columns).Select(_ => new XElement(ns + "Cell")))));
        }

        private static XElement WriteTable(XNamespace ns, TableControl table)
        {
            XElement element = new XElement(ns + "Table");
            if (!table.showBorders) element.SetAttributeValue("Borders", "false");
            if (table.spaceBefore.HasValue) element.SetAttributeValue("SpaceBefore", Format(table.spaceBefore.Value));
            if (table.alignment != TextAlignment.Left) element.SetAttributeValue("Align", table.alignment.ToString());
            if (table.pageBreak != PageBreak.None) element.SetAttributeValue("PageBreak", table.pageBreak.ToString());
            if (table.cellPadding is Vector2 pad) element.SetAttributeValue("Padding", $"{Format(pad.X)} {Format(pad.Y)}");
            for (int c = 0; c < table.widths.Count; c++)
            {
                XElement column = new XElement(ns + "Column", new XAttribute("Width", Format(table.widths[c])));
                if (table.leftRules[c] != TableRule.None) column.SetAttributeValue("RuleLeft", table.leftRules[c].ToString());
                if (table.rightRules[c] != TableRule.None) column.SetAttributeValue("RuleRight", table.rightRules[c].ToString());
                element.Add(column);
            }

            XElement row = null;
            foreach (StackPanelControl cell in table.Cells())
            {
                if (cell.gridColumn == 0)
                {
                    row = new XElement(ns + "Row");
                    element.Add(row);
                }

                XElement written = new XElement(ns + "Cell");
                if (table.ColumnSpan(cell) > 1) written.SetAttributeValue("ColumnSpan", table.ColumnSpan(cell));
                if (table.cellRules.TryGetValue(cell, out CellRules rules))
                {
                    if (rules.above != TableRule.None) written.SetAttributeValue("RuleAbove", WriteRule(rules.above, rules.aboveWidth));
                    if (rules.aboveTrim != RuleTrim.None) written.SetAttributeValue("TrimAbove", rules.aboveTrim.ToString());
                    if (rules.below != TableRule.None) written.SetAttributeValue("RuleBelow", WriteRule(rules.below, rules.belowWidth));
                    if (rules.belowTrim != RuleTrim.None) written.SetAttributeValue("TrimBelow", rules.belowTrim.ToString());
                }
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
        private static string WriteRule(TableRule kind, float width) => width > 0f ? $"{kind} {Format(width)}" : kind.ToString();

        private static string Format(object value) =>
            value is bool flag ? (flag ? "true" : "false") : Convert.ToString(value, CultureInfo.InvariantCulture);
        #endregion
    }
}
