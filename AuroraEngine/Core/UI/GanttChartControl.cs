using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.Filing;
using Silk.NET.GLFW;
using System.Globalization;
using System.Numerics;

namespace ArctisAurora.Core.UI
{
    public enum PlannerZoom { Hour, Day, Week, Month }

    // A planner's tickets as bars on a time axis, a row each under their category; only rows and units in the viewport have controls.
    public class GanttChartControl : ContainerControl
    {
        // Parts its owner lays out one by one.
        private sealed class ChartParts : ContainerControl
        {
            public ChartParts() => hitTestable = false;

            protected override Vector2 MeasureCore(Vector2 availableSize)
            {
                foreach (Entity child in children)
                    if (child is Control control) control.Measure(unbounded);
                arrange.desired = availableSize;
                return availableSize;
            }

            protected override void ArrangeCore(LayoutRect finalRect) => WriteArranged(finalRect);

            // Arranges again once its children are placed, so its bounds hold this frame's.
            internal void Settle(LayoutRect rect)
            {
                SetFlag(ArrangeFlags.ArrangeDirty, true);
                Arrange(rect);
            }
        }

        private enum Unit { Hour, Day, Week, Month, Year }

        private enum Grip { Move, Start, End }

        // geometry, design pixels
        public const float nameWidth = 200f;
        public const float bandHeight = 22f;
        public const float headerHeight = bandHeight * 2f;
        public const float rowHeight = 28f;
        private const float barInset = 5f;
        private const float textInset = 8f;
        private const float ticketIndent = 16f;
        private const float lineWidth = 1f;
        private const float todayWidth = 2f;
        private const float outlineWidth = 2f;
        private const float grabWidth = 6f;
        private const float elbowWidth = 8f;
        private const float attachmentAlpha = 0.4f;
        private const int fontSize = 13;

        private static readonly Vector2 unbounded = new Vector2(float.MaxValue, float.MaxValue);

        private readonly PlannerEditorControl editor;

        // the rows, top to bottom: a category, then its tickets
        private readonly List<(PlannerCategory category, PlannerTicket? ticket)> rows = new List<(PlannerCategory, PlannerTicket?)>();
        private bool rowsStale = true;

        // the time span drawn, rebuilt with the rows
        public DateTime origin { get; private set; }
        public DateTime end { get; private set; }

        // parts, in draw order
        private readonly ChartParts bands = new ChartParts();
        private readonly ChartParts grid = new ChartParts();
        private readonly ChartParts bars = new ChartParts();
        private readonly PanelControl[] outline = new PanelControl[4];
        private readonly PanelControl today = new PanelControl { hitTestable = false };
        private readonly ChartParts names = new ChartParts();
        private readonly ChartParts headers = new ChartParts();

        // pooled parts
        private readonly List<PanelControl> bandParts = new List<PanelControl>();
        private readonly List<PanelControl> lines = new List<PanelControl>();
        private readonly List<PanelControl> barParts = new List<PanelControl>();
        private readonly List<PanelControl> attachmentParts = new List<PanelControl>();
        private readonly List<PanelControl> linkParts = new List<PanelControl>();
        private readonly List<PanelControl> nameParts = new List<PanelControl>();
        private readonly List<LabelControl> categoryNames = new List<LabelControl>();
        private readonly List<LabelControl> ticketNames = new List<LabelControl>();
        private readonly List<PanelControl> headerParts = new List<PanelControl>();
        private readonly List<LabelControl> unitNames = new List<LabelControl>();
        private readonly List<LabelControl> upperNames = new List<LabelControl>();

        // the bar being dragged, as it was at the press
        private PlannerTicket? dragged;
        private PlannerTicket? draggedBefore;
        private Grip grip;
        private DateTime pressTime;
        private CursorShape shownCursor = CursorShape.Arrow;

