using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.Registry;
using ArctisAurora.EngineWork;
using System.Numerics;

namespace ArctisAurora.Core.UI
{
    // A one-line editable field: the text, a caret and a selection box under one control.
    [A_XSDType("TextBox", "UI")]
    public class TextBoxControl : ContainerControl, IContext
    {
        // The text itself, handing its presses to the box, which owns the caret.
        private class FieldLine : TextRunControl
        {
            public override Control? ActiveContextTarget() => (parent as Control)?.ActiveContextTarget();
        }

        public Action<string>? onCommit;
        public Action? onCancel;
        public Action? onBlur;

        // selection first: children draw in order, so it lands behind the text
        private readonly PanelControl selection = new PanelControl();
        private readonly FieldLine line = new FieldLine();
        private readonly CaretControl caret = new CaretControl();

        // where a selection started; equal to the cursor means nothing is selected
        private int anchor;
        private int cursor;
        private string committed = string.Empty;

        public bool isEditing { get; private set; }

        [A_XSDElementProperty("Text", "UI", "The string being edited.")]
        public string text
        {
            get => line.text;
            set
            {
                line.text = value ?? string.Empty;
                committed = line.text;
                anchor = cursor = line.text.Length;
            }
        }

        [A_XSDElementProperty("FontSize", "UI", "Font size in pixels.")]
        public int fontSize
        {
            get => line.fontSize;
            set => line.fontSize = value;
        }

        [A_XSDElementProperty("TextColorHex", "UI", "Colour of the text.")]
        public string? textColorHex
        {
            get => line.colorHex;
            set => PaintText(value, PaletteRole.Ink);
        }

        [A_XSDElementProperty("SelectionColorHex", "UI", "Ground of the selected range.")]
        public string? selectionColorHex
        {
            get => selection.colorHex;
            set => selection.PaintOr(value, PaletteRole.Line);
        }

        [A_XSDElementProperty("CaretColorHex", "UI", "Colour of the insertion point.")]
        public string? caretColorHex
        {
            get => caret.colorHex;
            set => caret.PaintOr(value, PaletteRole.Ink);
        }

        public TextBoxControl()
        {
            clipOutOfBounds = true;

            selection.hitTestable = false;
            selection.role = PaletteRole.Line;
            caret.hitTestable = false;

            base.AddChild(selection);
            base.AddChild(line);
            base.AddChild(caret);

            caret.Blur();
        }

        // Paints the text an authored hex, or the role when there is none.
        public void PaintText(string? hex, PaletteRole fallback) => line.PaintOr(hex, fallback);

        // A box is its three parts; nothing is authored inside it.
        public override void AddChild(Entity entity) =>
            throw new Exception("TextBoxControl takes no children.");

        public void Focus()
        {
            isEditing = true;
            caret.Focus();
            SelectAll();
        }

        public void SelectAll()
        {
            anchor = 0;
            cursor = line.text.Length;
            InvalidateArrange();
        }

        #region ---- editing ----
        public void WriteChar(char c)
        {
            if (c == '\0') return;

            DeleteSelection();
            line.text = line.text[..cursor] + c + line.text[cursor..];
            cursor++;
            anchor = cursor;
            InvalidateLayout();
        }

        public void Backspace()
        {
            if (!DeleteSelection() && cursor > 0)
            {
                cursor--;
                line.text = line.text[..cursor] + line.text[(cursor + 1)..];
            }
            anchor = cursor;
            InvalidateLayout();
        }

        public void Delete()
        {
            if (!DeleteSelection() && cursor < line.text.Length)
                line.text = line.text[..cursor] + line.text[(cursor + 1)..];

            anchor = cursor;
            InvalidateLayout();
        }

        // One line, so the vertical and page moves collapse onto its ends.
        public void MoveCaret(CaretMove move, bool extend)
        {
            switch (move)
            {
                case CaretMove.Left: if (cursor > 0) cursor--; break;
                case CaretMove.Right: if (cursor < line.text.Length) cursor++; break;
                case CaretMove.Up:
                case CaretMove.PageUp:
                case CaretMove.LineStart: cursor = 0; break;
                default: cursor = line.text.Length; break;
            }

            if (!extend) anchor = cursor;
            InvalidateArrange();
        }

