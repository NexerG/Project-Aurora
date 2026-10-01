using ArctisAurora.Core.ECS.EngineEntity;
using System.Numerics;

namespace ArctisAurora.Core.UI
{
    // A table in a note: fixed-width columns of cells, each cell a stack of blocks.
    public class TableControl : GridListControl
    {
        // column widths in design pixels, before zoom
        public readonly List<float> widths;

        // cell borders, laid over the cells
        private readonly List<PanelControl> borders = new List<PanelControl>();

        private float zoom = 1f;

        private const float cellInset = 6f;
        private const float borderWidth = 1f;

        public TableControl(List<float> widths)
        {
            this.widths = widths;
            foreach (float width in widths)
                columnDefinitions.Add(new ColumnDefinition { sizeMode = GridSizeMode.Fixed, value = width });
        }

        // Appends a row, one block list per column; a cell given none gets an empty block.
        public void AddRow(List<List<BlockControl>> cells)
        {
            int row = rowDefinitions.Count;
            rowDefinitions.Add(new RowDefinition { sizeMode = GridSizeMode.Auto });

            for (int c = 0; c < widths.Count; c++)
            {
                StackPanelControl cell = new StackPanelControl
                {
                    gridRow = (short)row,
                    gridColumn = (short)c,
                    margin = new Thickness(cellInset * zoom)
                };

                List<BlockControl> blocks = c < cells.Count ? cells[c] : new List<BlockControl>();
                if (blocks.Count == 0)
                {
                    BlockControl empty = new BlockControl();
                    empty.AppendRun(new Run());
                    blocks.Add(empty);
                }

                foreach (BlockControl block in blocks)
                    cell.AddChild(block);
                AddChild(cell);
            }
        }

        // Cells in reading order.
        public List<StackPanelControl> Cells()
        {
            List<StackPanelControl> cells = new List<StackPanelControl>();
            foreach (Entity child in children)
                if (child is StackPanelControl cell) cells.Add(cell);

            return cells;
        }

        // Every cell's blocks in reading order, appended to a document's flat block list.
        internal void AppendBlocks(List<BlockControl> into)
        {
            foreach (Entity child in children)
            {
                if (child is not StackPanelControl cell) continue;

                foreach (Entity entry in cell.children)
                    if (entry is BlockControl block) into.Add(block);
            }
        }

        // First block of the cell after or before the one holding from; null past either end.
        internal BlockControl? StepCell(BlockControl from, int direction)
        {
            List<StackPanelControl> cells = Cells();

            int index = cells.IndexOf(from.parent as StackPanelControl);
            int next = index + direction;
            if (index < 0 || next < 0 || next >= cells.Count) return null;

            foreach (Entity entry in cells[next].children)
                if (entry is BlockControl block) return block;

            return null;
        }

        internal void SetZoom(float value)
        {
            if (zoom != value)
            {
                zoom = value;
                for (int c = 0; c < widths.Count; c++)
                    columnDefinitions[c].value = widths[c] * zoom;
                foreach (StackPanelControl cell in Cells())
                    cell.margin = new Thickness(cellInset * zoom);
                InvalidateLayout();
            }

            foreach (Entity child in children)
            {
                if (child is not StackPanelControl cell) continue;

                foreach (Entity entry in cell.children)
                    if (entry is BlockControl block) block.SetZoom(zoom);
            }
        }

        public void ApplyLayout(DocumentLayout layout)
        {
            foreach (Entity child in children)
            {
                if (child is not StackPanelControl cell) continue;

                foreach (Entity entry in cell.children)
                    if (entry is BlockControl block) block.ApplyLayout(layout);
            }
        }

        #region ---- pages ----
        internal float FirstRowHeight => rowDefinitions.Count > 0 ? rowDefinitions[0].resolvedSize : 0f;

        // Pushes each row that would cross a page break to the next page, through the gap above it;
        // returns the table's height.
        internal float Paginate(float top, PageBands bands)
        {
            float y = top;
            for (int r = 0; r < rowDefinitions.Count; r++)
            {
                if (r > 0)
                {
                    float pushed = bands.Push(y, rowDefinitions[r].resolvedSize);
                    rowDefinitions[r - 1].gapAfter = pushed - y;
                    y = pushed;
                }
                y += rowDefinitions[r].resolvedSize;
            }

            if (rowDefinitions.Count > 0) rowDefinitions[^1].gapAfter = 0f;
            return y - top;
        }
        #endregion

        #region ---- layout ----
        // Measures without page gaps; Paginate sets them after.
        protected override Vector2 MeasureCore(Vector2 availableSize)
        {
            foreach (RowDefinition row in rowDefinitions)
                row.gapAfter = 0f;

            Vector2 desired = base.MeasureCore(availableSize);
            foreach (PanelControl border in borders)
                border.Measure(availableSize);

            return desired;
        }

        protected override void ArrangeCore(LayoutRect finalRect)
        {
            base.ArrangeCore(finalRect);
            ArrangeBorders();
        }

        // Top and left on every cell, right on the last column, bottom where no row follows directly.
        private void ArrangeBorders()
        {
            List<StackPanelControl> cells = Cells();
            int used = 0;

            for (int i = 0; i < cells.Count; i++)
            {
                LayoutRect box = Box(cells[i]);
                bool lastColumn = cells[i].gridColumn == widths.Count - 1;
                int below = i + widths.Count;

                Border(used++).Arrange(new LayoutRect(box.x, box.y, box.width, borderWidth));
                Border(used++).Arrange(new LayoutRect(box.x, box.y, borderWidth, box.height));
                if (lastColumn)
                    Border(used++).Arrange(new LayoutRect(box.Right - borderWidth, box.y, borderWidth, box.height));
                if (below >= cells.Count || Box(cells[below]).y > box.Bottom + 0.5f)
                    Border(used++).Arrange(new LayoutRect(box.x, box.Bottom - borderWidth, box.width, borderWidth));
            }

            for (int i = used; i < borders.Count; i++)
                borders[i].Arrange(LayoutRect.Empty);
        }

        // A cell's rect with its inset added back.
        private static LayoutRect Box(StackPanelControl cell)
        {
            LayoutRect r = cell.arrangedRect;
            Thickness m = cell.margin;
            return new LayoutRect(r.x - m.left, r.y - m.top, r.width + m.totalHorizontal, r.height + m.totalVertical);
        }

        // The border line at index, created on first use.
        private PanelControl Border(int index)
        {
            while (borders.Count <= index)
            {
                PanelControl line = new PanelControl { hitTestable = false };
                line.PaintOr(null, PaletteRole.Line);
                line.parent = this;
                children.Add(line);
                borders.Add(line);
                MarkTreeOrderDirty();
                line.Measure(arrange.measuredOffer);
            }
            return borders[index];
        }
        #endregion
    }
}
