using ArctisAurora.Core.Registry;
using ArctisAurora.Core.UI;
using ArctisAurora.Core.UISystem.Controls.Text.Document;
using ArctisAurora.Core.UISystem.Controls.Text.Editing;
using ArctisAurora.EngineWork;

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

            DocumentEditorControl editor = FocusedEditor();
            if (editor == null)
            {
                NextDocumentEditorControl next = NextEditor();
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

                NextTextBoxControl nextBox = NextBox();
                if (nextBox != null)
                {
                    while (input.Count > 0)
                        nextBox.WriteChar(input.Dequeue());
                    return;
                }

                TextBoxControl box = Box();
                if (box == null) return;

                while (input.Count > 0)
                    box.WriteChar(input.Dequeue());
                return;
            }

            // one step per character, so one press is one undo however many the queue held
            while (input.Count > 0)
                using (editor.BeginStep("Typing"))
                {
                    editor.DeleteSelection();

                    TextControl target = Target(editor);
                    if (target == null) return;

                    editor.TypeChar(target, input.Dequeue());

                    // WriteChar advances cursorPosition on the run itself, so the anchor is left
                    // behind — and the next iteration's DeleteSelection would eat what was typed.
                    editor.CollapseSelection();
                }

            editor.MarkDirty();
        }

        // Inside a document the caret's run is the target; activeControl is the standalone-input
        // fallback.
        private static TextControl Target(DocumentEditorControl editor)
        {
            if (editor?.CaretRun != null) return editor.CaretRun;

            TextControl control = UICollisionHandling.activeControl as TextControl;
            return control != null && control.isEditing ? control : null;
        }

        [A_XSDActionDependency("Text.Backspace", "Input", "Deletes the selection, or the character before the caret")]
        public static void Backspace()
        {
            DocumentEditorControl editor = FocusedEditor();
            if (editor != null) { editor.Backspace(); return; }

            NextDocumentEditorControl next = NextEditor();
            if (next != null) { next.Backspace(); return; }

            NextTextBoxControl nextBox = NextBox();
            if (nextBox != null) { nextBox.Backspace(); return; }

            Box()?.Backspace();
        }

        [A_XSDActionDependency("Text.Delete", "Input", "Deletes the selection, or the character after the caret")]
        public static void Delete()
        {
            DocumentEditorControl editor = FocusedEditor();
            if (editor != null) { editor.Delete(); return; }

            NextDocumentEditorControl next = NextEditor();
            if (next != null) { next.Delete(); return; }

            NextTextBoxControl nextBox = NextBox();
            if (nextBox != null) { nextBox.Delete(); return; }

            Box()?.Delete();
        }

        [A_XSDActionDependency("Text.NewBlock", "Input", "Splits the caret's block in two, or commits a standalone field")]
        public static void NewBlock()
        {
            DocumentEditorControl editor = FocusedEditor();
            if (editor != null) { editor.SplitBlock(); return; }

            NextDocumentEditorControl next = NextEditor();
            if (next != null) { next.SplitBlock(); return; }

            NextTextBoxControl nextBox = NextBox();
            if (nextBox != null) { nextBox.Commit(); return; }

            Box()?.Commit();
        }

        [A_XSDActionDependency("Text.SelectAll", "Input", "Selects the whole of the focused note or field")]
        public static void SelectAll()
        {
            DocumentEditorControl editor = FocusedEditor();
            if (editor != null) { editor.SelectAll(); return; }

            NextDocumentEditorControl next = NextEditor();
            if (next != null) { next.SelectAll(); return; }

            NextTextBoxControl nextBox = NextBox();
            if (nextBox != null) { nextBox.SelectAll(); return; }

            Box()?.SelectAll();
        }

        [A_XSDActionDependency("Text.Bold", "Input", "Toggles bold over the selection, or for what is typed next")]
        public static void Bold() => Toggle(style => new StyleDelta(bold: !style.bold),
                                            style => new NextStyleDelta(bold: !style.bold));

        [A_XSDActionDependency("Text.Italic", "Input", "Toggles italic over the selection, or for what is typed next")]
        public static void Italic() => Toggle(style => new StyleDelta(italic: !style.italic),
                                              style => new NextStyleDelta(italic: !style.italic));

        // The state comes off the caret's style, so a toggle is what that is not. Each stack
        // declares a caret style and a delta of its own, so each reads the toggle for itself.
        private static void Toggle(Func<CaretStyle, StyleDelta> delta,
            Func<NextCaretStyle, NextStyleDelta> nextDelta)
        {
            DocumentEditorControl editor = FocusedEditor();
            if (editor != null)
            {
                CaretStyle? source = editor.StyleSource;
                if (source != null) editor.ApplyStyle(delta(source.Value));
                return;
            }

            NextDocumentEditorControl next = NextEditor();
            NextCaretStyle? nextSource = next?.StyleSource;
            if (nextSource != null) next.ApplyStyle(nextDelta(nextSource.Value));
        }

        [A_XSDActionDependency("Text.Undo", "Input", "Reverses the last edit made to the focused note")]
        public static void Undo()
        {
            DocumentEditorControl editor = FocusedEditor();
            if (editor != null) { editor.Undo(); return; }

            NextEditor()?.Undo();
        }

        [A_XSDActionDependency("Text.Redo", "Input", "Reapplies the last edit undone in the focused note")]
        public static void Redo()
        {
            DocumentEditorControl editor = FocusedEditor();
            if (editor != null) { editor.Redo(); return; }

            NextEditor()?.Redo();
        }

        [A_XSDActionDependency("Text.Cancel", "Input", "Abandons the edit in a standalone field and restores what it held")]
        public static void Cancel()
        {
            NextTextBoxControl nextBox = NextBox();
            if (nextBox != null) { nextBox.Cancel(); return; }

            Box()?.Cancel();
        }

        [A_XSDActionDependency("Text.Save", "Input", "Writes the focused note back to the file it was loaded from")]
        public static void Save()
        {
            DocumentEditorControl editor = FocusedEditor();
            if (editor != null) { editor.SaveNamed(); return; }

            // No naming prompt on the new stack until the windows land at 6c2; an unnamed note is
            // written under the file name it already has.
            NextEditor()?.Save();
        }

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
            DocumentEditorControl editor = FocusedEditor();
            if (editor != null)
            {
                editor.MoveCaret(move, InputHandler.instance.IsModifierDown(InputModifier.Extend));
                return;
            }

            NextDocumentEditorControl next = NextEditor();
            if (next != null)
            {
                next.MoveCaret(move, InputHandler.instance.IsModifierDown(InputModifier.Extend));
                return;
            }

            NextTextBoxControl nextBox = NextBox();
            if (nextBox != null)
            {
                nextBox.MoveCaret(move, InputHandler.instance.IsModifierDown(InputModifier.Extend));
                return;
            }

            Box()?.MoveCaret(move, InputHandler.instance.IsModifierDown(InputModifier.Extend));
        }

        // Nearest document editor at or above whatever the new stack last made active.
        internal static NextDocumentEditorControl NextEditor()
        {
            for (ArctisAurora.Core.UI.Control control = UIEngine.activeControl;
                 control != null;
                 control = control.parent as ArctisAurora.Core.UI.Control)
                if (control is NextDocumentEditorControl editor) return editor;

            return null;
        }

        // The new stack's field, resolved off its own active-control context. Tried before Box, and
        // only one of the two stacks ever holds a field.
        private static NextTextBoxControl NextBox()
        {
            for (ArctisAurora.Core.UI.Control control = UIEngine.activeControl;
                 control != null;
                 control = control.parent as ArctisAurora.Core.UI.Control)
                if (control is NextTextBoxControl box) return box.isEditing ? box : null;

            return null;
        }

        // Nearest standalone field at or above whatever the collision handler last made active. The
        // document path is checked first everywhere, so a field inside a note could not shadow it.
        private static TextBoxControl Box()
        {
            for (VulkanControl control = UICollisionHandling.activeControl;
                 control != null;
                 control = control.parent as VulkanControl)
                if (control is TextBoxControl box) return box.isEditing ? box : null;

            return null;
        }

        // Nearest document editor at or above whatever the collision handler last made active.
        // Internal because the format bar resolves its target the same way — its buttons leave the
        // active control alone, so the caret's run is still what this walk starts from.
        internal static DocumentEditorControl FocusedEditor()
        {
            for (VulkanControl control = UICollisionHandling.activeControl;
                 control != null;
                 control = control.parent as VulkanControl)
                if (control is DocumentEditorControl editor) return editor;

            return null;
        }
    }
}
