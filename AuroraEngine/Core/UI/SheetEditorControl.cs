using ArctisAurora.Core.Editing;
using ArctisAurora.Core.Filing;
using ArctisAurora.Core.Filing.Serialization;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Numerics;
using System.Text;

namespace ArctisAurora.Core.UI
{
    // One open sheet: the scroll viewport, the grid under it, and the file behind them.
    public class SheetEditorControl : ScrollableControl, IClipboardTarget, IFileEditor
    {
        public SheetDocument document { get; private set; } = null!;
        public string? path { get; private set; }
        public bool unsaved { get; private set; }
        public UndoStack undo { get; } = new UndoStack();

        bool IFileEditor.isDirty => unsaved;

        // selection, in cells
        public int activeRow { get; private set; }
        public int activeColumn { get; private set; }
        public int anchorRow { get; private set; }
        public int anchorColumn { get; private set; }

        // the cell the field is open on
        public bool editing { get; private set; }
        public int editRow { get; private set; }
        public int editColumn { get; private set; }

        private SheetControl? sheet;

        // honoured at the end of Arrange
        private bool scrollToActivePending;
        private SessionTab? pendingView;

        // the layer edits land in: the topmost
        private SheetLayer Layer => sheet!.page.layers[^1];

        public SheetEditorControl()
        {
            scrollDirection = ScrollDirection.Both;
            PaintOr(null, PaletteRole.Surface);
        }

        public void LoadPath(string nameOrPath)
        {
            path = Path.GetFullPath(Path.IsPathRooted(nameOrPath) ? nameOrPath : Paths.Doc(nameOrPath));
            Load(SheetDocument.Load(path));
        }

        public void Load(SheetDocument loaded)
        {
            document = loaded;
            undo.Clear();
            editing = false;
            activeRow = activeColumn = anchorRow = anchorColumn = 0;

            sheet?.Destroy();
            sheet = new SheetControl(this, document.pages[0]);
            sheet.field.onCommit = value => FinishEdit(value, true);
            sheet.field.onBlur = () => { if (editing) FinishEdit(sheet!.field.text, false); };
            sheet.field.onCancel = CancelEdit;
            AddChild(sheet);
        }

        #region ---- file ----
        public void Save()
        {
            if (path == null) return;
            document.Save(path);
            unsaved = false;
        }

        public void Repath(string newPath, string name)
        {
            path = newPath;
            document.name = name;
        }

        // Cells changed under the grid: an edit, an undo or a redo.
        internal void CellsChanged()
        {
            unsaved = true;
            sheet?.CellsChanged();
        }

        public void Undo()
        {
            if (undo.Undo()) RequestScrollToActive();
        }

        public void Redo()
        {
            if (undo.Redo()) RequestScrollToActive();
        }
        #endregion

        #region ---- selection ----
        public void Select(int row, int column, bool extend)
        {
            activeRow = Math.Max(0, row);
            activeColumn = Math.Max(0, column);
            if (!extend)
            {
                anchorRow = activeRow;
                anchorColumn = activeColumn;
            }

            sheet?.InvalidateLayout();
            RequestScrollToActive();
        }

        public void Move(int rows, int columns, bool extend) => Select(activeRow + rows, activeColumn + columns, extend);

        // Commits an open edit, then steps down (up with back).
        public void Enter(bool back)
        {
            if (editing) sheet!.field.Commit();
            Move(back ? -1 : 1, 0, false);
        }

        // Commits an open edit, then steps right (left with back).
        public void Tab(bool back)
        {
            if (editing) sheet!.field.Commit();
            Move(0, back ? -1 : 1, false);
        }

        public void SelectAll()
        {
            (int rows, int columns) = sheet!.page.Used();
            anchorRow = anchorColumn = 0;
            Select(Math.Max(0, rows - 1), Math.Max(0, columns - 1), true);
        }

        private (int top, int left, int bottom, int right) Range() =>
            (Math.Min(anchorRow, activeRow), Math.Min(anchorColumn, activeColumn),
             Math.Max(anchorRow, activeRow), Math.Max(anchorColumn, activeColumn));

        internal void RequestScrollToActive()
        {
            scrollToActivePending = true;
            InvalidateArrange();
        }
        #endregion

        #region ---- editing ----
        // Opens the field on the active cell, holding its text with keep, empty without.
        public void BeginEdit(bool keep)
        {
            if (editing || sheet == null) return;

            editing = true;
            (editRow, editColumn) = (activeRow, activeColumn);

            TextBoxControl field = sheet.field;
            field.text = keep ? Layer.Get(editRow, editColumn) ?? string.Empty : string.Empty;
            UIEngine.SetActiveControl(field);
            field.Focus();
            if (keep) field.MoveCaret(CaretMove.LineEnd, false);

            sheet.InvalidateLayout();
            RequestScrollToActive();
        }

        // Writes chars typed over a cell that is not being edited, replacing what it held.
        public void TypeOver(Queue<char> input)
        {
            if (editing || input.Count == 0) return;

            BeginEdit(false);
            while (input.Count > 0)
                sheet!.field.WriteChar(input.Dequeue());
        }

