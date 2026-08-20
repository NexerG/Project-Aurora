using ArctisAurora.Core.Registry;
using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.UISystem.Controls.Containers;
using ArctisAurora.Core.UISystem.Controls.Text.Document;
using ArctisAurora.EngineWork;
using ArctisAurora.EngineWork.Rendering;
using Silk.NET.Maths;

namespace ArctisAurora.Core.UISystem.Controls.Text.Editing
{
    // A one-line editable field: the text, a caret and a selection box under one control. The text is
    // a child rather than the control itself because a TextControl's children are its glyphs —
    // SyncGlyphs trims everything past the last character, so a caret parented to the text would be
    // discarded by the next keystroke. Same split as DocumentControl and its runs.
    [A_XSDType("TextBox", "UI")]
    public class TextBoxControl : AbstractContainerControl
    {
        // The text itself. One line, and it hands its clicks to the box, which owns the caret.
        private class FieldLine : TextInputControl
        {
            protected override float WrapWidth(float available) => float.MaxValue;

            public override void ResolveOnClick(Vector2D<float> oldPos, Vector2D<float> delta)
            {
                BeginEdit();
                (parent as VulkanControl)?.ResolveOnClick(oldPos, delta);
            }
        }

        public Action<string> onCommit;
        public Action onCancel;

        // selection first: children draw in order, so it lands behind the text
        private readonly SelectionControl selection = new SelectionControl();
        private readonly FieldLine line = new FieldLine();
        private readonly CaretControl caret = new CaretControl();

        // where a selection started; equal to the cursor means nothing is selected
        private int anchor;
        private string committed = string.Empty;

        [A_XSDElementProperty("Text", "UI", "The string being edited.")]
        public string text
        {
            get => line.text;
            set
            {
                line.text = value ?? string.Empty;
                committed = line.text;
                anchor = line.cursorPosition = line.text.Length;
            }
        }

        [A_XSDElementProperty("FontSize", "UI", "Font size in pixels.")]
        public int fontSize
        {
            get => line.fontSize;
            set => line.fontSize = value;
        }

        [A_XSDElementProperty("TextColorHex", "UI", "Colour of the text.")]
        public string textColorHex
        {
            get => line.controlColorHex;
            set => line.controlColorHex = value;
        }

        public bool isEditing => line.isEditing;

        public TextBoxControl()
        {
            clipOutOfBounds = true;
            base.AddChild(selection);
            base.AddChild(line);
            base.AddChild(caret);
        }

        // A box is its three parts; nothing is authored inside it.
        public override void AddChild(Entity entity) =>
            throw new Exception("TextBoxControl takes no children.");

        public void Focus()
        {
            line.BeginEdit();
            SelectAll();
        }

        public void SelectAll()
        {
            anchor = 0;
            line.cursorPosition = line.text.Length;
            InvalidateArrange();
        }

        #region ---- editing ----
        public void WriteChar(char c)
        {
            DeleteSelection();
            line.WriteChar(c);
            anchor = line.cursorPosition;
            InvalidateLayout();
        }

        public void Backspace()
        {
            if (!DeleteSelection()) line.Backspace();
            anchor = line.cursorPosition;
            InvalidateLayout();
        }

        public void Delete()
        {
            if (!DeleteSelection()) line.Delete();
            anchor = line.cursorPosition;
            InvalidateLayout();
        }

        // One line, so the vertical and page moves collapse onto its ends.
        public void MoveCaret(CaretMove move, bool extend)
        {
            switch (move)
            {
                case CaretMove.Left: line.MoveCursorLeft(); break;
                case CaretMove.Right: line.MoveCursorRight(); break;
                case CaretMove.Up:
                case CaretMove.PageUp:
                case CaretMove.LineStart: line.MoveCursorHome(); break;
                default: line.MoveCursorEnd(); break;
            }

            if (!extend) anchor = line.cursorPosition;
            InvalidateArrange();
        }

        public void Commit()
        {
            line.CommitEdit();
            committed = line.text;
            onCommit?.Invoke(committed);
        }

