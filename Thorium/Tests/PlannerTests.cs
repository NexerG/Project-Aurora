using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Testing;
using ArctisAurora.Core.UI;
using ArctisAurora.Core.Users;
using ArctisAurora.EngineWork;
using System.Numerics;
using System.Xml.Linq;

namespace Thorium.Tests
{
    internal static class PlannerTests
    {
        [A_XSDActionDependency("Planner.XmlRoundTrip", "Test")]
        private static IEnumerator<int> XmlRoundTrip(TestContext t)
        {
            XElement source = XElement.Parse(
                "<Planner Name=\"Launch\" NameWidth=\"240\">" +
                "<Category Id=\"11111111-1111-1111-1111-111111111111\" Name=\"Design\" Color=\"#E5484D\" Width=\"300\" />" +
                "<Category Id=\"22222222-2222-2222-2222-222222222222\" Name=\"Build\" Color=\"#30A46C\" />" +
                "<Ticket Id=\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\" Name=\"Sketches\" Category=\"11111111-1111-1111-1111-111111111111\" " +
                "Start=\"2026-10-05\" End=\"2026-10-08\" AllDay=\"true\" Creator=\"Grexen\" Created=\"2026-10-01T09:30:00\" />" +
                "<Ticket Id=\"bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb\" Name=\"Review\" Category=\"22222222-2222-2222-2222-222222222222\" Color=\"#FFB224\" " +
                "Start=\"2026-10-09T14:00\" End=\"2026-10-09T16:30\" TimeZone=\"Europe/Vilnius\" Creator=\"Ada\" Order=\"2\" Follows=\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\">" +
                "<Assignee User=\"Grexen\" /><Assignee User=\"Ada\" />" +
                "<Attachment Kind=\"Travel\" Side=\"Before\" Minutes=\"30\" /><Attachment Kind=\"Prep\" Side=\"After\" Minutes=\"90\" /><Checklist /></Ticket>" +
                "<Milestone At=\"2026-11-01\" />" +
                "</Planner>");

            PlannerDocument planner = PlannerXml.Parse(source);
            PlannerTicket sketches = planner.tickets[0];
            PlannerTicket review = planner.tickets[1];

            t.Check(XNode.DeepEquals(source, PlannerXml.ToXml(planner)), $"the planner writes back as it was read: {PlannerXml.ToXml(planner)}");
            t.Check(planner.categories.Count == 2 && planner.tickets.Count == 2, "both categories and both tickets are read");
            t.Check(planner.nameWidth == 240f && planner.categories[0].width == 300f && planner.categories[1].width == PlannerCategory.defaultWidth, "the name column and board column widths are read");
            t.Check(planner.extra.Count == 1 && review.extra.Count == 1, "elements the reader does not know are kept");
            t.Check(sketches.time.allDay && sketches.time.end - sketches.time.start == TimeSpan.FromDays(3), "an all-day ticket ends at midnight after its last day");
            t.Check(!review.time.allDay && review.time.end - review.time.start == TimeSpan.FromMinutes(150), "a timed ticket keeps its hours and minutes");
            t.Check(ReferenceEquals(sketches.creator, User.current), "the current user's name reads back as the current user");
            t.Check(review.assignees.Count == 2 && review.assignees[1].name == "Ada", "assignees are read in order");
            t.Check(planner.ColorOf(sketches) == "#E5484D" && planner.ColorOf(review) == "#FFB224", "a ticket without a colour takes its category's");
            t.Check(planner.TicketsIn(planner.categories[1]).Single() == review, "a ticket is filed under its category");
            t.Check(review.follows == sketches.id && planner.Followers(sketches).Single() == review, "a ticket's leader is read");
            t.Check(review.OuterStart == new DateTime(2026, 10, 9, 13, 30, 0) && review.OuterEnd == new DateTime(2026, 10, 9, 18, 0, 0), $"attachments widen a ticket's time: {review.OuterStart} - {review.OuterEnd}");

            PlannerDocument bare = PlannerXml.Parse(XElement.Parse("<Planner><Ticket Name=\"No dates\" /></Planner>"));
            t.Check(bare.categories.Count == 1 && bare.tickets.Count == 0 && bare.extra.Count == 1, "a planner always has a category; a ticket without dates is kept unread");
            yield break;
        }

        [A_XSDActionDependency("Planner.GanttGeometry", "Test")]
        private static IEnumerator<int> GanttGeometry(TestContext t)
        {
            PlannerDocument planner = PlannerDocument.Blank("Geometry");
            PlannerTicket ticket = new PlannerTicket
            {
                name = "Three days",
                categoryId = planner.categories[0].id,
                time = TimeRange.Days(new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 7))
            };
            planner.tickets.Add(ticket);

            PlannerEditorControl editor = Show(t, planner);
            yield return 2;

            GanttChartControl chart = editor.chart!;
            LayoutRect bar = chart.BarRect(ticket);
            t.Check(MathF.Abs(bar.width - 96f) < 0.01f, $"a three-day bar is three days wide at Day zoom: {bar.width}");
            t.Check(MathF.Abs(bar.x - chart.X(ticket.time.start)) < 0.01f, "a bar starts at its start time");
            t.Check(chart.RowOf(ticket) == 1 && bar.y > chart.RowTop(1) && bar.Bottom < chart.RowTop(2), "a ticket's bar sits in the row under its category");

            DateTime noon = new DateTime(2026, 10, 6, 12, 0, 0);
            t.Check(MathF.Abs((float)(chart.TimeAt(chart.X(noon)) - noon).TotalMinutes) < 1f, "a time maps to a position and back");

            editor.SetZoom(PlannerZoom.Week);
            yield return 2;
            t.Check(MathF.Abs(chart.BarRect(ticket).width - 30f) < 0.01f, $"the bar narrows with the zoom: {chart.BarRect(ticket).width}");
            t.Check(chart.origin <= ticket.time.start && chart.end >= ticket.time.end, "the chart spans every ticket");
        }