        private void FinishEdit(string value, bool refocus)
        {
            if (!editing) return;
            editing = false;

            Write("Edit cell", new List<(int, int, string?)> { (editRow, editColumn, value) });
            if (refocus) UIEngine.SetActiveControl(sheet!);
            sheet!.InvalidateLayout();
        }

        private void CancelEdit()
        {
            if (!editing) return;
            editing = false;

            UIEngine.SetActiveControl(sheet!);
            sheet!.InvalidateLayout();
        }

        // Empties every selected cell of the edited layer.
        public void Clear() => Clear("Clear cells");

        private void Clear(string label)
        {
            if (editing || sheet == null) return;

            (int top, int left, int bottom, int right) = Range();
            List<(int, int, string?)> cleared = new List<(int, int, string?)>();
            foreach (long key in Layer.cells.Keys)
            {
                int row = SheetDocument.RowOf(key);
                int column = SheetDocument.ColumnOf(key);
                if (row >= top && row <= bottom && column >= left && column <= right)
                    cleared.Add((row, column, null));
            }

            Write(label, cleared);
        }

        // One undo step setting each cell to its value.
        private void Write(string label, List<(int row, int column, string? value)> cells)
        {
            List<(int, int, string?, string?)> changed = new List<(int, int, string?, string?)>();
            foreach ((int row, int column, string? value) in cells)
            {
                string? before = Layer.Get(row, column);
                string? after = string.IsNullOrEmpty(value) ? null : value;
                if (before != after) changed.Add((row, column, before, after));
            }
            if (changed.Count == 0) return;

            SheetCellEdit edit = new SheetCellEdit(this, Layer, changed);
            using (undo.Begin(label))
            {
                edit.Redo();
                undo.Push(edit);
            }
        }
        #endregion

        #region ---- clipboard ----
        // The selection as tab-separated rows.
        public bool Copy()
        {
            if (editing || sheet == null) return false;

            (int top, int left, int bottom, int right) = Range();
            StringBuilder text = new StringBuilder();
            for (int r = top; r <= bottom; r++)
            {
                for (int c = left; c <= right; c++)
                {
                    if (c > left) text.Append('\t');
                    text.Append(sheet.page.Shown(r, c));
                }
                if (r < bottom) text.Append("\r\n");
            }

            ClipboardText.Set(text.ToString());
            return true;
        }

        public bool Cut()
        {
            if (!Copy()) return false;
            Clear("Cut");
            return true;
        }

        // Tab-separated rows from the active cell, selected after.
        public bool Paste(string text)
        {
            if (editing || sheet == null) return false;

            string[] rows = text.Replace("\r\n", "\n").Split('\n');
            int count = rows.Length > 1 && rows[^1].Length == 0 ? rows.Length - 1 : rows.Length;

            List<(int, int, string?)> cells = new List<(int, int, string?)>();
            int width = 1;
            for (int r = 0; r < count; r++)
            {
                string[] values = rows[r].Split('\t');
                width = Math.Max(width, values.Length);
                for (int c = 0; c < values.Length; c++)
                    cells.Add((activeRow + r, activeColumn + c, values[c]));
            }

            int row = activeRow;
            int column = activeColumn;
            Write("Paste", cells);
            anchorRow = row;
            anchorColumn = column;
            Select(row + count - 1, column + width - 1, true);
            return true;
        }

        public bool PasteImage(Image<Rgba32> image) => false;
        #endregion

        #region ---- view ----
        // Active cell and anchor in the caret/anchor fields, scroll in ScrollX and TopDelta.
        public SessionTab ViewState()
        {
            if (pendingView != null) return pendingView;

            Vector2 scroll = GetScrollOffset();
            return new SessionTab
            {
                caretBlock = activeRow,
                caretOffset = activeColumn,
                anchorBlock = anchorRow,
                anchorOffset = anchorColumn,
                scrollX = scroll.X,
                topDelta = scroll.Y
            };
        }

        public void RestoreView(SessionTab view)
        {
            if (view.caretBlock >= 0)
            {
                anchorRow = Math.Max(0, view.anchorBlock);
                anchorColumn = Math.Max(0, view.anchorOffset);
                activeRow = view.caretBlock;
                activeColumn = Math.Max(0, view.caretOffset);
            }

            pendingView = view;
            sheet?.InvalidateLayout();
            InvalidateArrange();
        }

        // Applies a restored view or scrolls the active cell into view.
        protected override void ArrangeCore(LayoutRect finalRect)
        {
            base.ArrangeCore(finalRect);
            if (sheet == null) return;

            Vector2 before = GetScrollOffset();
            if (pendingView != null)
            {
                SessionTab view = pendingView;
                pendingView = null;
                scrollToActivePending = false;
                SetScrollOffset(new Vector2(view.scrollX, view.topDelta));
            }
            else if (scrollToActivePending)
            {
                scrollToActivePending = false;
                LayoutRect cell = sheet.CellRect(activeRow, activeColumn);
                ScrollIntoView(new LayoutRect(cell.x - SheetControl.headerWidth, cell.y - SheetControl.headerHeight,
                    cell.width + SheetControl.headerWidth, cell.height + SheetControl.headerHeight));
            }

            if (GetScrollOffset() != before) base.ArrangeCore(finalRect);
            else SetFlag(ArrangeFlags.ArrangeDirty, false);
        }
        #endregion
    }
}
