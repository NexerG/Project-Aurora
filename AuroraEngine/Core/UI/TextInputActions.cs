using ArctisAurora.Core.Registry;
using ArctisAurora.EngineWork;

namespace ArctisAurora.Core.UI
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

            DocumentEditorControl next = Editor();
            if (next != null)
            {
                // one step per character, so one press is one undo however many the queue held
                while (input.Count > 0)
                    using (next.BeginStep("Typing"))
                    {
                        next.DeleteSelection();
                        next.TypeChar(input.Dequeue());
                    }

                next.MarkDirty();
                return;
            }

            TextBoxControl nextBox = Box();
            if (nextBox == null) return;

            while (input.Count > 0)
                nextBox.WriteChar(input.Dequeue());
        }

        [A_XSDActionDependency("Text.Backspace", "Input", "Deletes the selection, or the character before the caret")]
        public static void Backspace()
        {
            DocumentEditorControl next = Editor();
            if (next != null) { next.Backspace(); return; }

            Box()?.Backspace();
        }

        [A_XSDActionDependency("Text.Delete", "Input", "Deletes the selection, or the character after the caret")]
        public static void Delete()
        {
            DocumentEditorControl next = Editor();
            if (next != null) { next.Delete(); return; }

            Box()?.Delete();
        }

        [A_XSDActionDependency("Text.NewBlock", "Input", "Splits the caret's block in two, or commits a standalone field")]
        public static void NewBlock()
        {
            DocumentEditorControl next = Editor();
            if (next != null) { next.SplitBlock(); return; }

            Box()?.Commit();
        }

        [A_XSDActionDependency("Text.SelectAll", "Input", "Selects the whole of the focused note or field")]
        public static void SelectAll()
        {
            DocumentEditorControl next = Editor();
            if (next != null) { next.SelectAll(); return; }

            Box()?.SelectAll();
        }

        [A_XSDActionDependency("Text.Bold", "Input", "Toggles bold over the selection, or for what is typed next")]
        public static void Bold() => Toggle(style => new StyleDelta(bold: !style.bold));

        [A_XSDActionDependency("Text.Italic", "Input", "Toggles italic over the selection, or for what is typed next")]
        public static void Italic() => Toggle(style => new StyleDelta(italic: !style.italic));

        // The state comes off the caret's style, so a toggle is what that is not.
        private static void Toggle(Func<CaretStyle, StyleDelta> nextDelta)
        {
            DocumentEditorControl next = Editor();
            CaretStyle? nextSource = next?.StyleSource;
            if (nextSource != null) next.ApplyStyle(nextDelta(nextSource.Value));
        }

        [A_XSDActionDependency("Text.Undo", "Input", "Reverses the last edit made to the focused note")]
        public static void Undo() => Editor()?.Undo();

        [A_XSDActionDependency("Text.Redo", "Input", "Reapplies the last edit undone in the focused note")]
        public static void Redo() => Editor()?.Redo();

        [A_XSDActionDependency("Text.Cancel", "Input", "Abandons the edit in a standalone field and restores what it held")]
        public static void Cancel() => Box()?.Cancel();

        [A_XSDActionDependency("Text.Save", "Input", "Writes the focused note back to the file it was loaded from")]
        public static void Save() => Editor()?.SaveNamed();

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

        // The Extend modifier keeps the anchor instead of collapsing it onto the new position.
        private static void Move(CaretMove move)
        {
            DocumentEditorControl next = Editor();
            if (next != null)
            {
                next.MoveCaret(move, InputHandler.instance.IsModifierDown(InputModifier.Extend));
                return;
            }

            Box()?.MoveCaret(move, InputHandler.instance.IsModifierDown(InputModifier.Extend));
        }

        // Nearest document editor at or above whatever the new stack last made active.
        internal static DocumentEditorControl Editor()
        {
            for (ArctisAurora.Core.UI.Control control = UIEngine.activeControl;
                 control != null;
                 control = control.parent as ArctisAurora.Core.UI.Control)
                if (control is DocumentEditorControl editor) return editor;

            return null;
        }

        // Nearest editing field at or above whatever the new stack last made active.
        private static TextBoxControl Box()
        {
            for (ArctisAurora.Core.UI.Control control = UIEngine.activeControl;
                 control != null;
                 control = control.parent as ArctisAurora.Core.UI.Control)
                if (control is TextBoxControl box) return box.isEditing ? box : null;

            return null;
        }
    }
}
