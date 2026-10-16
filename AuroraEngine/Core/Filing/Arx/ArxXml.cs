using System.Xml.Linq;

namespace ArctisAurora.Core.Filing.Arx
{
    // Interned strings an archive's XML entries refer to by id.
    public sealed class ArxStrings
    {
        private readonly Dictionary<string, int> _ids = new Dictionary<string, int>(StringComparer.Ordinal);
        public readonly List<string> list = new List<string>();

        public int Id(string value)
        {
            if (_ids.TryGetValue(value, out int id)) return id;
            id = list.Count;
            list.Add(value);
            _ids.Add(value, id);
            return id;
        }
    }

    // An XML tree as records of string ids: elements, attributes, text and CDATA.
    public static class ArxXml
    {
        // node tags
        private const byte element = 1;
        private const byte text = 2;
        private const byte cdata = 3;

        public static void Encode(XElement root, ArxStrings strings, BinaryWriter writer) => WriteElement(root, strings, writer);

        public static XElement Decode(BinaryReader reader, string[] strings)
        {
            reader.ReadByte();
            return ReadElement(reader, strings);
        }

        private static void WriteElement(XElement node, ArxStrings strings, BinaryWriter writer)
        {
            writer.Write(element);
            writer.Write7BitEncodedInt(strings.Id(node.Name.NamespaceName));
            writer.Write7BitEncodedInt(strings.Id(node.Name.LocalName));

            List<XAttribute> attributes = node.Attributes().ToList();
            writer.Write7BitEncodedInt(attributes.Count);
            foreach (XAttribute attribute in attributes)
            {
                writer.Write7BitEncodedInt(strings.Id(attribute.Name.NamespaceName));
                writer.Write7BitEncodedInt(strings.Id(attribute.Name.LocalName));
                writer.Write7BitEncodedInt(strings.Id(attribute.Value));
            }

            List<XNode> children = node.Nodes().Where(n => n is XElement || n is XText).ToList();
            if (children.Count == 0 && !node.IsEmpty) children.Add(new XText(string.Empty));
            writer.Write7BitEncodedInt(children.Count);
            foreach (XNode child in children)
            {
                if (child is XElement e) WriteElement(e, strings, writer);
                else
                {
                    writer.Write(child is XCData ? cdata : text);
                    writer.Write7BitEncodedInt(strings.Id(((XText)child).Value));
                }
            }
        }

        private static XElement ReadElement(BinaryReader reader, string[] strings)
        {
            string elementNs = strings[reader.Read7BitEncodedInt()];
            XElement node = new XElement(XName.Get(strings[reader.Read7BitEncodedInt()], elementNs));

            int attributeCount = reader.Read7BitEncodedInt();
            for (int i = 0; i < attributeCount; i++)
            {
                string ns = strings[reader.Read7BitEncodedInt()];
                string local = strings[reader.Read7BitEncodedInt()];
                node.Add(new XAttribute(XName.Get(local, ns), strings[reader.Read7BitEncodedInt()]));
            }

            int childCount = reader.Read7BitEncodedInt();
            for (int i = 0; i < childCount; i++)
            {
                byte tag = reader.ReadByte();
                if (tag == element) node.Add(ReadElement(reader, strings));
                else
                {
                    string value = strings[reader.Read7BitEncodedInt()];
                    if (tag == cdata) node.Add(new XCData(value));
                    else node.Add(value);
                }
            }
            return node;
        }
    }
}
