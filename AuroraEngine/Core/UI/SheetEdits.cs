using ArctisAurora.Core.Editing;

namespace ArctisAurora.Core.UI
{
    // Cells of one layer before and after an edit.
    public sealed class SheetCellEdit : IEditRecord
    {
        private readonly SheetDocument document;
        private readonly SheetPage page;
        private readonly SheetLayer layer;
        private readonly List<(int row, int column, string? before, string? after)> cells;

        public SheetCellEdit(SheetDocument document, SheetPage page, SheetLayer layer, List<(int, int, string?, string?)> cells)
        {
            this.document = document;
            this.page = page;
            this.layer = layer;
            this.cells = cells;
        }

        public void Undo()
        {
            foreach ((int row, int column, string? before, _) in cells)
                layer.Set(row, column, before);
            SheetBook.Changed(document, page, cells.Select(cell => (cell.row, cell.column)));
        }

        public void Redo()
        {
            foreach ((int row, int column, _, string? after) in cells)
                layer.Set(row, column, after);
            SheetBook.Changed(document, page, cells.Select(cell => (cell.row, cell.column)));
        }
    }

    // Cell formats of one page before and after an edit.
    public sealed class SheetFormatEdit : IEditRecord
    {
        private readonly SheetDocument document;
        private readonly SheetPage page;
        private readonly List<(int row, int column, SheetFormat before, SheetFormat after)> cells;

        public SheetFormatEdit(SheetDocument document, SheetPage page, List<(int, int, SheetFormat, SheetFormat)> cells)
        {
            this.document = document;
            this.page = page;
            this.cells = cells;
        }

        public void Undo()
        {
            foreach ((int row, int column, SheetFormat before, _) in cells)
                page.SetFormat(row, column, before);
            SheetBook.Changed(document, page, []);
        }

        public void Redo()
        {
            foreach ((int row, int column, _, SheetFormat after) in cells)
                page.SetFormat(row, column, after);
            SheetBook.Changed(document, page, []);
        }
    }

    // One column's width or row's height before and after a drag; null is the default size.
    public sealed class SheetBandEdit : IEditRecord
    {
        private readonly SheetDocument document;
        private readonly SheetPage page;
        private readonly bool column;
        private readonly int index;
        private readonly float? before;
        private readonly float? after;

        public SheetBandEdit(SheetDocument document, SheetPage page, bool column, int index, float? before, float? after)
        {
            this.document = document;
            this.page = page;
            this.column = column;
            this.index = index;
            this.before = before;
            this.after = after;
        }

        public void Undo() => Apply(before);

        public void Redo() => Apply(after);

        private void Apply(float? size)
        {
            Dictionary<int, float> bands = column ? page.columnWidths : page.rowHeights;
            if (size is float value) bands[index] = value;
            else bands.Remove(index);
            SheetBook.Changed(document, page, []);
        }
    }
}
