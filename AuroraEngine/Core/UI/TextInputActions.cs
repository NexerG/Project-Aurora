using ArctisAurora.Core.Filing;
using ArctisAurora.Core.Registry;
using ArctisAurora.EngineWork;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

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
        PageDown,
        WordLeft,
        WordRight,
        DocumentStart,
        DocumentEnd
    }

    // A control Ctrl+C/X/V reach. False passes the request on to the control above.
    public interface IClipboardTarget
    {
        bool Copy();
        bool Cut();
        bool Paste(string text);
        bool PasteImage(Image<Rgba32> image);
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
            bool word = InputHandler.instance.IsModifierDown(InputModifier.Word);

            DocumentEditorControl next = Editor();
            if (next != null) { next.Backspace(word); return; }

            Box()?.Backspace(word);
        }

        [A_XSDActionDependency("Text.Delete", "Input", "Deletes the selection, or the character after the caret")]
        public static void Delete()
        {
            bool word = InputHandler.instance.IsModifierDown(InputModifier.Word);

            DocumentEditorControl next = Editor();
            if (next != null) { next.Delete(word); return; }

            Box()?.Delete(word);
        }

        [A_XSDActionDependency("Text.Copy", "Input", "Copies the selection to the clipboard")]
        public static void Copy() => ToClipboardTarget(target => target.Copy());

        [A_XSDActionDependency("Text.Cut", "Input", "Moves the selection to the clipboard")]
        public static void Cut() => ToClipboardTarget(target => target.Cut());

        [A_XSDActionDependency("Text.Paste", "Input", "Inserts the clipboard's text at the caret")]
        public static void Paste()
        {
            string? text = ClipboardText.Get();
            if (!string.IsNullOrEmpty(text))
            {
                ToClipboardTarget(target => target.Paste(text));
                return;
            }

            if (!ClipboardImage.TryGet(out Image<Rgba32>? image)) return;
            using (image)
                ToClipboardTarget(target => target.PasteImage(image));
        }

        // Walks up from the active control until a target handles the request.
        private static void ToClipboardTarget(Func<IClipboardTarget, bool> request)
        {
            for (ArctisAurora.Core.UI.Control control = UIEngine.activeControl;
                 control != null;
                 control = control.parent as ArctisAurora.Core.UI.Control)
                if (control is IClipboardTarget target && request(target)) return;
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

        [A_XSDActionDependency("Text.Underline", "Input", "Toggles underline over the selection, or for what is typed next")]
        public static void Underline() => Toggle(style => new StyleDelta(underline: !style.underline));

        #region ---- list markers ----
        [A_XSDActionDependency("List.Disc", "Input", "Marks the caret's list level with filled circles")]
        public static void MarkDisc() => Editor()?.SetListMarker(ListMarker.Disc);

        [A_XSDActionDependency("List.Circle", "Input", "Marks the caret's list level with empty circles")]
        public static void MarkCircle() => Editor()?.SetListMarker(ListMarker.Circle);

        [A_XSDActionDependency("List.Triangle", "Input", "Marks the caret's list level with filled triangles")]
        public static void MarkTriangle() => Editor()?.SetListMarker(ListMarker.Triangle);

        [A_XSDActionDependency("List.TriangleOutline", "Input", "Marks the caret's list level with empty triangles")]
        public static void MarkTriangleOutline() => Editor()?.SetListMarker(ListMarker.TriangleOutline);

        [A_XSDActionDependency("List.Square", "Input", "Marks the caret's list level with filled squares")]
        public static void MarkSquare() => Editor()?.SetListMarker(ListMarker.Square);

        [A_XSDActionDependency("List.SquareOutline", "Input", "Marks the caret's list level with empty squares")]
        public static void MarkSquareOutline() => Editor()?.SetListMarker(ListMarker.SquareOutline);

        [A_XSDActionDependency("List.Decimal", "Input", "Numbers the caret's list level 1, 2, 3")]
        public static void MarkDecimal() => Editor()?.SetListMarker(ListMarker.Decimal);

        [A_XSDActionDependency("List.UpperAlpha", "Input", "Numbers the caret's list level A, B, C")]
        public static void MarkUpperAlpha() => Editor()?.SetListMarker(ListMarker.UpperAlpha);

        [A_XSDActionDependency("List.LowerAlpha", "Input", "Numbers the caret's list level a, b, c")]
        public static void MarkLowerAlpha() => Editor()?.SetListMarker(ListMarker.LowerAlpha);

        [A_XSDActionDependency("List.LowerRoman", "Input", "Numbers the caret's list level i, ii, iii")]
        public static void MarkLowerRoman() => Editor()?.SetListMarker(ListMarker.LowerRoman);

        [A_XSDActionDependency("List.UpperRoman", "Input", "Numbers the caret's list level I, II, III")]
        public static void MarkUpperRoman() => Editor()?.SetListMarker(ListMarker.UpperRoman);
        #endregion

        #region ---- picture wrap ----
        [A_XSDActionDependency("Picture.WrapInline", "Input", "Puts the selected picture in line with the text")]
        public static void WrapInline() => Editor()?.SetPictureWrap(PictureWrap.Inline);

        [A_XSDActionDependency("Picture.WrapSquare", "Input", "Wraps text around the selected picture's box")]
        public static void WrapSquare() => Editor()?.SetPictureWrap(PictureWrap.Square);

        [A_XSDActionDependency("Picture.WrapTight", "Input", "Wraps text around the selected picture's opaque outline")]
        public static void WrapTight() => Editor()?.SetPictureWrap(PictureWrap.Tight);

        [A_XSDActionDependency("Picture.WrapTopAndBottom", "Input", "Keeps text above and below the selected picture only")]
        public static void WrapTopAndBottom() => Editor()?.SetPictureWrap(PictureWrap.TopAndBottom);

        [A_XSDActionDependency("Picture.WrapBehind", "Input", "Puts the selected picture behind the text")]
        public static void WrapBehind() => Editor()?.SetPictureWrap(PictureWrap.Behind);

        [A_XSDActionDependency("Picture.WrapInFront", "Input", "Puts the selected picture in front of the text")]
        public static void WrapInFront() => Editor()?.SetPictureWrap(PictureWrap.InFront);

        [A_XSDActionDependency("Picture.CollideBox", "Input", "Wraps text around the turned picture's bounding box")]
        public static void CollideBox() => Editor()?.SetPictureCollision(PictureCollision.Box);

        [A_XSDActionDependency("Picture.CollideShape", "Input", "Wraps text around the turned picture's own shape")]
        public static void CollideShape() => Editor()?.SetPictureCollision(PictureCollision.Shape);
        #endregion

        [A_XSDActionDependency("Text.Indent", "Input", "Nests the list items under the caret one level deeper")]
        public static void Indent() => Editor()?.ShiftListLevel(1);

        [A_XSDActionDependency("Text.Outdent", "Input", "Moves the list items under the caret one level out")]
        public static void Outdent() => Editor()?.ShiftListLevel(-1);

        // The state comes off the caret's style, so a toggle is what that is not.
        private static void Toggle(Func<CaretStyle, StyleDelta> nextDelta)
        {
            DocumentEditorControl next = Editor();
            CaretStyle? nextSource = next?.StyleSource;
            if (nextSource != null) next.ApplyStyle(nextDelta(nextSource.Value));
        }

        [A_XSDActionDependency("Text.Undo", "Input", "Reverses the last edit made to the focused note")]
        public static void Undo()
        {
            DocumentEditorControl next = Editor();
            if (next != null) { next.Undo(); return; }

            Box()?.Undo();
        }

        [A_XSDActionDependency("Text.Redo", "Input", "Reapplies the last edit undone in the focused note")]
        public static void Redo()
        {
            DocumentEditorControl next = Editor();
            if (next != null) { next.Redo(); return; }

            Box()?.Redo();
        }

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

        // The Extend modifier keeps the anchor instead of collapsing it onto the new position; the
        // Word modifier turns character moves into word moves and line ends into document ends.
        private static void Move(CaretMove move)
        {
            if (InputHandler.instance.IsModifierDown(InputModifier.Word))
                move = move switch
                {
                    CaretMove.Left => CaretMove.WordLeft,
                    CaretMove.Right => CaretMove.WordRight,
                    CaretMove.LineStart => CaretMove.DocumentStart,
                    CaretMove.LineEnd => CaretMove.DocumentEnd,
                    _ => move
                };

            DocumentEditorControl next = Editor();
            if (next != null)
            {
                next.DisarmStyle();
                next.MoveCaret(move, InputHandler.instance.IsModifierDown(InputModifier.Extend));
                return;
            }

            Box()?.MoveCaret(move, InputHandler.instance.IsModifierDown(InputModifier.Extend));
        }

        #region ---- word boundaries ----
        internal enum CharClass { Space, Word, Symbol }

        internal static CharClass ClassOf(char c) =>
            char.IsWhiteSpace(c) ? CharClass.Space
            : char.IsLetterOrDigit(c) || c == '_' ? CharClass.Word
            : CharClass.Symbol;

        // Where a word move from an offset lands inside one string.
        internal static int WordEdge(string s, int offset, int direction)
        {
            int i = Math.Clamp(offset, 0, s.Length);

            if (direction > 0)
            {
                if (i < s.Length && ClassOf(s[i]) is CharClass run && run != CharClass.Space)
                    while (i < s.Length && ClassOf(s[i]) == run) i++;
                while (i < s.Length && ClassOf(s[i]) == CharClass.Space) i++;
                return i;
            }

            while (i > 0 && ClassOf(s[i - 1]) == CharClass.Space) i--;
            if (i > 0)
            {
                CharClass run = ClassOf(s[i - 1]);
                while (i > 0 && ClassOf(s[i - 1]) == run) i--;
            }
            return i;
        }
        #endregion

        // Nearest editor at or above the active control; none past an editing field.
        internal static DocumentEditorControl Editor()
        {
            for (ArctisAurora.Core.UI.Control control = UIEngine.activeControl;
                 control != null;
                 control = control.parent as ArctisAurora.Core.UI.Control)
            {
                if (control is TextBoxControl { isEditing: true }) return null;
                if (control is DocumentEditorControl editor) return editor;
            }

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
