using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.EngineWork;
using System.Globalization;
using System.Numerics;

namespace ArctisAurora.Core.UI
{
    // One page of a sheet as a grid; only the cells in the viewport have controls.
    public class SheetControl : ContainerControl
    {
        // Parts its owner lays out one by one.
        private sealed class Parts : ContainerControl
        {
            public Parts() => hitTestable = false;

            protected override Vector2 MeasureCore(Vector2 availableSize)
            {
                foreach (Entity child in children)
                    if (child is Control control) control.Measure(unbounded);
                arrange.desired = availableSize;
                return availableSize;
            }

            protected override void ArrangeCore(LayoutRect finalRect) => WriteArranged(finalRect);
        }

        // geometry, design pixels
        public const float headerWidth = 48f;
        public const float headerHeight = 24f;
        private const float cellInset = 4f;
        private const float lineWidth = 1f;
        private const float outlineWidth = 2f;
        private const int fontSize = 14;

        // grid past the last used cell
        private const int spareRows = 100;
        private const int spareColumns = 26;

        private static readonly Vector2 unbounded = new Vector2(float.MaxValue, float.MaxValue);

        private readonly SheetEditorControl editor;
        public readonly SheetPage page;

        // parts, in draw order
        private readonly Parts grid = new Parts();
        private readonly PanelControl selection = new PanelControl { hitTestable = false, alpha = 0.18f };
        private readonly PanelControl[] outline = new PanelControl[4];
        internal readonly TextBoxControl field = new TextBoxControl { fontSize = fontSize };
        private readonly Parts headers = new Parts();

        // pooled grid and header parts
        private readonly List<PanelControl> lines = new List<PanelControl>();
        private readonly List<LabelControl> labels = new List<LabelControl>();
        private readonly List<PanelControl> headerParts = new List<PanelControl>();
        private readonly List<LabelControl> headerNames = new List<LabelControl>();

        // used extent, rebuilt after an edit
        private bool usedStale = true;
        private (int rows, int columns) used;

        public SheetControl(SheetEditorControl editor, SheetPage page)
        {
            this.editor = editor;
            this.page = page;

            selection.PaintOr(null, PaletteRole.Accent);
            field.PaintOr(null, PaletteRole.Field);

            AddChild(grid);
            AddChild(selection);
            for (int i = 0; i < outline.Length; i++)
            {
                outline[i] = new PanelControl { hitTestable = false };
                outline[i].PaintOr(null, PaletteRole.Accent);
                AddChild(outline[i]);
            }
            AddChild(field);
            AddChild(headers);
        }

        // Cells were written; the extent may have moved.
        internal void CellsChanged()
        {
            usedStale = true;
            InvalidateLayout();
        }

        #region ---- geometry ----
        private Vector2 Origin => new Vector2(arrangedRect.x + headerWidth, arrangedRect.y + headerHeight);

        // The scroller's inner rect: the part of the grid on screen.
        private LayoutRect Viewport() =>
            parent is ScrollableControl scroller ? scroller.arrangedRect.Shrink(scroller.padding) : arrangedRect;

        public LayoutRect CellRect(int row, int column)
        {
            Vector2 origin = Origin;
            return new LayoutRect(origin.X + page.ColumnLeft(column), origin.Y + page.RowTop(row),
                page.ColumnWidth(column), page.RowHeight(row));
        }

        // The cell under a point; false over the headers.
        public bool CellAt(Vector2 point, out int row, out int column)
        {
            LayoutRect view = Viewport();
            Vector2 origin = Origin;
            row = page.RowAt(point.Y - origin.Y);
            column = page.ColumnAt(point.X - origin.X);
            return point.X >= view.x + headerWidth && point.Y >= view.y + headerHeight;
        }

        private LayoutRect RangeRect()
        {
            LayoutRect first = CellRect(Math.Min(editor.anchorRow, editor.activeRow), Math.Min(editor.anchorColumn, editor.activeColumn));
            LayoutRect last = CellRect(Math.Max(editor.anchorRow, editor.activeRow), Math.Max(editor.anchorColumn, editor.activeColumn));
            return new LayoutRect(first.x, first.y, last.Right - first.x, last.Bottom - first.y);
        }
        #endregion

