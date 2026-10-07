using ArctisAurora.Core.Users;
using System.Globalization;
using System.Xml.Linq;

namespace ArctisAurora.Core.UI
{
    // <Planner Name><Category Id Name Color/><Ticket Id Name Category Color Start End AllDay TimeZone Creator Created Order Follows><Assignee User/><Attachment Kind Side Minutes/></Ticket></Planner>
    public static class PlannerXml
    {
        private const string dayFormat = "yyyy-MM-dd";
        private const string timeFormat = "yyyy-MM-ddTHH:mm";

        public static PlannerDocument Load(string path) =>
            Parse(XDocument.Load(path, LoadOptions.PreserveWhitespace).Root ?? throw new Exception($"Planner '{path}' is empty."));

        public static void Save(PlannerDocument document, string path)
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            new XDocument(new XDeclaration("1.0", "utf-8", null), ToXml(document)).Save(path);
        }

        #region ---- read ----
        public static PlannerDocument Parse(XElement root)
        {
            PlannerDocument document = new PlannerDocument { name = (string?)root.Attribute("Name") };

            foreach (XElement element in root.Elements())
            {
                switch (element.Name.LocalName)
                {
                    case "Category":
                        document.categories.Add(new PlannerCategory
                        {
                            id = Id(element, "Id") ?? Guid.NewGuid(),
                            name = (string?)element.Attribute("Name") ?? "",
                            colorHex = Color(element) ?? PlannerDocument.defaultColor
                        });
                        break;
                    case "Ticket":
                        if (ReadTicket(element) is PlannerTicket ticket) document.tickets.Add(ticket);
                        else document.extra.Add(new XElement(element));
                        break;
                    default:
                        document.extra.Add(new XElement(element));
                        break;
                }
            }

            if (document.categories.Count == 0)
                document.categories.Add(new PlannerCategory { name = "General" });
            return document;
        }

        // Null when the ticket has no readable start and end.
        private static PlannerTicket? ReadTicket(XElement element)
        {
            if (Time(element, "Start") is not DateTime start || Time(element, "End") is not DateTime end) return null;

            PlannerTicket ticket = new PlannerTicket
            {
                id = Id(element, "Id") ?? Guid.NewGuid(),
                name = (string?)element.Attribute("Name") ?? "",
                categoryId = Id(element, "Category") ?? Guid.Empty,
                colorHex = Color(element),
                time = new TimeRange(start, end < start ? start : end, (string?)element.Attribute("AllDay") == "true", (string?)element.Attribute("TimeZone")),
                creator = (string?)element.Attribute("Creator") is string creator ? User.Named(creator) : null,
                created = Time(element, "Created"),
                order = int.TryParse((string?)element.Attribute("Order"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int order) ? order : 0,
                follows = Id(element, "Follows")
            };

            foreach (XElement child in element.Elements())
            {
                if (child.Name.LocalName == "Assignee" && (string?)child.Attribute("User") is string user && user.Length > 0)
                    ticket.assignees.Add(User.Named(user));
                else if (child.Name.LocalName == "Attachment" && ReadAttachment(child) is PlannerAttachment attachment)
                    ticket.attachments.Add(attachment);
                else ticket.extra.Add(new XElement(child));
            }
            return ticket;
        }

        // Null without a kind and a readable length.
        private static PlannerAttachment? ReadAttachment(XElement element)
        {
            if ((string?)element.Attribute("Kind") is not { Length: > 0 } kind) return null;
            if (!int.TryParse((string?)element.Attribute("Minutes"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int minutes) || minutes <= 0) return null;
            return new PlannerAttachment(kind, (string?)element.Attribute("Side") != "After", TimeSpan.FromMinutes(minutes));
        }

        private static Guid? Id(XElement element, string attribute) =>
            Guid.TryParse((string?)element.Attribute(attribute), out Guid id) ? id : null;

        private static DateTime? Time(XElement element, string attribute) =>
            DateTime.TryParse((string?)element.Attribute(attribute), CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime time) ? time : null;

        // A colour as #RRGGBB or RRGGBB; anything else is dropped.
        private static string? Color(XElement element)
        {
            string? color = (string?)element.Attribute("Color");
            if (color == null) return null;
            string digits = color.StartsWith('#') ? color[1..] : color;
            return digits.Length == 6 && digits.All(char.IsAsciiHexDigit) ? color : null;
        }
        #endregion

        #region ---- write ----
        public static XElement ToXml(PlannerDocument document)
        {
            XElement root = new XElement("Planner");
            if (document.name != null) root.SetAttributeValue("Name", document.name);

            foreach (PlannerCategory category in document.categories)
                root.Add(new XElement("Category",
                    new XAttribute("Id", category.id),
                    new XAttribute("Name", category.name),
                    new XAttribute("Color", category.colorHex)));

            foreach (PlannerTicket ticket in document.tickets)
            {
                string format = ticket.time.allDay ? dayFormat : timeFormat;
                XElement element = new XElement("Ticket",
                    new XAttribute("Id", ticket.id),
                    new XAttribute("Name", ticket.name),
                    new XAttribute("Category", ticket.categoryId));
                element.SetAttributeValue("Color", ticket.colorHex);
                element.SetAttributeValue("Start", ticket.time.start.ToString(format, CultureInfo.InvariantCulture));
                element.SetAttributeValue("End", ticket.time.end.ToString(format, CultureInfo.InvariantCulture));
                if (ticket.time.allDay) element.SetAttributeValue("AllDay", "true");
                element.SetAttributeValue("TimeZone", ticket.time.timeZone);
                element.SetAttributeValue("Creator", ticket.creator?.name);
                element.SetAttributeValue("Created", ticket.created?.ToString("s", CultureInfo.InvariantCulture));
                if (ticket.order != 0) element.SetAttributeValue("Order", ticket.order.ToString(CultureInfo.InvariantCulture));
                element.SetAttributeValue("Follows", ticket.follows);

                foreach (User assignee in ticket.assignees)
                    element.Add(new XElement("Assignee", new XAttribute("User", assignee.name)));
                foreach (PlannerAttachment attachment in ticket.attachments)
                    element.Add(new XElement("Attachment",
                        new XAttribute("Kind", attachment.kind),
                        new XAttribute("Side", attachment.before ? "Before" : "After"),
                        new XAttribute("Minutes", ((int)attachment.duration.TotalMinutes).ToString(CultureInfo.InvariantCulture))));
                element.Add(ticket.extra.Select(e => new XElement(e)));
                root.Add(element);
            }

            root.Add(document.extra.Select(e => new XElement(e)));
            return root;
        }
        #endregion
    }
}
