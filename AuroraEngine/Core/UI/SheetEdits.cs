using ArctisAurora.Core.Editing;

namespace ArctisAurora.Core.UI
{
    // Cells of one layer before and after an edit.
    public sealed class SheetCellEdit : IEditRecord
    {
        private readonly SheetEditorControl editor;
        private readonly SheetLayer layer;
        private readonly List<(int row, int column, string? before, string? after)> cells;

        public SheetCellEdit(SheetEditorControl editor, SheetLayer layer, List<(int, int, string?, string?)> cells)
        {
            this.editor = editor;
            this.layer = layer;
            this.cells = cells;
        }

        public void Undo()
        {
            foreach ((int row, int column, string? before, _) in cells)
                layer.Set(row, column, before);
            editor.CellsChanged();
        }

        public void Redo()
        {
            foreach ((int row, int column, _, string? after) in cells)
                layer.Set(row, column, after);
            editor.CellsChanged();
        }
    }
}