        // "Move to category": the categories of the planner the menu was opened on.
        static GanttChartControl() => ContextMenus.RegisterSource("planner-categories", () =>
        {
            List<ContextMenuEntry> entries = new List<ContextMenuEntry>();
            PlannerEditorControl? editor = null;
            for (Control? c = ContextMenus.target; c != null && editor == null; c = c.parent as Control)
                editor = c as PlannerEditorControl;
            if (editor?.selected == null) return entries;

            foreach (PlannerCategory category in editor.document.categories)
                entries.Add(new ContextMenuButton(category.name, () => editor.MoveSelected(category)));
            return entries;
        });

        public GanttChartControl(PlannerEditorControl editor)
        {
            this.editor = editor;
            contextMenu = "planner";
            stopsContextMenu = true;
            today.PaintOr(null, PaletteRole.Accent);

            AddChild(bands);
            AddChild(grid);
            AddChild(bars);
            for (int i = 0; i < outline.Length; i++)
            {
                outline[i] = new PanelControl { hitTestable = false };
                outline[i].PaintOr(null, PaletteRole.Ink);
                AddChild(outline[i]);
            }
            AddChild(today);
            AddChild(names);
            AddChild(headers);
        }

        // Tickets or categories changed; rows and span are rebuilt.
        internal void RowsChanged()
        {
            rowsStale = true;
            InvalidateLayout();
        }

        #region ---- geometry ----
        private float DayWidth => editor.zoom switch
        {
            PlannerZoom.Hour => 768f,
            PlannerZoom.Day => 32f,
            PlannerZoom.Week => 10f,
            _ => 3f
        };

        private Unit Lower => (Unit)editor.zoom;

        private Unit Upper => editor.zoom switch
        {
            PlannerZoom.Hour => Unit.Day,
            PlannerZoom.Month => Unit.Year,
            _ => Unit.Month
        };

        // A time's distance from the chart's left edge of the time area.
        public float Offset(DateTime time) => (float)(time - origin).TotalDays * DayWidth;

        public float X(DateTime time) => arrangedRect.x + nameWidth + Offset(time);

        public DateTime TimeAt(float x) => origin.AddDays((x - arrangedRect.x - nameWidth) / DayWidth);

        public float RowTop(int row) => arrangedRect.y + headerHeight + row * rowHeight;

        public int RowOf(PlannerTicket ticket) => rows.FindIndex(row => ReferenceEquals(row.ticket, ticket));

        public LayoutRect BarRect(PlannerTicket ticket)
        {
            float left = X(ticket.time.start);
            float top = RowTop(RowOf(ticket));
            return new LayoutRect(left, top + barInset, MathF.Max(2f, X(ticket.time.end) - left), rowHeight - barInset * 2f);
        }

        private LayoutRect Hidden => new LayoutRect(arrangedRect.x, arrangedRect.y, 0f, 0f);

        private LayoutRect Viewport() =>
            parent is ScrollableControl scroller ? scroller.arrangedRect.Shrink(scroller.padding) : arrangedRect;

        private static DateTime Floor(DateTime time, Unit unit) => unit switch
        {
            Unit.Hour => new DateTime(time.Year, time.Month, time.Day, time.Hour, 0, 0),
            Unit.Day => time.Date,
            Unit.Week => time.Date.AddDays(-(((int)time.DayOfWeek + 6) % 7)),
            Unit.Month => new DateTime(time.Year, time.Month, 1),
            _ => new DateTime(time.Year, 1, 1)
        };

        private static DateTime Next(DateTime time, Unit unit) => unit switch
        {
            Unit.Hour => time.AddHours(1),
            Unit.Day => time.AddDays(1),
            Unit.Week => time.AddDays(7),
            Unit.Month => time.AddMonths(1),
            _ => time.AddYears(1)
        };

