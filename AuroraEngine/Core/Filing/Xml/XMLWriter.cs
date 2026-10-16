using System.Text;
using System.Xml.Linq;

namespace ArctisAurora.Core.Filing.Xml
{
    // XML as text: a tab a level, CRLF, attributes in element order.
    public static class XMLWriter
    {
        public const string Newline = "\r\n";
        public const string Indent = "\t";

        // An element and its children; the caller has written its first indent.
        public static void Element(StringBuilder text, XElement element, int depth)
        {
            StartTag(text, element, false);
            if (element.IsEmpty)
            {
                text.Append(" />");
                return;
            }

            text.Append('>');
            if (!element.HasElements) Escape(text, element.Value, false);
            else
            {
                foreach (XElement child in element.Elements())
                {
                    text.Append(Newline);
                    for (int i = 0; i <= depth; i++) text.Append(Indent);
                    Element(text, child, depth + 1);
                }
                text.Append(Newline);
                for (int i = 0; i < depth; i++) text.Append(Indent);
            }
            text.Append("</").Append(element.Name.LocalName).Append('>');
        }

        public static void StartTag(StringBuilder text, XElement element, bool declareNamespace)
        {
            text.Append('<').Append(element.Name.LocalName);
            foreach (XAttribute attribute in element.Attributes())
            {
                text.Append(' ').Append(AttributeName(element, attribute)).Append("=\"");
                Escape(text, attribute.Value, true);
                text.Append('"');
            }

            if (!declareNamespace || element.Name.Namespace == XNamespace.None) return;
            text.Append(" xmlns=\"");
            Escape(text, element.Name.NamespaceName, true);
            text.Append('"');
        }

        private static string AttributeName(XElement element, XAttribute attribute)
        {
            XName name = attribute.Name;
            if (name.Namespace == XNamespace.None) return name.LocalName;
            if (name.Namespace == XNamespace.Xmlns) return "xmlns:" + name.LocalName;
            return element.GetPrefixOfNamespace(name.Namespace) + ":" + name.LocalName;
        }

        private static void Escape(StringBuilder text, string value, bool attribute)
        {
            foreach (char c in value)
                switch (c)
                {
                    case '&': text.Append("&amp;"); break;
                    case '<': text.Append("&lt;"); break;
                    case '>': text.Append("&gt;"); break;
                    case '"' when attribute: text.Append("&quot;"); break;
                    case '\n' when attribute: text.Append("&#xA;"); break;
                    case '\t' when attribute: text.Append("&#x9;"); break;
                    case '\r': text.Append("&#xD;"); break;
                    default:
                        if (c < ' ' && c != '\t' && c != '\n') throw new ArgumentException($"U+{(int)c:X4} cannot be written to XML.");
                        text.Append(c);
                        break;
                }
        }
    }
}