        #region ---- layout ----
        protected override Vector2 MeasureCore(Vector2 availableSize)
        {
            if (usedStale)
            {
                used = page.Used();
                usedStale = false;
            }

            foreach (Entity child in children)
                if (child is Control control) control.Measure(unbounded);

            int rows = Math.Max(used.rows, editor.activeRow + 1) + spareRows;
            int columns = Math.Max(used.columns, editor.activeColumn + 1) + spareColumns;
            arrange.desired = new Vector2(headerWidth + page.ColumnLeft(columns), headerHeight + page.RowTop(rows));
            return arrange.desired;
        }

        protected override void ArrangeCore(LayoutRect finalRect)
        {
            WriteArranged(finalRect);
            grid.Arrange(finalRect);
            headers.Arrange(finalRect);

            LayoutRect view = Viewport();
            Vector2 origin = Origin;
            float cellsLeft = view.x + headerWidth;
            float cellsTop = view.y + headerHeight;

            int firstColumn = page.ColumnAt(cellsLeft - origin.X);
            int firstRow = page.RowAt(cellsTop - origin.Y);
            int lastColumn = firstColumn;
            while (origin.X + page.ColumnLeft(lastColumn + 1) < view.Right) lastColumn++;
            int lastRow = firstRow;
            while (origin.Y + page.RowTop(lastRow + 1) < view.Bottom) lastRow++;

            ArrangeGrid(view, origin, firstRow, lastRow, firstColumn, lastColumn);
            ArrangeSelection();
            ArrangeHeaders(view, origin, firstRow, lastRow, firstColumn, lastColumn);
        }

        // Lines at every visible band's far edge, then the text of every visible cell.
        private void ArrangeGrid(LayoutRect view, Vector2 origin, int firstRow, int lastRow, int firstColumn, int lastColumn)
        {
            int line = 0;
            for (int c = firstColumn; c <= lastColumn; c++)
            {
                float right = origin.X + page.ColumnLeft(c + 1);
                Pooled(lines, grid, line++, PaletteRole.Line).Arrange(new LayoutRect(right - lineWidth, view.y, lineWidth, view.height));
            }
            for (int r = firstRow; r <= lastRow; r++)
            {
                float bottom = origin.Y + page.RowTop(r + 1);
                Pooled(lines, grid, line++, PaletteRole.Line).Arrange(new LayoutRect(view.x, bottom - lineWidth, view.width, lineWidth));
            }
            for (int i = line; i < lines.Count; i++)
                lines[i].Arrange(LayoutRect.Empty);

            int label = 0;
            for (int r = firstRow; r <= lastRow; r++)
                for (int c = firstColumn; c <= lastColumn; c++)
                {
                    if (editor.editing && r == editor.editRow && c == editor.editColumn) continue;

                    string? text = page.Shown(r, c);
                    if (string.IsNullOrEmpty(text)) continue;

                    LayoutRect cell = CellRect(r, c);
                    bool number = double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
                    PlaceText(Label(labels, grid, label++, PaletteRole.Ink), text, cell, number);
                }
            for (int i = label; i < labels.Count; i++)
                labels[i].Arrange(LayoutRect.Empty);
        }

        // Shaded range, outlined active cell, and the field over the cell being edited.
        private void ArrangeSelection()
        {
            bool range = editor.anchorRow != editor.activeRow || editor.anchorColumn != editor.activeColumn;
            selection.Arrange(range ? RangeRect() : LayoutRect.Empty);

            LayoutRect active = CellRect(editor.activeRow, editor.activeColumn);
            outline[0].Arrange(new LayoutRect(active.x, active.y, active.width, outlineWidth));
            outline[1].Arrange(new LayoutRect(active.x, active.Bottom - outlineWidth, active.width, outlineWidth));
            outline[2].Arrange(new LayoutRect(active.x, active.y, outlineWidth, active.height));
            outline[3].Arrange(new LayoutRect(active.Right - outlineWidth, active.y, outlineWidth, active.height));

            if (!editor.editing)
            {
                field.Arrange(LayoutRect.Empty);
                return;
            }

            LayoutRect cell = CellRect(editor.editRow, editor.editColumn);
            field.Arrange(new LayoutRect(cell.x + outlineWidth, cell.y + outlineWidth,
                cell.width - outlineWidth * 2f, cell.height - outlineWidth * 2f));
        }