        private static string Name(DateTime time, Unit unit) => unit switch
        {
            Unit.Hour => time.ToString("HH", CultureInfo.InvariantCulture),
            Unit.Day => time.Day.ToString(CultureInfo.InvariantCulture),
            Unit.Week => time.ToString("d MMM", CultureInfo.InvariantCulture),
            Unit.Month => time.ToString("MMM", CultureInfo.InvariantCulture),
            _ => time.ToString("yyyy", CultureInfo.InvariantCulture)
        };

        private static string UpperName(DateTime time, Unit unit) => unit switch
        {
            Unit.Day => time.ToString("ddd d MMMM yyyy", CultureInfo.InvariantCulture),
            Unit.Month => time.ToString("MMMM yyyy", CultureInfo.InvariantCulture),
            _ => time.ToString("yyyy", CultureInfo.InvariantCulture)
        };
        #endregion

        #region ---- layout ----
        // Categories in order, each followed by its tickets; the span covers every ticket and today, padded by a few units.
        private void BuildRows()
        {
            rows.Clear();
            PlannerDocument document = editor.document;
            foreach (PlannerCategory category in document.categories)
            {
                rows.Add((category, null));
                foreach (PlannerTicket ticket in document.TicketsIn(category))
                    rows.Add((category, ticket));
            }

            DateTime now = editor.Now;
            DateTime first = document.tickets.Count == 0 ? now : document.tickets.Min(ticket => ticket.OuterStart);
            DateTime last = document.tickets.Count == 0 ? now : document.tickets.Max(ticket => ticket.OuterEnd);
            if (now < first) first = now;
            if (now > last) last = now;

            Unit lower = Lower;
            int pad = lower == Unit.Hour ? 6 : 4;
            origin = Floor(first, lower);
            for (int i = 0; i < pad; i++) origin = Floor(origin.AddTicks(-1), lower);
            end = Floor(last, lower);
            for (int i = 0; i <= pad; i++) end = Next(end, lower);
        }

        protected override Vector2 MeasureCore(Vector2 availableSize)
        {
            if (rowsStale)
            {
                BuildRows();
                rowsStale = false;
            }

            foreach (Entity child in children)
                if (child is Control control) control.Measure(unbounded);

            arrange.desired = new Vector2(nameWidth + Offset(end), headerHeight + rows.Count * rowHeight);
            return arrange.desired;
        }

        protected override void ArrangeCore(LayoutRect finalRect)
        {
            WriteArranged(finalRect);
            bands.Arrange(finalRect);
            grid.Arrange(finalRect);
            bars.Arrange(finalRect);
            names.Arrange(finalRect);
            headers.Arrange(finalRect);

            LayoutRect view = Viewport();
            float bodyTop = view.y + headerHeight;
            int firstRow = Math.Max(0, (int)MathF.Floor((bodyTop - RowTop(0)) / rowHeight));
            int lastRow = Math.Min(rows.Count - 1, (int)MathF.Floor((view.Bottom - RowTop(0)) / rowHeight));
            DateTime from = TimeAt(view.x + nameWidth);
            DateTime to = TimeAt(view.Right);
            if (from < origin) from = origin;
            if (to > end) to = end;

            ArrangeUnits(view, from, to, ArrangeRows(view, firstRow, lastRow));
            ArrangeLinks(firstRow, lastRow);
            ArrangeBars(firstRow, lastRow);
            ArrangeSelection();
            ArrangeToday(view);
            ArrangeNames(view, firstRow, lastRow);
            ArrangeHeader(view, from, to);
            bands.Settle(finalRect);
            grid.Settle(finalRect);
            bars.Settle(finalRect);
            names.Settle(finalRect);
            headers.Settle(finalRect);
        }

        // Category rows shaded, a line under every row.
        private int ArrangeRows(LayoutRect view, int firstRow, int lastRow)
        {
            int band = 0;
            int line = 0;
            for (int r = firstRow; r <= lastRow; r++)
            {
                float top = RowTop(r);
                if (rows[r].ticket == null)
                    Pooled(bandParts, bands, band++, PaletteRole.Chrome).Arrange(new LayoutRect(view.x, top, view.width, rowHeight));
                Pooled(lines, grid, line++, PaletteRole.Line).Arrange(new LayoutRect(view.x, top + rowHeight - lineWidth, view.width, lineWidth));
            }
            for (int i = band; i < bandParts.Count; i++)
                bandParts[i].Arrange(Hidden);
            return line;
        }

