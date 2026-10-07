using ArctisAurora.Core.Filing;
using ArctisAurora.Core.Users;
using ArctisAurora.EngineWork;
using System.Globalization;
using System.Numerics;

namespace ArctisAurora.Core.UI
{
    // A planner's tickets as cards in a column per category.
    public class PlannerBoardControl : StackPanelControl
    {
        public const float columnWidth = 260f;
        private const float gap = 12f;
        private const float inset = 8f;
        private const float stripWidth = 4f;
        private const int titleSize = 14;
        private const int detailSize = 12;

        private const float markWidth = 3f;
        private const float dropGap = 28f;
        private const float chipHeight = 18f;
        private const float dragThreshold = 4f;

        private readonly PlannerEditorControl editor;

        // the built columns, left to right, and their cards top to bottom
        private readonly List<(PlannerCategory category, StackPanelControl column, List<TicketCard> cards)> columns =
            new List<(PlannerCategory, StackPanelControl, List<TicketCard>)>();

        // the drop marker: the column outlined and a gap opened before the card a drop lands before, or after the last
        private StackPanelControl? markedColumn;
        private TicketCard? markedCard;
        private int markedIndex = -1;
        private bool rebuildPending;

        public PlannerBoardControl(PlannerEditorControl editor)
        {
            this.editor = editor;
            orientation = Orientation.Horizontal;
            Spacing = gap;
            padding = new Thickness(gap);
            alpha = 0f;
            Rebuild();
        }

        // Tickets or categories changed; the columns are built again next tick, once however many edits land.
        internal void RebuildSoon()
        {
            if (rebuildPending) return;
            rebuildPending = true;
            Engine.Post(() =>
            {
                rebuildPending = false;
                if (!destroyed) Rebuild();
            });
        }

        // Every column built again.
        private void Rebuild()
        {
            foreach (Control column in children.OfType<Control>().ToList())
                column.Destroy();
            columns.Clear();
            markedColumn = null;
            markedCard = null;
            markedIndex = -1;

            PlannerDocument document = editor.document;
            foreach (PlannerCategory category in document.categories)
            {
                List<PlannerTicket> tickets = document.TicketsIn(category).ToList();
                StackPanelControl column = new StackPanelControl
                {
                    preferredWidth = columnWidth,
                    verticalAlignment = VerticalAlignment.Top,
                    Spacing = inset,
                    padding = new Thickness(inset),
                    cornerRole = CornerRole.Control
                };
                column.PaintOr(null, PaletteRole.Chrome);
                column.contextMenu = "planner-category";
                column.stopsContextMenu = true;
                column.RegisterOnPress(e =>
                {
                    editor.PickCategory(category, column, e.point);
                    return false;
                });
                column.RegisterOnTap(e =>
                {
                    if (e.tapCount != 2) return false;
                    editor.EditPickedCategory();
                    return true;
                });

                StackPanelControl heading = new StackPanelControl { orientation = Orientation.Horizontal, Spacing = inset, alpha = 0f, hitTestable = false };
                PanelControl swatch = new PanelControl { preferredWidth = 10f, preferredHeight = 10f, verticalAlignment = VerticalAlignment.Center, cornerRole = CornerRole.Control, hitTestable = false };
                swatch.PaintOr(category.colorHex, PaletteRole.Accent);
                heading.AddChild(swatch);
                heading.AddChild(Text($"{category.name}  {tickets.Count}", titleSize, PaletteRole.Ink, FontStyle.Bold));
                column.AddChild(heading);

                List<TicketCard> cards = new List<TicketCard>();
                foreach (PlannerTicket ticket in tickets)
                {
                    TicketCard card = new TicketCard(this, ticket);
                    Fill(card, document, ticket);
                    cards.Add(card);
                    column.AddChild(card);
                }
                columns.Add((category, column, cards));
                AddChild(column);
            }
        }

        public IReadOnlyList<TicketCard> CardsIn(PlannerCategory category) =>
            columns.Find(c => ReferenceEquals(c.category, category)).cards ?? new List<TicketCard>();

        // A colour strip over the name, the dates and the creator.
        private static void Fill(StackPanelControl card, PlannerDocument document, PlannerTicket ticket)
        {
            PanelControl strip = new PanelControl { preferredHeight = stripWidth, horizontalAlignment = HorizontalAlignment.Stretch, cornerRole = CornerRole.Control, hitTestable = false };
            strip.PaintOr(document.ColorOf(ticket), PaletteRole.Accent);
            card.AddChild(strip);

            StackPanelControl body = new StackPanelControl { Spacing = 2f, padding = new Thickness(inset), alpha = 0f, hitTestable = false };
            body.AddChild(Text(ticket.name, titleSize, PaletteRole.Ink, FontStyle.Regular));
            body.AddChild(Text(Dates(ticket.time), detailSize, PaletteRole.MutedInk, FontStyle.Regular));
            if (ticket.creator is User creator)
                body.AddChild(Text($"by {creator.name}", detailSize, PaletteRole.MutedInk, FontStyle.Regular));
            if (ticket.assignees.Count > 0)
            {
                StackPanelControl chips = new StackPanelControl { orientation = Orientation.Horizontal, Spacing = 4f, alpha = 0f, hitTestable = false };
                foreach (User assignee in ticket.assignees)
                {
                    StackPanelControl chip = new StackPanelControl { preferredHeight = chipHeight, padding = new Thickness(6f, 0f), cornerRole = CornerRole.Control, hitTestable = false };
                    chip.PaintOr(null, PaletteRole.SubField);
                    LabelControl initials = Text(assignee.initials, detailSize, PaletteRole.Ink, FontStyle.Bold);
                    initials.verticalPosition = 0.5f;
                    chip.AddChild(initials);
                    chips.AddChild(chip);
                }
                body.AddChild(chips);
            }
            card.AddChild(body);
        }