        // Column letters along the top, row numbers down the side, both pinned to the viewport.
        private void ArrangeHeaders(LayoutRect view, Vector2 origin, int firstRow, int lastRow, int firstColumn, int lastColumn)
        {
            Pooled(headerParts, headers, 0, PaletteRole.Chrome).Arrange(new LayoutRect(view.x, view.y, view.width, headerHeight));
            Pooled(headerParts, headers, 1, PaletteRole.Chrome).Arrange(new LayoutRect(view.x, view.y, headerWidth, view.height));
            Pooled(headerParts, headers, 2, PaletteRole.Line).Arrange(new LayoutRect(view.x, view.y + headerHeight - lineWidth, view.width, lineWidth));
            Pooled(headerParts, headers, 3, PaletteRole.Line).Arrange(new LayoutRect(view.x + headerWidth - lineWidth, view.y, lineWidth, view.height));

            int name = 0;
            for (int c = firstColumn; c <= lastColumn; c++)
            {
                float left = origin.X + page.ColumnLeft(c);
                if (left + page.ColumnWidth(c) <= view.x + headerWidth) continue;
                LayoutRect band = new LayoutRect(left, view.y, page.ColumnWidth(c), headerHeight);
                PlaceCentred(Label(headerNames, headers, name++, PaletteRole.MutedInk), SheetDocument.ColumnName(c), band);
            }
            for (int r = firstRow; r <= lastRow; r++)
            {
                float top = origin.Y + page.RowTop(r);
                if (top + page.RowHeight(r) <= view.y + headerHeight) continue;
                LayoutRect band = new LayoutRect(view.x, top, headerWidth, page.RowHeight(r));
                PlaceCentred(Label(headerNames, headers, name++, PaletteRole.MutedInk), (r + 1).ToString(CultureInfo.InvariantCulture), band);
            }
            for (int i = name; i < headerNames.Count; i++)
                headerNames[i].Arrange(LayoutRect.Empty);
        }

        private static void PlaceText(LabelControl label, string text, LayoutRect cell, bool alignRight)
        {
            label.text = text;
            label.Measure(unbounded);

            float room = MathF.Max(0f, cell.width - cellInset * 2f);
            Vector2 size = label.DesiredSize;
            float x = alignRight && size.X <= room ? cell.Right - cellInset - size.X : cell.x + cellInset;
            label.Arrange(new LayoutRect(x, cell.y + (cell.height - size.Y) * 0.5f, MathF.Min(size.X, room), size.Y));
        }

        private static void PlaceCentred(LabelControl label, string text, LayoutRect band)
        {
            label.text = text;
            label.Measure(unbounded);

            Vector2 size = label.DesiredSize;
            float width = MathF.Min(size.X, band.width);
            label.Arrange(new LayoutRect(band.x + (band.width - width) * 0.5f, band.y + (band.height - size.Y) * 0.5f, width, size.Y));
        }

        private PanelControl Pooled(List<PanelControl> pool, Parts layer, int index, PaletteRole role)
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

        private LabelControl Label(List<LabelControl> pool, Parts layer, int index, PaletteRole role)
        {
            while (pool.Count <= index)
            {
                LabelControl label = new LabelControl { hitTestable = false, fontSize = fontSize };
                label.PaintOr(null, role);
                Adopt(layer, label);
                pool.Add(label);
            }
            return pool[index];
        }

        private void Adopt(Parts layer, Control part)
        {
            part.parent = layer;
            layer.children.Add(part);
            MarkTreeOrderDirty();
            part.Measure(unbounded);
        }
        #endregion

        #region ---- pointer ----
        public override bool OnPointerPress(PointerEvent e)
        {
            base.OnPointerPress(e);
            if (e.button != PointerEvent.leftButton) return false;

            if (CellAt(e.point, out int row, out int column))
            {
                editor.Select(row, column, InputHandler.instance.IsModifierDown(InputModifier.Extend));
                StartDrag();
            }
            return true;
        }

        public override void OnDrag(PointerEvent e)
        {
            base.OnDrag(e);
            if (CellAt(e.point, out int row, out int column)) editor.Select(row, column, true);
        }

        public override bool OnPointerTap(PointerEvent e)
        {
            if (e.tapCount != 2 || !CellAt(e.point, out _, out _)) return base.OnPointerTap(e);

            editor.BeginEdit(true);
            return true;
        }
        #endregion
    }
}