        // A line at the start of every lower unit in view, down to the last row; row lines took the first ones.
        private void ArrangeUnits(LayoutRect view, DateTime from, DateTime to, int line)
        {
            Unit lower = Lower;
            float height = MathF.Max(0f, MathF.Min(view.Bottom, RowTop(rows.Count)) - view.y);
            for (DateTime t = Floor(from, lower); t <= to; t = Next(t, lower))
            {
                float x = X(t);
                if (x < view.x + nameWidth) continue;
                Pooled(lines, grid, line++, PaletteRole.Line).Arrange(new LayoutRect(x, view.y, lineWidth, height));
            }
            for (int i = line; i < lines.Count; i++)
                lines[i].Arrange(Hidden);
        }

        // Bars, with their attachments faint on either side.
        private void ArrangeBars(int firstRow, int lastRow)
        {
            PlannerDocument document = editor.document;
            int bar = 0;
            int attached = 0;
            for (int r = firstRow; r <= lastRow; r++)
            {
                if (rows[r].ticket is not PlannerTicket ticket) continue;
                PanelControl part = Pooled(barParts, bars, bar++, PaletteRole.Accent);
                string color = document.ColorOf(ticket);
                if (part.colorHex != color) part.PaintOr(color, PaletteRole.Accent);
                part.cornerRole = CornerRole.Control;
                LayoutRect rect = BarRect(ticket);
                part.Arrange(rect);

                DateTime before = ticket.OuterStart;
                DateTime after = ticket.time.end;
                foreach (PlannerAttachment attachment in ticket.attachments)
                {
                    ref DateTime cursor = ref attachment.before ? ref before : ref after;
                    float left = X(cursor);
                    cursor += attachment.duration;
                    PanelControl block = Pooled(attachmentParts, bars, attached++, PaletteRole.Accent);
                    if (block.alpha != attachmentAlpha) block.alpha = attachmentAlpha;
                    string kindColor = PlannerAttachmentKinds.Find(attachment.kind)?.colorHex ?? color;
                    if (block.colorHex != kindColor) block.PaintOr(kindColor, PaletteRole.Accent);
                    block.Arrange(new LayoutRect(left, rect.y, X(cursor) - left, rect.height));
                }
            }
            for (int i = bar; i < barParts.Count; i++)
                barParts[i].Arrange(Hidden);
            for (int i = attached; i < attachmentParts.Count; i++)
                attachmentParts[i].Arrange(Hidden);
        }

        // An elbow from each leader's end to its follower's start, when either row is in view.
        private void ArrangeLinks(int firstRow, int lastRow)
        {
            PlannerDocument document = editor.document;
            int link = 0;
            foreach (PlannerTicket follower in document.tickets)
            {
                if (follower.follows is not Guid id || document.Find(id) is not PlannerTicket leader) continue;
                int from = RowOf(leader);
                int to = RowOf(follower);
                if (from < 0 || to < 0 || Math.Max(from, to) < firstRow || Math.Min(from, to) > lastRow) continue;

                float startX = X(leader.time.end);
                float bendX = startX + elbowWidth;
                float endX = X(follower.time.start);
                float fromY = RowTop(from) + rowHeight * 0.5f;
                float toY = RowTop(to) + rowHeight * 0.5f;
                Pooled(linkParts, grid, link++, PaletteRole.MutedInk).Arrange(new LayoutRect(startX, fromY, elbowWidth, lineWidth));
                Pooled(linkParts, grid, link++, PaletteRole.MutedInk).Arrange(new LayoutRect(bendX, MathF.Min(fromY, toY), lineWidth, MathF.Abs(toY - fromY) + lineWidth));
                Pooled(linkParts, grid, link++, PaletteRole.MutedInk).Arrange(new LayoutRect(MathF.Min(bendX, endX), toY, MathF.Abs(endX - bendX), lineWidth));
            }
            for (int i = link; i < linkParts.Count; i++)
                linkParts[i].Arrange(Hidden);
        }

