using ArctisAurora.Core.Registry;
using ArctisAurora.Core.UISystem.Controls.Text.Document;
using ArctisAurora.EngineWork;
// WinForms is enabled in this project; alias the engine enum to avoid the System.Windows.Forms.Keys clash.
using Keys = ArctisAurora.EngineWork.Keys;

namespace ArctisAurora.Core.UISystem.Controls.Text
{
    public enum CaretMove
    {
        Left,
        Right,
        Up,
        Down,
        LineStart,
        LineEnd,
        PageUp,
        PageDown
    }

    // Keybind actions for text editing. They live in the engine rather than in a host so an
    // application declares which keys reach them in XML and writes no input code of its own.
    public static class TextInputActions
    {
        [A_XSDActionDependency("Text.Write", "Input", "Writes queued characters into the focused text control")]
        public static void Write()
        {
            // drain every char polled this tick — the OS repeat rate can outpace the frame rate
            Queue<char> input = InputHandler.charInputReadQueue;
            if (input.Count == 0) return;

            DocumentEditorControl editor = Editor();
            TextControl target = Target(editor);
            if (target == null) return;

            while (input.Count > 0)
                target.WriteChar(input.Dequeue());

            // WriteChar advances cursorPosition on the run itself, so the anchor is left behind and
            // typing would select what it just typed.
            editor?.CollapseSelection();
        }

        // Inside a document the caret's run is the target, never activeControl on its own: merely
        // hovering a glyph repoints activeControl to that glyph's run and drops the previous one out
        // of edit, so the target would follow the mouse instead of the caret and a character typed
        // with the pointer resting elsewhere would be stranded in the queue.
        private static TextControl Target(DocumentEditorControl editor)
        {
            if (editor?.CaretRun != null) return editor.CaretRun;

            TextControl control = UICollisionHandling.activeControl as TextControl;
            return control != null && control.isEditing ? control : null;
        }

        [A_XSDActionDependency("Text.Save", "Input", "Writes the focused note back to the file it was loaded from")]
        public static void Save() => Editor()?.Save();

        [A_XSDActionDependency("Text.CaretLeft", "Input")]
        public static void CaretLeft() => Move(CaretMove.Left);

        [A_XSDActionDependency("Text.CaretRight", "Input")]
        public static void CaretRight() => Move(CaretMove.Right);

        [A_XSDActionDependency("Text.CaretUp", "Input")]
        public static void CaretUp() => Move(CaretMove.Up);

        [A_XSDActionDependency("Text.CaretDown", "Input")]
        public static void CaretDown() => Move(CaretMove.Down);

        [A_XSDActionDependency("Text.CaretLineStart", "Input")]
        public static void CaretLineStart() => Move(CaretMove.LineStart);

        [A_XSDActionDependency("Text.CaretLineEnd", "Input")]
        public static void CaretLineEnd() => Move(CaretMove.LineEnd);

        [A_XSDActionDependency("Text.CaretPageUp", "Input")]
        public static void CaretPageUp() => Move(CaretMove.PageUp);

        [A_XSDActionDependency("Text.CaretPageDown", "Input")]
        public static void CaretPageDown() => Move(CaretMove.PageDown);

        // Shift extends the selection instead of collapsing it onto the new position.
        private static void Move(CaretMove move) => Editor()?.MoveCaret(move,
            InputHandler.instance.IsKeyDown(Keys.LeftShift) || InputHandler.instance.IsKeyDown(Keys.RightShift));

        // Nearest document editor at or above whatever the collision handler last made active.
        private static DocumentEditorControl Editor()
        {
            for (VulkanControl control = UICollisionHandling.activeControl;
                 control != null;
                 control = control.parent as VulkanControl)
                if (control is DocumentEditorControl editor) return editor;

            return null;
        }
    }
}
