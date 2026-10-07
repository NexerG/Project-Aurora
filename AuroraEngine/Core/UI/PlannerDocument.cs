using ArctisAurora.Core.Editing;
using ArctisAurora.Core.Users;
using System.Xml.Linq;

namespace ArctisAurora.Core.UI
{
    // A planner file: categories and the tickets filed under them, shown as a Gantt chart or a board.
    public class PlannerDocument
    {
        public const string extension = ".planner.xml";
        public const string defaultColor = "#4C8BF5";

        public string? name;
        public readonly List<PlannerCategory> categories = new List<PlannerCategory>();
        public readonly List<PlannerTicket> tickets = new List<PlannerTicket>();

        // elements this version does not read, written back as they came
        public readonly List<XElement> extra = new List<XElement>();

        public readonly UndoStack undo = new UndoStack();

        // raised after an edit or its undo
        public event Action? changed;

        public void Changed() => changed?.Invoke();

        public static bool IsPlanner(string path) => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase);

        public static PlannerDocument Load(string path) => PlannerXml.Load(path);

        public void Save(string path) => PlannerXml.Save(this, path);

        // One category and no tickets.
        public static PlannerDocument Blank(string? name)
        {
            PlannerDocument document = new PlannerDocument { name = name };
            document.categories.Add(new PlannerCategory { name = "General", colorHex = defaultColor });
            return document;
        }

        // A ticket's category; one whose category is gone falls back to the first.
        public PlannerCategory CategoryOf(PlannerTicket ticket) =>
            categories.Find(category => category.id == ticket.categoryId) ?? categories[0];

        public string ColorOf(PlannerTicket ticket) => ticket.colorHex ?? CategoryOf(ticket).colorHex;

        // A category's tickets in board order.
        public IEnumerable<PlannerTicket> TicketsIn(PlannerCategory category) =>
            tickets.Where(ticket => ReferenceEquals(CategoryOf(ticket), category))
                .OrderBy(ticket => ticket.order).ThenBy(ticket => ticket.time.start);

        // The order that puts a ticket last in a category.
        public int NextOrder(PlannerCategory category)
        {
            List<PlannerTicket> filed = TicketsIn(category).ToList();
            return filed.Count == 0 ? 0 : filed.Max(ticket => ticket.order) + 1;
        }

        public PlannerTicket? Find(Guid id) => tickets.Find(ticket => ticket.id == id);

        // The tickets that follow a ticket directly.
        public IEnumerable<PlannerTicket> Followers(PlannerTicket ticket) =>
            tickets.Where(other => other.follows == ticket.id && !ReferenceEquals(other, ticket));

        // True when ticket is upstream of other: other follows it, directly or through others.
        public bool Upstream(PlannerTicket ticket, PlannerTicket other)
        {
            HashSet<Guid> seen = new HashSet<Guid>();
            for (PlannerTicket? at = other.follows is Guid id ? Find(id) : null; at != null && seen.Add(at.id); at = at.follows is Guid next ? Find(next) : null)
                if (ReferenceEquals(at, ticket)) return true;
            return false;
        }
    }

    public sealed class PlannerCategory
    {
        public Guid id = Guid.NewGuid();
        public string name = "";
        public string colorHex = PlannerDocument.defaultColor;
    }

    public sealed class PlannerTicket
    {
        public Guid id = Guid.NewGuid();
        public string name = "";
        public Guid categoryId;
        public string? colorHex;
        public TimeRange time;
        public User? creator;
        public DateTime? created;
        public readonly List<User> assignees = new List<User>();

        // position in its board column
        public int order;

        // time attached before and after it, and the ticket it follows
        public readonly List<PlannerAttachment> attachments = new List<PlannerAttachment>();
        public Guid? follows;

        public readonly List<XElement> extra = new List<XElement>();

        // The time it takes with its attachments.
        public DateTime OuterStart => time.start - Attached(true);

        public DateTime OuterEnd => time.end + Attached(false);

        public TimeSpan Attached(bool before)
        {
            TimeSpan total = TimeSpan.Zero;
            foreach (PlannerAttachment attachment in attachments)
                if (attachment.before == before) total += attachment.duration;
            return total;
        }

        public PlannerTicket Clone()
        {
            PlannerTicket copy = new PlannerTicket();
            copy.CopyFrom(this);
            return copy;
        }

        public void CopyFrom(PlannerTicket other)
        {
            id = other.id;
            name = other.name;
            categoryId = other.categoryId;
            colorHex = other.colorHex;
            time = other.time;
            creator = other.creator;
            created = other.created;
            order = other.order;
            assignees.Clear();
            assignees.AddRange(other.assignees);
            attachments.Clear();
            attachments.AddRange(other.attachments);
            follows = other.follows;
            extra.Clear();
            extra.AddRange(other.extra);
        }
    }

    // Wall-clock start and exclusive end; an all-day range runs midnight to midnight.
    public readonly record struct TimeRange(DateTime start, DateTime end, bool allDay, string? timeZone)
    {
        public static TimeRange Days(DateOnly first, DateOnly last) =>
            new TimeRange(first.ToDateTime(TimeOnly.MinValue), last.AddDays(1).ToDateTime(TimeOnly.MinValue), true, null);
    }
}