        // An outline around the selected ticket's bar.
        private void ArrangeSelection()
        {
            PlannerTicket? ticket = editor.selected;
            if (ticket == null || RowOf(ticket) < 0)
            {
                foreach (PanelControl edge in outline)
                    edge.Arrange(Hidden);
                return;
            }

            LayoutRect bar = BarRect(ticket);
            LayoutRect ring = new LayoutRect(bar.x - outlineWidth, bar.y - outlineWidth, bar.width + outlineWidth * 2f, bar.height + outlineWidth * 2f);
            outline[0].Arrange(new LayoutRect(ring.x, ring.y, ring.width, outlineWidth));
            outline[1].Arrange(new LayoutRect(ring.x, ring.Bottom - outlineWidth, ring.width, outlineWidth));
            outline[2].Arrange(new LayoutRect(ring.x, ring.y, outlineWidth, ring.height));
            outline[3].Arrange(new LayoutRect(ring.Right - outlineWidth, ring.y, outlineWidth, ring.height));
        }

        private void ArrangeToday(LayoutRect view)
        {
            float x = X(editor.Now);
            float bottom = MathF.Min(view.Bottom, RowTop(rows.Count));
            bool shown = x >= view.x + nameWidth && x <= view.Right;
            today.Arrange(shown ? new LayoutRect(x - todayWidth * 0.5f, view.y, todayWidth, MathF.Max(0f, bottom - view.y)) : Hidden);
        }

        // The name column, pinned to the viewport's left edge.
        private void ArrangeNames(LayoutRect view, int firstRow, int lastRow)
        {
            Pooled(nameParts, names, 0, PaletteRole.Surface).Arrange(new LayoutRect(view.x, view.y, nameWidth, view.height));
            Pooled(nameParts, names, 1, PaletteRole.Line).Arrange(new LayoutRect(view.x + nameWidth - lineWidth, view.y, lineWidth, view.height));

            int category = 0;
            int ticket = 0;
            for (int r = firstRow; r <= lastRow; r++)
            {
                LayoutRect cell = new LayoutRect(view.x, RowTop(r), nameWidth, rowHeight);
                if (rows[r].ticket is PlannerTicket shown)
                    PlaceText(Label(ticketNames, names, ticket++, PaletteRole.Ink, FontStyle.Regular), shown.name, cell, ticketIndent);
                else
                    PlaceText(Label(categoryNames, names, category++, PaletteRole.Ink, FontStyle.Bold), rows[r].category.name, cell, 0f);
            }
            for (int i = ticket; i < ticketNames.Count; i++)
                ticketNames[i].Arrange(Hidden);
            for (int i = category; i < categoryNames.Count; i++)
                categoryNames[i].Arrange(Hidden);
        }