        public void Commit()
        {
            isEditing = false;
            caret.Blur();
            committed = line.text;
            onCommit?.Invoke(committed);
        }

        // Restores what the box held when it was last committed.
        public void Cancel()
        {
            isEditing = false;
            caret.Blur();
            line.text = committed;
            anchor = cursor = committed.Length;
            onCancel?.Invoke();
            InvalidateLayout();
        }

        private bool DeleteSelection()
        {
            int from = Math.Min(anchor, cursor);
            int to = Math.Max(anchor, cursor);
            if (from == to) return false;

            line.text = line.text[..from] + line.text[to..];
            cursor = anchor = from;
            return true;
        }
        #endregion

        #region ---- focus ----
        public void OnContextAdded(string context) { }

        public void OnContextRemoved(string context)
        {
            if (context == "ActiveControl") LoseFocus();
        }

        // Raised only when the context went somewhere outside the box. The session ends here rather
        // than in the handler, so the caret leaves with the focus whatever onBlur decides to do.
        private void LoseFocus()
        {
            for (Control control = UIEngine.activeControl; control != null; control = control.parent as Control)
                if (ReferenceEquals(control, this)) return;

            isEditing = false;
            caret.Blur();
            InvalidateArrange();
            onBlur?.Invoke();
        }
        #endregion

        #region ---- pointer ----
        public override bool OnPointerPress(PointerEvent e)
        {
            if (e.button != PointerEvent.leftButton) return false;

            isEditing = true;
            caret.Focus();

            cursor = line.IndexAt(e.point);
            if (!InputHandler.instance.IsModifierDown(InputModifier.Extend)) anchor = cursor;

            InvalidateArrange();
            return true;
        }
        #endregion

        #region ---- layout ----
        protected override Vector2 MeasureCore(Vector2 availableSize)
        {
            float w = preferredWidth > 0 ? preferredWidth : MathF.Max(minWidth, availableSize.X);
            float h = preferredHeight > 0 ? preferredHeight : MathF.Max(minHeight, availableSize.Y);

            line.Measure(new Vector2(float.MaxValue, float.MaxValue));

            arrange.desired = new Vector2(w, h);
            SetFlag(ArrangeFlags.MeasureDirty, false);
            return arrange.desired;
        }

        protected override void ArrangeCore(LayoutRect finalRect)
        {
            WriteArranged(finalRect);

            LayoutRect inner = finalRect.Shrink(padding);

            // The text is centred on the box rather than filling it, so a tall box does not push the
            // one line it holds to the top.
            float textHeight = line.DesiredSize.Y;
            line.Arrange(new LayoutRect(inner.x, inner.y + (inner.height - textHeight) * 0.5f,
                MathF.Max(inner.width, line.DesiredSize.X), textHeight));

            ArrangeCaretAndSelection();

            SetFlag(ArrangeFlags.ArrangeDirty, false);
        }

        // Both collapse to nothing when the box is not being edited — a zero-area quad neither draws
        // nor hit-tests.
        private void ArrangeCaretAndSelection()
        {
            if (!isEditing)
            {
                caret.Arrange(LayoutRect.Empty);
                selection.Arrange(LayoutRect.Empty);
                return;
            }

            Vector2 origin = line.TextOrigin;
            CaretGeometry at = line.CaretAt(cursor);
            caret.Arrange(new LayoutRect(origin.X + at.x, origin.Y + at.top, CaretControl.Width, at.height));

            if (anchor == cursor)
            {
                selection.Arrange(LayoutRect.Empty);
                return;
            }

            CaretGeometry other = line.CaretAt(anchor);
            float left = MathF.Min(at.x, other.x);
            float right = MathF.Max(at.x, other.x);
            selection.Arrange(new LayoutRect(origin.X + left, origin.Y + at.top, right - left, at.height));
        }
        #endregion
    }
}
