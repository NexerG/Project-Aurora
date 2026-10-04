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
}