        // Two bands pinned to the viewport's top: the larger unit above, the zoom's unit below.
        private void ArrangeHeader(LayoutRect view, DateTime from, DateTime to)
        {
            Pooled(headerParts, headers, 0, PaletteRole.Chrome).Arrange(new LayoutRect(view.x, view.y, view.width, headerHeight));
            Pooled(headerParts, headers, 1, PaletteRole.Line).Arrange(new LayoutRect(view.x + nameWidth, view.y + bandHeight - lineWidth, view.width - nameWidth, lineWidth));
            Pooled(headerParts, headers, 2, PaletteRole.Line).Arrange(new LayoutRect(view.x, view.y + headerHeight - lineWidth, view.width, lineWidth));
            Pooled(headerParts, headers, 3, PaletteRole.Line).Arrange(new LayoutRect(view.x + nameWidth - lineWidth, view.y, lineWidth, headerHeight));

            float left = view.x + nameWidth;
            Unit lower = Lower;
            int unit = 0;
            for (DateTime t = Floor(from, lower); t <= to; t = Next(t, lower))
            {
                float x = X(t);
                float width = X(Next(t, lower)) - x;
                if (x < left) continue;
                PlaceCentred(Label(unitNames, headers, unit++, PaletteRole.MutedInk, FontStyle.Regular), Name(t, lower),
                    new LayoutRect(x, view.y + bandHeight, width, bandHeight));
            }
            for (int i = unit; i < unitNames.Count; i++)
                unitNames[i].Arrange(Hidden);

            Unit upper = Upper;
            int name = 0;
            for (DateTime t = Floor(from, upper); t <= to; t = Next(t, upper))
            {
                float start = MathF.Max(left, X(t));
                float stop = MathF.Min(view.Right, X(Next(t, upper)));
                if (stop <= start) continue;
                PlaceText(Label(upperNames, headers, name++, PaletteRole.Ink, FontStyle.Regular), UpperName(t, upper),
                    new LayoutRect(start, view.y, stop - start, bandHeight), 0f);
            }
            for (int i = name; i < upperNames.Count; i++)
                upperNames[i].Arrange(Hidden);
        }

        private static void PlaceText(LabelControl label, string text, LayoutRect cell, float indent)
        {
            label.text = text;
            label.Measure(unbounded);

            float room = MathF.Max(0f, cell.width - textInset * 2f - indent);
            Vector2 size = label.DesiredSize;
            label.Arrange(new LayoutRect(cell.x + textInset + indent, cell.y + (cell.height - size.Y) * 0.5f, MathF.Min(size.X, room), size.Y));
        }

        private static void PlaceCentred(LabelControl label, string text, LayoutRect band)
        {
            label.text = text;
            label.Measure(unbounded);

            Vector2 size = label.DesiredSize;
            float width = MathF.Min(size.X, band.width);
            label.Arrange(new LayoutRect(band.x + (band.width - width) * 0.5f, band.y + (band.height - size.Y) * 0.5f, width, size.Y));
        }

        private PanelControl Pooled(List<PanelControl> pool, ChartParts layer, int index, PaletteRole role)
        {
            while (pool.Count <= index)
            {
                PanelControl part = new PanelControl { hitTestable = false };
                part.PaintOr(null, role);
                Adopt(layer, part);
                pool.Add(part);
            }
            return pool[index];
        }

        private LabelControl Label(List<LabelControl> pool, ChartParts layer, int index, PaletteRole role, FontStyle style)
        {
            while (pool.Count <= index)
            {
                LabelControl label = new LabelControl { hitTestable = false, fontSize = fontSize, style = style };
                label.PaintOr(null, role);
                Adopt(layer, label);
                pool.Add(label);
            }
            return pool[index];
        }

        private void Adopt(ChartParts layer, Control part)
        {
            part.parent = layer;
            layer.children.Add(part);
            MarkTreeOrderDirty();
            part.Measure(unbounded);
        }
        #endregion

        #region ---- pointer ----
        // The ticket whose row is under a point, below the header.
        public PlannerTicket? TicketAt(Vector2 point) => RowAt(point) is int row ? rows[row].ticket : null;

        // The category of the row under a point: its own row or one of its tickets'.
        public PlannerCategory? CategoryAt(Vector2 point) => RowAt(point) is int row ? rows[row].category : null;

        private int? RowAt(Vector2 point)
        {
            if (point.Y < Viewport().y + headerHeight) return null;
            int row = (int)MathF.Floor((point.Y - RowTop(0)) / rowHeight);
            return row >= 0 && row < rows.Count ? row : null;
        }