        [A_XSDActionDependency("Planner.BoardColumns", "Test")]
        private static IEnumerator<int> BoardColumns(TestContext t)
        {
            PlannerDocument planner = PlannerDocument.Blank("Board");
            planner.categories.Add(new PlannerCategory { name = "Done", colorHex = "#30A46C" });
            for (int i = 0; i < 3; i++)
                planner.tickets.Add(new PlannerTicket
                {
                    name = $"Ticket {i}",
                    categoryId = planner.categories[i == 2 ? 1 : 0].id,
                    time = TimeRange.Days(new DateOnly(2026, 10, 5 + i), new DateOnly(2026, 10, 5 + i)),
                    creator = User.current,
                    order = 2 - i
                });

            PlannerEditorControl editor = Show(t, planner);
            editor.ShowView(PlannerView.Board);
            yield return 2;

            t.Check(editor.chart == null && editor.board != null, "the board replaces the chart");
            List<Control> columns = editor.board!.children.OfType<Control>().ToList();
            t.Check(columns.Count == 2, "one column per category");
            t.Check(columns[0].children.Count == 3 && columns[1].children.Count == 2, "each column holds its heading and its cards");
            t.Check(planner.TicketsIn(planner.categories[0]).First().name == "Ticket 1", "cards follow board order");
            t.Check(PlannerBoardControl.Dates(planner.tickets[0].time) == "5 Oct 2026", "a one-day ticket shows one date");
        }

        [A_XSDActionDependency("Planner.ViewState", "Test")]
        private static IEnumerator<int> ViewState(TestContext t)
        {
            PlannerEditorControl editor = Show(t, PlannerDocument.Blank("View"));
            editor.SetZoom(PlannerZoom.Week);
            editor.ShowView(PlannerView.Board);
            yield return 2;

            SessionTab saved = editor.ViewState();
            PlannerEditorControl restored = Show(t, PlannerDocument.Blank("View"));
            restored.RestoreView(saved);
            yield return 2;
            t.Check(restored.view == PlannerView.Board && restored.zoom == PlannerZoom.Week, "a restored tab shows the view and zoom it was saved with");

            restored.ShowView(PlannerView.Calendar);
            restored.ShowSpan(CalendarSpan.Month);
            restored.Step(1);
            yield return 2;
            saved = restored.ViewState();
            PlannerEditorControl calendar = Show(t, PlannerDocument.Blank("View"));
            calendar.RestoreView(saved);
            yield return 2;
            t.Check(calendar.view == PlannerView.Calendar && calendar.span == CalendarSpan.Month && calendar.anchor == new DateOnly(2026, 11, 6),
                $"a restored calendar shows the span and month it was saved with: {calendar.span} {calendar.anchor}");
        }

        [A_XSDActionDependency("Planner.DragMoveUndo", "Test")]
        private static IEnumerator<int> DragMoveUndo(TestContext t)
        {
            (PlannerEditorControl editor, PlannerTicket ticket) = ShowOne(t);
            yield return 2;

            GanttChartControl chart = editor.chart!;
            LayoutRect bar = chart.BarRect(ticket);
            Vector2 at = new Vector2(bar.x + bar.width * 0.5f, bar.y + bar.height * 0.5f);
            yield return t.Drag(chart, at, at + new Vector2(64f, 0f), 8);
            yield return 2;

            t.Check(ticket.time.start == new DateTime(2026, 10, 7) && ticket.time.end == new DateTime(2026, 10, 10), $"a two-day drag moves the ticket two days: {ticket.time}");
            t.Check(ticket.time.allDay, "a moved all-day ticket stays all day");
            t.Check(ReferenceEquals(editor.selected, ticket) && editor.unsaved, "the dragged ticket is selected and the planner is unsaved");

            editor.Undo();
            t.Check(ticket.time.start == new DateTime(2026, 10, 5) && ticket.time.end == new DateTime(2026, 10, 8), $"one undo puts the whole drag back: {ticket.time}");
            editor.Redo();
            t.Check(ticket.time.start == new DateTime(2026, 10, 7), "redo moves it again");
        }

        [A_XSDActionDependency("Planner.DragResize", "Test")]
        private static IEnumerator<int> DragResize(TestContext t)
        {
            (PlannerEditorControl editor, PlannerTicket ticket) = ShowOne(t);
            yield return 2;

            GanttChartControl chart = editor.chart!;
            LayoutRect bar = chart.BarRect(ticket);
            float middle = bar.y + bar.height * 0.5f;
            yield return t.Drag(chart, new Vector2(bar.Right - 2f, middle), new Vector2(bar.Right + 30f, middle), 8);
            yield return 2;
            t.Check(ticket.time.start == new DateTime(2026, 10, 5) && ticket.time.end == new DateTime(2026, 10, 9), $"dragging the end edge a day longer moves only the end: {ticket.time}");

            bar = chart.BarRect(ticket);
            yield return t.Drag(chart, new Vector2(bar.x + 2f, middle), new Vector2(bar.x + 300f, middle), 8);
            yield return 2;
            t.Check(ticket.time.start == new DateTime(2026, 10, 8) && ticket.time.end == new DateTime(2026, 10, 9), $"the start edge stops a day before the end: {ticket.time}");

            editor.Undo();
            t.Check(ticket.time.start == new DateTime(2026, 10, 5) && ticket.time.end == new DateTime(2026, 10, 9), "each resize is its own undo step");
        }

        [A_XSDActionDependency("Planner.EditAddDelete", "Test")]
        private static IEnumerator<int> EditAddDelete(TestContext t)
        {
            (PlannerEditorControl editor, PlannerTicket ticket) = ShowOne(t);
            yield return 2;

            PlannerTicket draft = ticket.Clone();
            draft.name = "Renamed";
            draft.colorHex = "#FFB224";
            draft.assignees.Add(User.current);
            editor.Apply(ticket, draft);
            t.Check(ticket.name == "Renamed" && ticket.colorHex == "#FFB224" && ticket.assignees.Count == 1, "an applied draft is written over the ticket");
            editor.Undo();
            t.Check(ticket.name == "Three days" && ticket.colorHex == null && ticket.assignees.Count == 0, "one undo restores every field");

            PlannerTicket added = new PlannerTicket { name = "Added", categoryId = editor.document.categories[0].id, time = ticket.time, creator = User.current };
            editor.Apply(null, added);
            t.Check(editor.document.tickets.Count == 2 && ReferenceEquals(editor.selected, added), "a new ticket is added and selected");
            yield return 2;
            t.Check(editor.chart!.RowOf(added) == 2, "the chart shows the added ticket's row");

            editor.DeleteSelected();
            t.Check(editor.document.tickets.Count == 1 && editor.selected == null, "deleting removes the selected ticket");
            editor.Undo();
            t.Check(editor.document.tickets.Count == 2 && editor.document.tickets[1] == added, "undo brings it back in its place");
            editor.Undo();
            t.Check(editor.document.tickets.Count == 1, "undo of the add removes it again");
        }

