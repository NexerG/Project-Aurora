using ArctisAurora.Core.Registry;
using ArctisAurora.EngineWork;

namespace ArctisAurora.Core.UI
{
    // Keybind actions for sheets. Each does nothing unless a sheet holds the active control.
    public static class SheetActions
    {
        #region ---- moving ----
        [A_XSDActionDependency("Sheet.Up", "Input", "Moves the active cell up; Shift extends the selection")]
        public static void Up() => Move(-1, 0);

        [A_XSDActionDependency("Sheet.Down", "Input", "Moves the active cell down; Shift extends the selection")]
        public static void Down() => Move(1, 0);

        [A_XSDActionDependency("Sheet.Left", "Input", "Moves the active cell left; Shift extends the selection")]
        public static void Left() => Move(0, -1);

        [A_XSDActionDependency("Sheet.Right", "Input", "Moves the active cell right; Shift extends the selection")]
        public static void Right() => Move(0, 1);

        private static void Move(int rows, int columns)
        {
            SheetEditorControl? sheet = Editor();
            if (sheet == null || sheet.editing) return;

            sheet.Move(rows, columns, InputHandler.instance.IsModifierDown(InputModifier.Extend));
        }

        [A_XSDActionDependency("Sheet.Enter", "Input", "Commits the cell being edited and steps down; Shift steps up")]
        public static void Enter() => Editor()?.Enter(InputHandler.instance.IsModifierDown(InputModifier.Extend));

        [A_XSDActionDependency("Sheet.Tab", "Input", "Commits the cell being edited and steps right")]
        public static void Tab() => Editor()?.Tab(false);

        [A_XSDActionDependency("Sheet.TabBack", "Input", "Commits the cell being edited and steps left")]
        public static void TabBack() => Editor()?.Tab(true);

        [A_XSDActionDependency("Sheet.SelectAll", "Input", "Selects every used cell of the sheet")]
        public static void SelectAll()
        {
            SheetEditorControl? sheet = Editor();
            if (sheet != null && !sheet.editing) sheet.SelectAll();
        }
        #endregion

        #region ---- editing ----
        [A_XSDActionDependency("Sheet.Write", "Input", "Starts editing the active cell with the typed characters")]
        public static void Write() => Editor()?.TypeOver(InputHandler.charInputReadQueue);

        [A_XSDActionDependency("Sheet.Edit", "Input", "Edits the active cell, keeping its text")]
        public static void Edit() => Editor()?.BeginEdit(true);

        [A_XSDActionDependency("Sheet.Clear", "Input", "Empties the selected cells")]
        public static void Clear() => Editor()?.Clear();

        [A_XSDActionDependency("Sheet.Undo", "Input", "Reverses the last change to the sheet")]
        public static void Undo()
        {
            SheetEditorControl? sheet = Editor();
            if (sheet != null && !sheet.editing) sheet.Undo();
        }

        [A_XSDActionDependency("Sheet.Redo", "Input", "Reapplies the last change undone in the sheet")]
        public static void Redo()
        {
            SheetEditorControl? sheet = Editor();
            if (sheet != null && !sheet.editing) sheet.Redo();
        }

        [A_XSDActionDependency("Sheet.Save", "Input", "Writes the sheet back to its file")]
        public static void Save() => Editor()?.Save();
        #endregion

        // Nearest sheet at or above the active control.
        internal static SheetEditorControl? Editor()
        {
            for (Control? control = UIEngine.activeControl; control != null; control = control.parent as Control)
                if (control is SheetEditorControl sheet) return sheet;
            return null;
        }
    }
}