        // The bar under a point and which part of it: an edge resizes, the middle moves.
        private bool GripAt(Vector2 point, out PlannerTicket ticket, out Grip at)
        {
            ticket = null!;
            at = Grip.Move;
            if (point.X < Viewport().x + nameWidth || TicketAt(point) is not PlannerTicket hit) return false;

            LayoutRect bar = BarRect(hit);
            if (point.X < bar.x - grabWidth || point.X > bar.Right + grabWidth || point.Y < bar.y || point.Y > bar.Bottom) return false;

            float inside = MathF.Min(grabWidth, bar.width / 3f);
            ticket = hit;
            at = point.X <= bar.x + inside ? Grip.Start : point.X >= bar.Right - inside ? Grip.End : Grip.Move;
            return true;
        }

        public override bool OnPointerPress(PointerEvent e)
        {
            base.OnPointerPress(e);
            editor.PickCategory(CategoryAt(e.point), this, e.point);
            if (e.button == PointerEvent.rightButton)
            {
                editor.Select(TicketAt(e.point));
                return false;
            }
            if (e.button != PointerEvent.leftButton) return false;

            if (GripAt(e.point, out PlannerTicket ticket, out Grip at))
            {
                editor.Select(ticket);
                dragged = ticket;
                draggedBefore = ticket.Clone();
                grip = at;
                pressTime = TimeAt(e.point.X);
                StartDrag();
            }
            else editor.Select(TicketAt(e.point));
            return true;
        }

        // Moves or resizes in whole units: hours on Hour zoom for a timed ticket, days otherwise.
        public override void OnDrag(PointerEvent e)
        {
            base.OnDrag(e);
            if (dragged == null || draggedBefore == null) return;

            TimeRange was = draggedBefore.time;
            TimeSpan unit = was.allDay || editor.zoom != PlannerZoom.Hour ? TimeSpan.FromDays(1) : TimeSpan.FromHours(1);
            long steps = (long)Math.Round((double)(TimeAt(e.point.X) - pressTime).Ticks / unit.Ticks);
            TimeSpan delta = TimeSpan.FromTicks(steps * unit.Ticks);

            dragged.time = grip switch
            {
                Grip.Move => was with { start = was.start + delta, end = was.end + delta },
                Grip.Start => was with { start = Min(was.start + delta, was.end - unit) },
                _ => was with { end = Max(was.end + delta, was.start + unit) }
            };
            InvalidateLayout();
        }

        public override void OnDragStop(bool accepted)
        {
            base.OnDragStop(accepted);
            if (dragged == null || draggedBefore == null) return;

            PlannerTicket ticket = dragged;
            PlannerTicket before = draggedBefore;
            dragged = null;
            draggedBefore = null;
            editor.CommitTime(ticket, before, grip == Grip.Move ? "Move ticket" : "Resize ticket");
            ShowCursor(CursorShape.Arrow);
        }

        public override bool OnPointerMove(PointerEvent e)
        {
            bool handled = base.OnPointerMove(e);
            if (dragged == null)
                ShowCursor(GripAt(e.point, out _, out Grip at) && at != Grip.Move ? CursorShape.HResize : CursorShape.Arrow);
            return handled;
        }

        public override bool OnPointerExit(PointerEvent e)
        {
            if (dragged == null) ShowCursor(CursorShape.Arrow);
            return base.OnPointerExit(e);
        }

        public override bool OnPointerTap(PointerEvent e)
        {
            if (e.tapCount != 2 || RowAt(e.point) == null) return base.OnPointerTap(e);

            if (TicketAt(e.point) is PlannerTicket ticket)
            {
                editor.Select(ticket);
                editor.EditSelected();
            }
            else editor.EditPickedCategory();
            return true;
        }

        private void ShowCursor(CursorShape shape)
        {
            if (shownCursor == shape) return;
            shownCursor = shape;
            UIEngine.WindowOf(this)?.os.ChangeCursor(shape);
        }

        private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;

        private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;
        #endregion
    }
}