        [A_XSDActionDependency("Planner.DeleteKeys", "Test")]
        private static IEnumerator<int> DeleteKeys(TestContext t)
        {
            (PlannerEditorControl editor, PlannerTicket ticket) = ShowOne(t);
            yield return 2;

            LayoutRect bar = editor.chart!.BarRect(ticket);
            yield return t.Click(editor.chart, new Vector2(bar.x + bar.width * 0.5f, bar.y + bar.height * 0.5f));
            t.Check(ReferenceEquals(editor.selected, ticket), "a click selects the bar under it");

            yield return t.Key(Keys.Delete);
            t.Check(editor.document.tickets.Count == 0, "Delete removes the selected ticket");
            yield return t.Key(Keys.Z, Keys.LeftControl);
            t.Check(editor.document.tickets.Count == 1, "Ctrl+Z brings it back");
        }

        [A_XSDActionDependency("Planner.SaveKey", "Test")]
        private static IEnumerator<int> SaveKey(TestContext t)
        {
            string path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), $"aurora-planner-{Guid.NewGuid():N}{PlannerDocument.extension}"));
            PlannerDocument planner = PlannerDocument.Blank("Saved");
            planner.tickets.Add(new PlannerTicket { name = "Before", categoryId = planner.categories[0].id, time = TimeRange.Days(new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 5)) });
            planner.Save(path);

            PlannerEditorControl editor = new PlannerEditorControl { clock = () => new DateTime(2026, 10, 6, 12, 0, 0) };
            editor.LoadPath(path);
            t.Show(editor);
            yield return 2;

            PlannerTicket ticket = editor.document.tickets[0];
            PlannerTicket draft = ticket.Clone();
            draft.name = "After";
            editor.Apply(ticket, draft);
            LayoutRect bar = editor.chart!.BarRect(ticket);
            yield return t.Click(editor.chart, new Vector2(bar.x + bar.width * 0.5f, bar.y + bar.height * 0.5f));
            t.Check(editor.unsaved, "an edit leaves the planner unsaved");

            yield return t.Key(Keys.S, Keys.LeftControl);
            t.Check(!editor.unsaved && PlannerDocument.Load(path).tickets[0].name == "After", "Ctrl+S writes the planner to its file");
            File.Delete(path);
        }

        [A_XSDActionDependency("Planner.TwoViews", "Test")]
        private static IEnumerator<int> TwoViews(TestContext t)
        {
            string path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), $"aurora-planner-{Guid.NewGuid():N}{PlannerDocument.extension}"));
            PlannerDocument planner = PlannerDocument.Blank("Shared");
            planner.tickets.Add(new PlannerTicket { name = "Before", categoryId = planner.categories[0].id, time = TimeRange.Days(new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 5)) });
            planner.Save(path);

            PlannerEditorControl a = new PlannerEditorControl { widthStar = 1f };
            PlannerEditorControl b = new PlannerEditorControl { widthStar = 1f };
            a.LoadPath(path);
            b.LoadPath(path);
            StackPanelControl both = new StackPanelControl { orientation = StackPanelControl.Orientation.Horizontal };
            both.AddChild(a);
            both.AddChild(b);
            t.Show(both);
            yield return 2;
            t.Check(ReferenceEquals(a.document, b.document), "two views of one planner file share one document");

            PlannerTicket ticket = a.document.tickets[0];
            PlannerTicket draft = ticket.Clone();
            draft.name = "After";
            a.Apply(ticket, draft);
            yield return 2;
            t.Check(b.document.tickets[0].name == "After" && b.unsaved, "an edit in one view shows in the other, which is unsaved too");

            b.Undo();
            yield return 2;
            t.Check(a.document.tickets[0].name == "Before", "undo in the other view reverts it");

            PlannerDocument shared = a.document;
            a.Destroy();
            yield return 2;
            PlannerEditorControl again = new PlannerEditorControl();
            again.LoadPath(path);
            t.Check(ReferenceEquals(again.document, shared), "closing one view keeps the document for the other");

            both.Destroy();
            again.Destroy();
            yield return 2;
            PlannerEditorControl fresh = new PlannerEditorControl();
            fresh.LoadPath(path);
            t.Check(!ReferenceEquals(fresh.document, shared), "closing the last view lets the document go");
            fresh.Destroy();
            yield return 2;
            File.Delete(path);
        }

        [A_XSDActionDependency("Planner.PopupCategory", "Test")]
        private static IEnumerator<int> PopupCategory(TestContext t)
        {
            (PlannerEditorControl editor, PlannerTicket ticket) = ShowOne(t);
            PlannerCategory done = new PlannerCategory { name = "Done", colorHex = "#30A46C" };
            editor.document.categories.Add(done);
            editor.document.Changed();
            yield return 2;

            editor.Select(ticket);
            editor.EditSelected();
            yield return 20;
            DropdownControl? category = Find<DropdownControl>(editor).FirstOrDefault();
            t.Check(category != null && category.selected == "General", "the popup shows the ticket's category");
            if (category == null) yield break;

            yield return t.Click(category);
            yield return 20;
            ContextMenuControl.Row? row = Find<ContextMenuControl.Row>(editor).FirstOrDefault(r => (r.entry as ContextMenuButton)?.text == "Done");
            t.Check(row != null, "the category list opens over the popup");
            if (row == null) yield break;
            yield return t.Click(row);
            yield return 2;

            ButtonControl? apply = Find<ButtonControl>(editor).FirstOrDefault(b => b.children.OfType<LabelControl>().Any(l => l.text == "Apply"));
            t.Check(apply != null, "the popup is still open after the pick");
            if (apply == null) yield break;
            yield return t.Click(apply);
            yield return 2;

            t.Check(ticket.categoryId == done.id && editor.chart!.RowOf(ticket) == 2, "Apply files the ticket under the picked category, in the row under it");
            editor.Undo();
            t.Check(ticket.categoryId == editor.document.categories[0].id, "one undo files it back");
        }

        [A_XSDActionDependency("Planner.MenuMove", "Test")]
        private static IEnumerator<int> MenuMove(TestContext t)
        {
            (PlannerEditorControl editor, PlannerTicket ticket) = ShowOne(t);
            PlannerCategory done = new PlannerCategory { name = "Done", colorHex = "#30A46C" };
            editor.document.categories.Add(done);
            editor.document.Changed();
            yield return 2;

            LayoutRect bar = editor.chart.BarRect(ticket);
            yield return t.Click(editor.chart, new Vector2(bar.x + bar.width * 0.5f, bar.y + bar.height * 0.5f), Keys.MouseRight);
            yield return 20;
            ContextMenuControl.Row? move = Find<ContextMenuControl.Row>(editor).FirstOrDefault(r => (r.entry as ContextMenuSubmenu)?.text == "Move to category");
            t.Check(ReferenceEquals(editor.selected, ticket) && move != null, "a right click selects the bar and offers Move to category");
            if (move == null) yield break;

            yield return t.MoveTo(move);
            yield return 20;
            ContextMenuControl.Row? target = Find<ContextMenuControl.Row>(editor).FirstOrDefault(r => (r.entry as ContextMenuButton)?.text == "Done");
            t.Check(target != null, "the submenu lists the planner's categories");
            if (target == null) yield break;
            yield return t.Click(target);
            yield return 2;

            t.Check(ticket.categoryId == done.id, "picking a category moves the ticket");
            editor.Undo();
            t.Check(ticket.categoryId == editor.document.categories[0].id, "one undo moves it back");
        }

        // Every control of a type in the editor's window, menus included.
        private static List<T> Find<T>(Control within) where T : Control
        {
            List<T> found = new List<T>();
            Collect(UIEngine.WindowOf(within)!.ui.uiRoot, found);
            return found;
        }

        private static void Collect<T>(Entity entity, List<T> found) where T : Control
        {
            if (entity is T match) found.Add(match);
            foreach (Entity child in entity.children)
                Collect(child, found);
        }

        [A_XSDActionDependency("Planner.BoardDragAcross", "Test")]
        private static IEnumerator<int> BoardDragAcross(TestContext t)
        {
            PlannerDocument planner = BoardFixture(out PlannerCategory general, out PlannerCategory done);
            PlannerEditorControl editor = Show(t, planner);
            editor.ShowView(PlannerView.Board);
            yield return 2;

            PlannerBoardControl board = editor.board!;
            PlannerBoardControl.TicketCard a = board.CardsIn(general)[0];
            LayoutRect c = board.CardsIn(done)[0].arrangedRect;
            yield return t.Drag(a, Centre(a.arrangedRect), new Vector2(c.x + c.width * 0.5f, c.Bottom + 12f), 8);
            yield return 2;

            PlannerTicket moved = planner.tickets[0];
            t.Check(moved.categoryId == done.id, "a card dropped in another column moves to its category");
            t.Check(planner.TicketsIn(done).Select(x => x.name).SequenceEqual(new[] { "C", "A" }), "dropped below the last card, it goes last");
            t.Check(editor.board!.CardsIn(done).Count == 2 && editor.board.CardsIn(general).Count == 1, "the board shows the move");

            editor.Undo();
            t.Check(moved.categoryId == general.id && planner.TicketsIn(general).First() == moved, "one undo puts the card back where it was");
        }

        [A_XSDActionDependency("Planner.BoardDragWithin", "Test")]
        private static IEnumerator<int> BoardDragWithin(TestContext t)
        {
            PlannerDocument planner = BoardFixture(out PlannerCategory general, out _);
            planner.tickets.Add(new PlannerTicket { name = "D", categoryId = general.id, time = planner.tickets[0].time, order = 2 });
            PlannerEditorControl editor = Show(t, planner);
            editor.ShowView(PlannerView.Board);
            yield return 2;

            PlannerBoardControl board = editor.board!;
            PlannerBoardControl.TicketCard a = board.CardsIn(general)[0];
            LayoutRect last = board.CardsIn(general)[2].arrangedRect;
            yield return t.Drag(a, Centre(a.arrangedRect), new Vector2(last.x + last.width * 0.5f, last.Bottom + 40f), 8);
            yield return 2;

            t.Check(planner.TicketsIn(general).Select(x => x.name).SequenceEqual(new[] { "B", "D", "A" }), "a card dragged below the last one goes last");
            editor.Undo();
            t.Check(planner.TicketsIn(general).Select(x => x.name).SequenceEqual(new[] { "A", "B", "D" }), "one undo restores the whole order");
        }

        [A_XSDActionDependency("Planner.BoardGolden", "Test")]
        private static IEnumerator<int> BoardGolden(TestContext t)
        {
            PlannerDocument planner = BoardFixture(out PlannerCategory general, out PlannerCategory done);
            PlannerEditorControl editor = Show(t, planner);
            editor.ShowView(PlannerView.Board);
            yield return 4;
            PlannerBoardControl board = editor.board!;
            yield return t.Golden("Board", board);

            PlannerBoardControl.TicketCard a = board.CardsIn(general)[0];
            LayoutRect c = board.CardsIn(done)[0].arrangedRect;
            board.DraggingOver(a, new Vector2(c.x + c.width * 0.5f, c.y + 4f));
            yield return 4;
            yield return t.Golden("Marker", board);
            board.DraggingOverEnd(a);
        }

        [A_XSDActionDependency("Planner.CategoryEdits", "Test")]
        private static IEnumerator<int> CategoryEdits(TestContext t)
        {
            PlannerDocument planner = BoardFixture(out PlannerCategory general, out PlannerCategory done);
            PlannerEditorControl editor = Show(t, planner);
            yield return 2;

            editor.ApplyCategory(null, "Later", "#E5484D");
            t.Check(planner.categories.Count == 3 && planner.categories[2].name == "Later", "a new category is added last");
            editor.Undo();
            t.Check(planner.categories.Count == 2, "undo removes it");

            editor.ApplyCategory(general, "Ideas", "#FFB224");
            t.Check(general.name == "Ideas" && general.colorHex == "#FFB224" && planner.ColorOf(planner.tickets[0]) == "#FFB224", "a renamed, recoloured category recolours its tickets");
            editor.Undo();
            t.Check(general.name == "General" && general.colorHex == PlannerDocument.defaultColor, "undo restores name and colour");

            PlannerTicket c = planner.tickets[2];
            editor.DeleteCategory(done);
            yield return 2;
            t.Check(!planner.categories.Contains(done) && planner.TicketsIn(general).Contains(c), "a deleted category's tickets show under the first category");
            t.Check(editor.chart!.RowOf(c) > 0, "the chart still shows them");
            editor.Undo();
            t.Check(planner.categories[1] == done && planner.TicketsIn(done).Single() == c, "undo brings the category back with its tickets");

            editor.DeleteCategory(done);
            editor.DeleteCategory(general);
            t.Check(planner.categories.Count == 1, "the last category cannot be deleted");
        }

        [A_XSDActionDependency("Planner.CategoryPopup", "Test")]
        private static IEnumerator<int> CategoryPopup(TestContext t)
        {
            PlannerDocument planner = BoardFixture(out PlannerCategory general, out PlannerCategory done);
            PlannerEditorControl editor = Show(t, planner);
            yield return 2;

            GanttChartControl chart = editor.chart!;
            Vector2 row = new Vector2(chart.arrangedRect.x + 300f, chart.RowTop(0) + GanttChartControl.rowHeight * 0.5f);
            yield return t.Click(chart, row);
            yield return t.Click(chart, row);
            yield return 20;
            TextBoxControl? name = Find<TextBoxControl>(editor).FirstOrDefault(box => box.text == "General");
            t.Check(name != null, "a double click on a category row opens its popup");
            if (name == null) yield break;

            name.text = "Ideas";
            yield return t.Click(ButtonNamed(editor, "Apply")!);
            yield return 2;
            t.Check(general.name == "Ideas", "Apply renames the category");
            editor.Undo();
            t.Check(general.name == "General", "one undo puts the name back");

            editor.ShowView(PlannerView.Board);
            yield return 30;
            Control column = editor.board!.children.OfType<Control>().ElementAt(1);
            Vector2 heading = new Vector2(column.arrangedRect.x + column.arrangedRect.width * 0.5f, column.arrangedRect.y + 16f);
            yield return t.Click(column, heading);
            yield return t.Click(column, heading);
            yield return 20;
            ButtonControl? delete = ButtonNamed(editor, "Delete");
            t.Check(delete != null && ReferenceEquals(editor.pickedCategory, done), "a double click on a board column opens its category's popup");
            if (delete == null) yield break;

            yield return t.Click(delete);
            yield return 2;
            t.Check(!planner.categories.Contains(done) && editor.board!.CardsIn(general).Count == 3, "Delete removes the category and its card joins the first column");
            editor.Undo();
        }

        [A_XSDActionDependency("Planner.AssigneeGolden", "Test")]
        private static IEnumerator<int> AssigneeGolden(TestContext t)
        {
            PlannerDocument planner = BoardFixture(out _, out _);
            planner.tickets[0].assignees.Add(User.current);
            planner.tickets[0].assignees.Add(User.Named("Ada Lovelace"));
            planner.tickets[2].assignees.Add(User.Named("Bo"));
            PlannerEditorControl editor = Show(t, planner);
            editor.ShowView(PlannerView.Board);
            yield return 4;

            t.Check(User.current.initials == "GR" && User.Named("Ada Lovelace").initials == "AL", "initials are the first two letters of one word, or the first letters of two");
            t.Check(Find<LabelControl>(editor).Count(label => label.text is "GR" or "AL" or "BO") == 3, "each assignee shows as a chip of initials");
            yield return t.Golden("Board", editor.board!);
        }

        [A_XSDActionDependency("Planner.CalendarPack", "Test")]
        private static IEnumerator<int> CalendarPack(TestContext t)
        {
            DateTime day = new DateTime(2026, 10, 6);
            List<(DateTime, DateTime)> spans = new List<(DateTime, DateTime)>
            {
                (day.AddHours(9), day.AddHours(11)),
                (day.AddHours(10), day.AddHours(12)),
                (day.AddHours(10.5), day.AddHours(11)),
                (day.AddHours(13), day.AddHours(14)),
                (day.AddHours(11), day.AddHours(11.5))
            };
            (int column, int columns)[] packed = CalendarControl.Pack(spans);
            t.Check(packed[0] == (0, 3) && packed[1] == (1, 3) && packed[2] == (2, 3), $"three overlapping spans take three columns: {string.Join(" ", packed)}");
            t.Check(packed[4] == (0, 3), "a span that starts as one ends takes its freed column, in the same cluster");
            t.Check(packed[3] == (0, 1), "a span clear of the others has the day to itself");
            yield break;
        }

        [A_XSDActionDependency("Planner.CalendarGeometry", "Test")]
        private static IEnumerator<int> CalendarGeometry(TestContext t)
        {
            PlannerDocument planner = PlannerDocument.Blank("Calendar");
            Guid category = planner.categories[0].id;
            PlannerTicket meeting = Timed(planner, "Meeting", new DateTime(2026, 10, 6, 9, 0, 0), 90);
            PlannerTicket night = Timed(planner, "Night", new DateTime(2026, 10, 7, 22, 0, 0), 240);
            PlannerTicket trip = new PlannerTicket { name = "Trip", categoryId = category, time = TimeRange.Days(new DateOnly(2026, 10, 8), new DateOnly(2026, 10, 9)) };
            planner.tickets.Add(trip);
            PlannerEditorControl editor = Show(t, planner);
            editor.ShowView(PlannerView.Calendar);
            yield return 3;

            CalendarControl calendar = editor.calendar!;
            t.Check(calendar.first == new DateOnly(2026, 10, 5), $"the week starts on Monday: {calendar.first}");
            LayoutRect block = calendar.BlockRect(meeting)!.Value;
            Vector2 nine = calendar.PointAt(meeting.time.start);
            t.Check(MathF.Abs(block.y - nine.Y) < 0.5f && MathF.Abs(block.height - CalendarControl.hourHeight * 1.5f) < 0.5f, $"a timed ticket's block runs from its start for its length: {block.y} {block.height}");
            t.Check(calendar.TicketAt(calendar.PointAt(new DateTime(2026, 10, 8, 1, 0, 0))) == null, "a block scrolled under the pinned header is not hit");
            editor.scroller.SetScrollOffset(Vector2.Zero);
            yield return 2;
            t.Check(ReferenceEquals(calendar.TicketAt(calendar.PointAt(new DateTime(2026, 10, 7, 23, 0, 0))), night)
                && ReferenceEquals(calendar.TicketAt(calendar.PointAt(new DateTime(2026, 10, 8, 1, 0, 0))), night), "a ticket over midnight shows in both days");
            LayoutRect strip = calendar.BlockRect(trip)!.Value;
            t.Check(strip.y < editor.scroller.arrangedRect.y + calendar.TopHeight && strip.width > calendar.arrangedRect.width / 4f, $"an all-day ticket lies across its days in the strip: {strip}");

            editor.ShowSpan(CalendarSpan.Day);
            yield return 2;
            t.Check(calendar.first == new DateOnly(2026, 10, 6) && calendar.BlockRect(night) == null, "Day shows today alone");
            editor.Step(1);
            editor.ShowSpan(CalendarSpan.Week);
            editor.Step(1);
            t.Check(calendar.first == new DateOnly(2026, 10, 12), $"stepping moves by the span: {calendar.first}");
            editor.GoToday();
            t.Check(calendar.first == new DateOnly(2026, 10, 5), "Today goes back to this week");
        }

        [A_XSDActionDependency("Planner.CalendarDragUndo", "Test")]
        private static IEnumerator<int> CalendarDragUndo(TestContext t)
        {
            PlannerDocument planner = PlannerDocument.Blank("Calendar");
            PlannerTicket meeting = Timed(planner, "Meeting", new DateTime(2026, 10, 6, 9, 0, 0), 60);
            PlannerEditorControl editor = Show(t, planner);
            editor.ShowView(PlannerView.Calendar);
            yield return 3;

            CalendarControl calendar = editor.calendar!;
            Vector2 from = calendar.PointAt(new DateTime(2026, 10, 6, 9, 30, 0));
            Vector2 to = calendar.PointAt(new DateTime(2026, 10, 7, 11, 35, 0));
            yield return t.Drag(calendar, from, to, 8);
            yield return 2;
            t.Check(meeting.time.start == new DateTime(2026, 10, 7, 11, 0, 0) && meeting.time.end == new DateTime(2026, 10, 7, 12, 0, 0),
                $"a drag moves the block across days in 15-minute steps: {meeting.time}");

            LayoutRect block = calendar.BlockRect(meeting)!.Value;
            Vector2 edge = new Vector2(block.x + block.width * 0.5f, block.Bottom - 2f);
            yield return t.Drag(calendar, edge, edge + new Vector2(0f, CalendarControl.hourHeight), 8);
            yield return 2;
            t.Check(meeting.time.start == new DateTime(2026, 10, 7, 11, 0, 0) && meeting.time.end == new DateTime(2026, 10, 7, 13, 0, 0), $"the bottom edge resizes the end: {meeting.time}");

            editor.Undo();
            t.Check(meeting.time.end == new DateTime(2026, 10, 7, 12, 0, 0), "each drag is one undo step");
            editor.Undo();
            t.Check(meeting.time.start == new DateTime(2026, 10, 6, 9, 0, 0), "undo puts the move back");
        }

        [A_XSDActionDependency("Planner.CalendarCreate", "Test")]
        private static IEnumerator<int> CalendarCreate(TestContext t)
        {
            PlannerEditorControl editor = Show(t, PlannerDocument.Blank("Calendar"));
            editor.ShowView(PlannerView.Calendar);
            DateTime clear = DateTime.Now.AddMilliseconds(300);
            while (DateTime.Now < clear)
                yield return 1;

            CalendarControl calendar = editor.calendar!;
            Vector2 slot = calendar.PointAt(new DateTime(2026, 10, 8, 7, 10, 0));
            yield return t.Click(calendar, slot);
            yield return t.Click(calendar, slot);
            yield return 20;
            t.Check(Find<TextBoxControl>(editor).Any(box => box.text == "2026-10-08 07:00") && Find<TextBoxControl>(editor).Any(box => box.text == "2026-10-08 08:00"),
                "a double click on an empty slot opens a new ticket over the hour from that slot");
            ButtonControl? apply = ButtonNamed(editor, "Apply");
            if (apply == null)
            {
                ContextMenus.Close();
                yield break;
            }
            yield return t.Click(apply);
            yield return 2;

            PlannerTicket? added = editor.document.tickets.SingleOrDefault();
            t.Check(added != null && added.time == new TimeRange(new DateTime(2026, 10, 8, 7, 0, 0), new DateTime(2026, 10, 8, 8, 0, 0), false, null),
                $"Apply adds it at that time: {added?.time}");
        }

        [A_XSDActionDependency("Planner.CalendarMonth", "Test")]
        private static IEnumerator<int> CalendarMonth(TestContext t)
        {
            PlannerDocument planner = PlannerDocument.Blank("Calendar");
            PlannerTicket meeting = Timed(planner, "Meeting", new DateTime(2026, 10, 6, 9, 0, 0), 60);
            PlannerEditorControl editor = Show(t, planner);
            editor.ShowView(PlannerView.Calendar);
            editor.ShowSpan(CalendarSpan.Month);
            yield return 3;

            CalendarControl calendar = editor.calendar!;
            t.Check(calendar.first == new DateOnly(2026, 9, 28), $"the month grid starts on the Monday on or before the 1st: {calendar.first}");
            LayoutRect chip = calendar.BlockRect(meeting)!.Value;
            yield return t.Drag(calendar, Centre(chip), Centre(chip) + new Vector2(calendar.arrangedRect.width / 7f * 2f, 0f), 8);
            yield return 2;
            t.Check(meeting.time.start == new DateTime(2026, 10, 8, 9, 0, 0), $"a chip dragged two cells moves two days and keeps its time: {meeting.time}");
            editor.Undo();
            t.Check(meeting.time.start == new DateTime(2026, 10, 6, 9, 0, 0), "one undo moves it back");
        }

        [A_XSDActionDependency("Planner.CalendarGolden", "Test")]
        private static IEnumerator<int> CalendarGolden(TestContext t)
        {
            PlannerDocument planner = PlannerDocument.Blank("Calendar");
            planner.categories.Add(new PlannerCategory { name = "Build", colorHex = "#30A46C" });
            Timed(planner, "Standup", new DateTime(2026, 10, 5, 9, 0, 0), 30);
            Timed(planner, "Review", new DateTime(2026, 10, 6, 10, 0, 0), 120);
            Timed(planner, "Pairing", new DateTime(2026, 10, 6, 11, 0, 0), 90).categoryId = planner.categories[1].id;
            Timed(planner, "Lunch", new DateTime(2026, 10, 6, 12, 0, 0), 60).colorHex = "#FFB224";
            planner.tickets.Add(new PlannerTicket { name = "Offsite", categoryId = planner.categories[1].id, time = TimeRange.Days(new DateOnly(2026, 10, 8), new DateOnly(2026, 10, 9)) });
            PlannerEditorControl editor = Show(t, planner);
            editor.ShowView(PlannerView.Calendar);
            editor.Select(planner.tickets[1]);
            yield return 4;
            yield return t.Golden("Week", editor.calendar!);

            editor.ShowSpan(CalendarSpan.Month);
            yield return 4;
            yield return t.Golden("Month", editor.calendar!);
        }

        [A_XSDActionDependency("Planner.FollowersMove", "Test")]
        private static IEnumerator<int> FollowersMove(TestContext t)
        {
            PlannerDocument planner = PlannerDocument.Blank("Links");
            Guid category = planner.categories[0].id;
            PlannerTicket a = new PlannerTicket { name = "A", categoryId = category, time = TimeRange.Days(new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 6)), order = 0 };
            PlannerTicket b = new PlannerTicket { name = "B", categoryId = category, time = TimeRange.Days(new DateOnly(2026, 10, 7), new DateOnly(2026, 10, 7)), order = 1, follows = a.id };
            PlannerTicket c = new PlannerTicket { name = "C", categoryId = category, time = TimeRange.Days(new DateOnly(2026, 10, 8), new DateOnly(2026, 10, 8)), order = 2, follows = b.id };
            planner.tickets.AddRange(new[] { a, b, c });
            PlannerEditorControl editor = Show(t, planner);
            yield return 2;

            GanttChartControl chart = editor.chart!;
            Vector2 middle = Centre(chart.BarRect(a));
            yield return t.Drag(chart, middle, middle + new Vector2(64f, 0f), 8);
            yield return 2;
            t.Check(a.time.start == new DateTime(2026, 10, 7) && b.time.start == new DateTime(2026, 10, 9) && c.time.start == new DateTime(2026, 10, 10),
                $"moving a ticket moves everything that follows it: {a.time.start:d} {b.time.start:d} {c.time.start:d}");
            editor.Undo();
            t.Check(a.time.start == new DateTime(2026, 10, 5) && b.time.start == new DateTime(2026, 10, 7) && c.time.start == new DateTime(2026, 10, 8), "one undo puts all three back");
            yield return 2;

            LayoutRect bar = chart.BarRect(a);
            float y = bar.y + bar.height * 0.5f;
            yield return t.Drag(chart, new Vector2(bar.x + 2f, y), new Vector2(bar.x + 34f, y), 8);
            yield return 2;
            t.Check(a.time.start == new DateTime(2026, 10, 6) && b.time.start == new DateTime(2026, 10, 7), "resizing only the start leaves the followers");

            PlannerTicket draft = a.Clone();
            draft.time = a.time with { end = a.time.end.AddDays(1) };
            editor.Apply(a, draft);
            t.Check(b.time.start == new DateTime(2026, 10, 8) && c.time.start == new DateTime(2026, 10, 9), "a later end pushes the followers by as much");
            editor.Undo();
            t.Check(b.time.start == new DateTime(2026, 10, 7) && c.time.start == new DateTime(2026, 10, 8), "and one undo pulls them back");
        }

        [A_XSDActionDependency("Planner.FollowCycle", "Test")]
        private static IEnumerator<int> FollowCycle(TestContext t)
        {
            PlannerDocument planner = PlannerDocument.Blank("Links");
            Guid category = planner.categories[0].id;
            TimeRange days = TimeRange.Days(new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 5));
            PlannerTicket a = new PlannerTicket { name = "A", categoryId = category, time = days, order = 0 };
            PlannerTicket b = new PlannerTicket { name = "B", categoryId = category, time = days, order = 1, follows = a.id };
            PlannerTicket c = new PlannerTicket { name = "C", categoryId = category, time = days, order = 2, follows = b.id };
            planner.tickets.AddRange(new[] { a, b, c });
            PlannerEditorControl editor = Show(t, planner);
            yield return 2;

            t.Check(planner.Upstream(a, c) && !planner.Upstream(c, a), "a ticket is upstream of what follows it through others");
            editor.Select(a);
            editor.EditSelected();
            yield return 20;
            DropdownControl? follows = Find<DropdownControl>(editor).FirstOrDefault(d => d.options.Contains("(none)"));
            t.Check(follows != null && follows.options.SequenceEqual(new[] { "(none)" }), $"a ticket cannot follow what follows it: {string.Join(", ", follows?.options ?? new List<string>())}");
            yield return t.Key(Keys.Escape);
            yield return 4;

            editor.Select(c);
            editor.EditSelected();
            yield return 20;
            follows = Find<DropdownControl>(editor).FirstOrDefault(d => d.options.Contains("(none)"));
            t.Check(follows != null && follows.selected == "B" && follows.options.SequenceEqual(new[] { "(none)", "A", "B" }), "the last one may follow any other, and shows its leader");
            yield return t.Key(Keys.Escape);
            yield return 4;
        }

        [A_XSDActionDependency("Planner.AttachmentText", "Test")]
        private static IEnumerator<int> AttachmentText(TestContext t)
        {
            t.Check(PlannerAttachmentKinds.Find("Travel") != null && PlannerAttachmentKinds.Find("prep") != null, "Travel and Prep are loaded from the engine's kinds");
            (PlannerEditorControl editor, PlannerTicket ticket) = ShowOne(t);
            yield return 2;

            editor.Select(ticket);
            editor.EditSelected();
            yield return 20;
            List<TextBoxControl> boxes = PopupBoxes(editor, ticket.name);
            t.Check(boxes.Count == 6, $"the popup has name, start, end, assignees, before and after boxes: {boxes.Count}");
            if (boxes.Count != 6) yield break;

            boxes[4].text = "Travel 45m, Prep";
            boxes[5].text = "travel 1h30, Bogus 10";
            yield return t.Key(Keys.Enter);
            yield return 2;
            t.Check(ticket.attachments.SequenceEqual(new[]
                {
                    new PlannerAttachment("Travel", true, TimeSpan.FromMinutes(45)),
                    new PlannerAttachment("Prep", true, TimeSpan.FromMinutes(15)),
                    new PlannerAttachment("Travel", false, TimeSpan.FromMinutes(90))
                }), $"lengths are read, a bare kind takes its default and an unknown kind is dropped: {string.Join(", ", ticket.attachments)}");

            editor.EditSelected();
            yield return 20;
            boxes = PopupBoxes(editor, ticket.name);
            t.Check(boxes.Count == 6 && boxes[4].text == "Travel 45m, Prep 15m" && boxes[5].text == "Travel 1h30m", "the popup shows them back");
            yield return t.Key(Keys.Escape);
            yield return 4;
            editor.Undo();
            t.Check(ticket.attachments.Count == 0, "one undo removes them");
        }

        [A_XSDActionDependency("Planner.AttachmentGolden", "Test")]
        private static IEnumerator<int> AttachmentGolden(TestContext t)
        {
            PlannerDocument planner = PlannerDocument.Blank("Attachments");
            Guid category = planner.categories[0].id;
            PlannerTicket visit = new PlannerTicket { name = "Visit", categoryId = category, time = TimeRange.Days(new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 6)), order = 0 };
            visit.attachments.Add(new PlannerAttachment("Travel", true, TimeSpan.FromDays(1)));
            visit.attachments.Add(new PlannerAttachment("Prep", false, TimeSpan.FromDays(1)));
            PlannerTicket report = new PlannerTicket { name = "Report", categoryId = category, time = TimeRange.Days(new DateOnly(2026, 10, 9), new DateOnly(2026, 10, 10)), order = 1, follows = visit.id };
            planner.tickets.AddRange(new[] { visit, report });
            PlannerTicket client = Timed(planner, "Client", new DateTime(2026, 10, 6, 10, 0, 0), 60);
            client.order = 2;
            client.attachments.Add(new PlannerAttachment("Travel", true, TimeSpan.FromMinutes(45)));
            client.attachments.Add(new PlannerAttachment("Prep", false, TimeSpan.FromMinutes(30)));
            PlannerEditorControl editor = Show(t, planner);
            yield return 4;
            yield return t.Golden("Gantt", editor.chart!);

            editor.ShowView(PlannerView.Calendar);
            yield return 4;
            yield return t.Golden("Week", editor.calendar!);
        }

        // The popup's text boxes in order, found from its name box.
        private static List<TextBoxControl> PopupBoxes(Control within, string name)
        {
            TextBoxControl? box = Find<TextBoxControl>(within).FirstOrDefault(b => b.text == name);
            if (box?.parent?.parent is not Control content) return new List<TextBoxControl>();
            return content.children.OfType<Control>().SelectMany(row => row.children.OfType<TextBoxControl>()).ToList();
        }

        private static PlannerTicket Timed(PlannerDocument planner, string name, DateTime start, int minutes)
        {
            PlannerTicket ticket = new PlannerTicket { name = name, categoryId = planner.categories[0].id, time = new TimeRange(start, start.AddMinutes(minutes), false, null) };
            planner.tickets.Add(ticket);
            return ticket;
        }

        private static ButtonControl? ButtonNamed(Control within, string caption) =>
            Find<ButtonControl>(within).FirstOrDefault(b => b.children.OfType<LabelControl>().Any(l => l.text == caption));

        // General holds A and B, Done holds C.
        private static PlannerDocument BoardFixture(out PlannerCategory general, out PlannerCategory done)
        {
            PlannerDocument planner = PlannerDocument.Blank("Board");
            general = planner.categories[0];
            done = new PlannerCategory { name = "Done", colorHex = "#30A46C" };
            planner.categories.Add(done);
            TimeRange days = TimeRange.Days(new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 7));
            planner.tickets.Add(new PlannerTicket { name = "A", categoryId = general.id, time = days, creator = User.current, order = 0 });
            planner.tickets.Add(new PlannerTicket { name = "B", categoryId = general.id, time = days, creator = User.current, order = 1 });
            planner.tickets.Add(new PlannerTicket { name = "C", categoryId = done.id, time = days, creator = User.current, order = 0 });
            return planner;
        }

        private static Vector2 Centre(LayoutRect rect) => new Vector2(rect.x + rect.width * 0.5f, rect.y + rect.height * 0.5f);

        [A_XSDActionDependency("Planner.GanttGolden", "Test")]
        private static IEnumerator<int> GanttGolden(TestContext t)
        {
            PlannerDocument planner = PlannerDocument.Blank("Golden");
            planner.categories[0].name = "Design";
            planner.categories.Add(new PlannerCategory { name = "Build", colorHex = "#30A46C" });
            string[] names = { "Sketches", "Mockups", "Engine work", "UI pass" };
            for (int i = 0; i < names.Length; i++)
                planner.tickets.Add(new PlannerTicket
                {
                    name = names[i],
                    categoryId = planner.categories[i / 2].id,
                    time = TimeRange.Days(new DateOnly(2026, 10, 1 + i * 3), new DateOnly(2026, 10, 3 + i * 4)),
                    colorHex = i == 3 ? "#FFB224" : null,
                    order = i
                });

            PlannerEditorControl editor = Show(t, planner);
            editor.Select(planner.tickets[1]);
            yield return 4;
            yield return t.Golden("Day", editor.chart!);
        }

        // One three-day ticket, 5-7 Oct 2026, with the clock pinned to noon on the 6th.
        private static (PlannerEditorControl, PlannerTicket) ShowOne(TestContext t)
        {
            PlannerDocument planner = PlannerDocument.Blank("Edit");
            PlannerTicket ticket = new PlannerTicket
            {
                name = "Three days",
                categoryId = planner.categories[0].id,
                time = TimeRange.Days(new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 7))
            };
            planner.tickets.Add(ticket);
            return (Show(t, planner), ticket);
        }

        private static PlannerEditorControl Show(TestContext t, PlannerDocument planner)
        {
            PlannerEditorControl editor = new PlannerEditorControl { clock = () => new DateTime(2026, 10, 6, 12, 0, 0) };
            editor.Load(planner);
            t.Show(editor);
            return editor;
        }
    }
}