        #region ---- drop ----
        // The column under a point, the nearest one beside it otherwise, and how many of its other cards sit above the point.
        private bool DropAt(Control dragged, Vector2 point, out int column, out int index)
        {
            column = -1;
            index = 0;
            if (dragged is not TicketCard card || !ReferenceEquals(card.board, this) || columns.Count == 0) return false;

            float nearest = float.MaxValue;
            for (int i = 0; i < columns.Count; i++)
            {
                LayoutRect rect = columns[i].column.arrangedRect;
                float distance = point.X < rect.x ? rect.x - point.X : point.X > rect.Right ? point.X - rect.Right : 0f;
                if (distance < nearest)
                {
                    nearest = distance;
                    column = i;
                }
            }

            foreach (TicketCard other in columns[column].cards)
                if (!ReferenceEquals(other, card) && other.arrangedRect.y + other.arrangedRect.height * 0.5f < point.Y) index++;
            return true;
        }

        public override bool DraggingOverStart(Control dragged, Vector2 point) => Mark(dragged, point);

        public override bool DraggingOver(Control dragged, Vector2 point) => Mark(dragged, point);

        public override bool DraggingOverEnd(Control dragged)
        {
            if (dragged is not TicketCard) return false;
            Unmark();
            return true;
        }

        public override bool FinishDrag(Control dragged, Vector2 point)
        {
            if (!DropAt(dragged, point, out int column, out int index)) return false;
            editor.MoveTicket(((TicketCard)dragged).ticket, columns[column].category, index);
            return true;
        }

        private bool Mark(Control dragged, Vector2 point)
        {
            if (!DropAt(dragged, point, out int column, out int index)) return false;

            StackPanelControl outlined = columns[column].column;
            if (ReferenceEquals(outlined, markedColumn) && index == markedIndex) return true;

            Unmark();
            markedColumn = outlined;
            markedIndex = index;
            outlined.edgeRole = PaletteRole.Accent;
            outlined.edgeThickness = new Thickness(markWidth * 0.5f);

            List<TicketCard> others = columns[column].cards.Where(c => !ReferenceEquals(c, dragged)).ToList();
            if (others.Count == 0) return true;
            markedCard = others[Math.Min(index, others.Count - 1)];
            markedCard.margin = index < others.Count ? new Thickness(dropGap, 0f, 0f, 0f) : new Thickness(0f, 0f, dropGap, 0f);
            return true;
        }

        private void Unmark()
        {
            if (markedColumn != null) markedColumn.edgeThickness = new Thickness(0f);
            if (markedCard != null) markedCard.margin = new Thickness(0f);
            markedColumn = null;
            markedCard = null;
            markedIndex = -1;
        }
        #endregion

        // One ticket's card; a press dragged past a few pixels carries it to another place on the board.
        public sealed class TicketCard : StackPanelControl
        {
            public readonly PlannerBoardControl board;
            public readonly PlannerTicket ticket;
            private Vector2 grab;
            private bool armed;

            public TicketCard(PlannerBoardControl board, PlannerTicket ticket)
            {
                this.board = board;
                this.ticket = ticket;
                horizontalAlignment = HorizontalAlignment.Stretch;
                cornerRole = CornerRole.Control;
                PaintOr(null, PaletteRole.Surface);
            }

            public override bool OnPointerPress(PointerEvent e)
            {
                base.OnPointerPress(e);
                board.editor.PickCategory(board.editor.document.CategoryOf(ticket), this, e.point);
                armed = e.button == PointerEvent.leftButton;
                grab = e.point;
                return armed;
            }

            // Keeps taps from reaching the column.
            public override bool OnPointerTap(PointerEvent e)
            {
                base.OnPointerTap(e);
                return true;
            }

            public override bool OnPointerMove(PointerEvent e)
            {
                if (armed)
                {
                    if (!InputHandler.instance.IsKeyDown(Keys.MouseLeft)) armed = false;
                    else if ((e.point - grab).Length() >= dragThreshold)
                    {
                        armed = false;
                        StartDrag();
                        DragGhost.Show(this);
                    }
                }
                return base.OnPointerMove(e);
            }

            public override bool OnPointerRelease(PointerEvent e)
            {
                armed = false;
                return base.OnPointerRelease(e);
            }

            public override void OnDragStop(bool accepted)
            {
                DragGhost.Hide();
                base.OnDragStop(accepted);
            }
        }

        private static LabelControl Text(string text, int size, PaletteRole role, FontStyle style)
        {
            LabelControl label = new LabelControl { text = text, fontSize = size, style = style, hitTestable = false };
            label.PaintOr(null, role);
            return label;
        }

        // "7 Oct - 9 Oct" for whole days, with times otherwise.
        public static string Dates(TimeRange time)
        {
            if (time.allDay)
            {
                DateTime last = time.end.AddDays(-1);
                return last <= time.start
                    ? time.start.ToString("d MMM yyyy", CultureInfo.InvariantCulture)
                    : $"{time.start.ToString("d MMM", CultureInfo.InvariantCulture)} - {last.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}";
            }
            return $"{time.start.ToString("d MMM HH:mm", CultureInfo.InvariantCulture)} - {time.end.ToString("d MMM HH:mm", CultureInfo.InvariantCulture)}";
        }
    }
}