        // Restores what the box held when it was last committed.
        public void Cancel()
        {
            line.CancelEdit();
            line.text = committed;
            anchor = line.cursorPosition = committed.Length;
            onCancel?.Invoke();
            InvalidateLayout();
        }

        private bool DeleteSelection()
        {
            int from = Math.Min(anchor, line.cursorPosition);
            int to = Math.Max(anchor, line.cursorPosition);
            if (from == to) return false;

            line.DeleteAt(from, to - from);
            line.cursorPosition = anchor = from;
            return true;
        }
        #endregion

        #region ---- pointer ----
        public override void ResolveOnClick(Vector2D<float> oldPos, Vector2D<float> delta)
        {
            line.BeginEdit();

            int offset = OffsetUnderPointer();
            line.cursorPosition = offset;
            if (!InputHandler.instance.IsModifierDown(InputModifier.Extend)) anchor = offset;

            StartDrag();
            InvalidateArrange();
        }

        public override void ResolveDrag(Vector2D<float> lastPos, Vector2D<float> delta)
        {
            line.cursorPosition = OffsetUnderPointer();
            InvalidateArrange();
        }

        private int OffsetUnderPointer()
        {
            RenderWindow window = RenderWindow.Of(this);
            if (window == null) return line.cursorPosition;

            Vector2D<float> mouse = window.ui.ToDesignSpace(window.mousePos);
            LayoutRect inner = line.arrangedRect.Shrink(line.padding);
            return line.OffsetAt(mouse.X - inner.x, mouse.Y - inner.y);
        }
        #endregion

        #region ---- layout ----
        public override Vector2D<float> Measure(Vector2D<float> availableSize)
        {
            float w = preferredWidth > 0 ? preferredWidth : MathF.Max(minWidth, availableSize.X);
            float h = preferredHeight > 0 ? preferredHeight : MathF.Max(minHeight, availableSize.Y);

            line.Measure(new Vector2D<float>(float.MaxValue, float.MaxValue));

            DesiredSize = new Vector2D<float>(w, h);
            isMeasureDirty = false;
            return DesiredSize;
        }

        public override void Arrange(LayoutRect finalRect)
        {
            arrangedRect = finalRect;
            WriteArrangedTransform(finalRect);

            ClipRect = parent is VulkanControl parentControl
                ? LayoutRect.Intersect(finalRect, parentControl.ClipRect)
                : finalRect;

            LayoutRect inner = finalRect.Shrink(padding);

            // The text is centred on the box rather than filling it, so a tall box does not push the
            // one line it holds to the top.
            float textHeight = line.DesiredSize.Y;
            LayoutRect textRect = new LayoutRect(inner.x, inner.y + (inner.height - textHeight) * 0.5f,
                MathF.Max(inner.width, line.DesiredSize.X), textHeight);
            line.Arrange(textRect);

            ArrangeCaretAndSelection(textRect);

            isArrangeDirty = false;
        }

        // Both are collapsed to nothing when the box is not being edited — a zero-scale quad draws no
        // pixels, the same way the scrollbar thumb hides itself.
        private void ArrangeCaretAndSelection(LayoutRect textRect)
        {
            if (!line.isEditing)
            {
                caret.Arrange(LayoutRect.Empty);
                selection.Arrange(LayoutRect.Empty);
                return;
            }

            CaretGeometry cursor = line.CaretAt(line.cursorPosition);
            caret.Arrange(new LayoutRect(textRect.x + cursor.x, textRect.y + cursor.top,
                CaretControl.Width, cursor.height));

            if (anchor == line.cursorPosition)
            {
                selection.Arrange(LayoutRect.Empty);
                return;
            }

            CaretGeometry other = line.CaretAt(anchor);
            float left = MathF.Min(cursor.x, other.x);
            float right = MathF.Max(cursor.x, other.x);
            selection.Arrange(new LayoutRect(textRect.x + left, textRect.y + cursor.top,
                right - left, cursor.height));
        }
        #endregion
    }
}
