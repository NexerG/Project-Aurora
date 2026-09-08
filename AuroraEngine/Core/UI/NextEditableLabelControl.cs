using ArctisAurora.Core.Registry;
using Silk.NET.Maths;

namespace ArctisAurora.Core.UI
{
    // A label that becomes a one-line field for the length of an edit and goes back to a label after
    // it. Both parts live here for the whole life of the control; only one of them is ever shown.
    [A_XSDType("NextEditableLabel", "UI")]
    public class NextEditableLabelControl : ContainerControl
    {
        // field sizing
        private const int minEditWidth = 120;
        private const int editPadding = 24;

        private readonly NextLabelControl label = new NextLabelControl();
        private readonly NextTextBoxControl box = new NextTextBoxControl();

        private Action<string>? commit;

        public bool isEditing { get; private set; }

        [A_XSDElementProperty("Text", "UI", "The caption shown while the control is not being edited.")]
        public string text
        {
            get => label.text;
            set => label.text = value;
        }

        [A_XSDElementProperty("FontSize", "UI", "Font size in pixels.")]
        public int fontSize
        {
            get => label.fontSize;
            set { label.fontSize = value; box.fontSize = value; }
        }

        [A_XSDElementProperty("TextColorHex", "UI", "Colour of the text, in both halves.")]
        public string textColorHex
        {
            get => label.colorHex;
            set { label.colorHex = value; box.textColorHex = value; }
        }

        [A_XSDElementProperty("FieldColorHex", "UI", "Ground of the field while an edit is running.")]
        public string fieldColorHex
        {
            get => box.colorHex;
            set => box.colorHex = value;
        }

        public NextEditableLabelControl()
        {
            alpha = 0f;

            base.AddChild(label);
            base.AddChild(box);
            box.Hide();

            box.onCommit = Committed;
            box.onCancel = End;
            box.onBlur = () => { if (isEditing) box.Commit(); };
        }

        // A decoration: whatever hosts it takes the context, so a press on the caption and a release
        // on the host's own padding are the same target.
        public override Control? ActiveContextTarget() => (parent as Control)?.ActiveContextTarget();

        // Swaps the field in, seeded from the label and with everything selected.
        public void BeginEdit(Action<string> onCommit)
        {
            if (isEditing) return;

            commit = onCommit;
            isEditing = true;

            box.text = label.text;
            box.preferredWidth = MathF.Max(minEditWidth, label.DesiredSize.X + editPadding);
            label.Hide();
            box.Show();
            InvalidateLayout();

            UIEngine.SetActiveControl(box);
            box.Focus();
        }

        // The edit ends before the callback, so a handler that rebuilds the list is not tearing down
        // a control that is still mid-commit.
        private void Committed(string value)
        {
            Action<string>? commit = this.commit;
            End();
            commit?.Invoke(value);
        }

        private void End()
        {
            if (!isEditing) return;

            isEditing = false;
            commit = null;
            box.Hide();
            label.Show();
            InvalidateLayout();
        }

        private Control Visible => isEditing ? box : label;

        #region ---- layout ----
        public override Vector2D<float> Measure(Vector2D<float> availableSize)
        {
            Vector2D<float> desired = Visible.Measure(new Vector2D<float>(
                MathF.Max(0, availableSize.X - padding.totalHorizontal),
                MathF.Max(0, availableSize.Y - padding.totalVertical)));

            arrange.desired = new Vector2D<float>(
                preferredWidth > 0 ? preferredWidth : desired.X + padding.totalHorizontal,
                preferredHeight > 0 ? preferredHeight : desired.Y + padding.totalVertical);
            SetFlag(ArrangeFlags.MeasureDirty, false);
            return arrange.desired;
        }

        // Only the shown part is arranged; the hidden one keeps the collapsed clip Hide() gave it.
        public override void Arrange(LayoutRect finalRect)
        {
            WriteArranged(finalRect);
            Visible.Arrange(finalRect.Shrink(padding));
            SetFlag(ArrangeFlags.ArrangeDirty, false);
        }
        #endregion
    }
}
