using ArctisAurora.Core.Registry.Assets;
using ArctisAurora.Core.UISystem.Controls.Containers;
using ArctisAurora.Core.UISystem.Controls.Text.Editing;
using ArctisAurora.EngineWork.Registry;
using Silk.NET.Maths;

namespace ArctisAurora.Core.UISystem.Controls.Text
{
    // A label that becomes a one-line field for the length of an edit and goes back to a label after
    // it. Both parts live here for the whole life of the control; only one of them is ever shown.
    public class EditableLabelControl : AbstractContainerControl
    {
        // field sizing
        private const int minEditWidth = 120;
        private const int editPadding = 24;

        private readonly LabelControl label = new LabelControl();
        private readonly TextBoxControl box = new TextBoxControl();

        private Action<string>? commit;

        public bool isEditing { get; private set; }

        public string text
        {
            get => label.text;
            set => label.text = value;
        }

        public int fontSize
        {
            get => label.fontSize;
            set { label.fontSize = value; box.fontSize = value; }
        }

        public string textColorHex
        {
            get => label.controlColorHex;
            set { label.controlColorHex = value; box.textColorHex = value; }
        }

        public string fieldColorHex
        {
            get => box.controlColorHex;
            set => box.controlColorHex = value;
        }

        public EditableLabelControl()
        {
            maskAsset = AssetRegistries.GetAsset<TextureAsset>("invisible");
            BubbleAll();

            AddChild(label);
            AddChild(box);
            box.Hide();

            box.onCommit = Committed;
            box.onCancel = End;
            box.onBlur = () => { if (isEditing) box.Commit(); };
        }

        // Swaps the field in, seeded from the label and with everything selected.
        public void BeginEdit(Action<string> onCommit)
        {
            if (isEditing) return;

            commit = onCommit;
            isEditing = true;

            box.text = label.text;
            box.preferredWidth = (int)MathF.Max(minEditWidth, label.DesiredSize.X + editPadding);
            label.Hide();
            box.Show();
            InvalidateLayout();

            UICollisionHandling.SetActiveControl(box);
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

        private VulkanControl Visible => isEditing ? box : label;

        #region ---- layout ----
        public override Vector2D<float> Measure(Vector2D<float> availableSize)
        {
            Vector2D<float> desired = Visible.Measure(new Vector2D<float>(
                MathF.Max(0, availableSize.X - padding.totalHorizontal),
                MathF.Max(0, availableSize.Y - padding.totalVertical)));

            DesiredSize = new Vector2D<float>(
                preferredWidth > 0 ? preferredWidth : desired.X + padding.totalHorizontal,
                preferredHeight > 0 ? preferredHeight : desired.Y + padding.totalVertical);
            isMeasureDirty = false;
            return DesiredSize;
        }

        // Only the shown part is arranged; the hidden one keeps the collapsed clip Hide() gave it.
        public override void Arrange(LayoutRect finalRect)
        {
            arrangedRect = finalRect;
            WriteArrangedTransform(finalRect);

            ClipRect = parent is VulkanControl parentControl
                ? (clipOutOfBounds ? LayoutRect.Intersect(finalRect, parentControl.ClipRect) : parentControl.ClipRect)
                : finalRect;

            Visible.Arrange(finalRect.Shrink(padding));
            isArrangeDirty = false;
        }
        #endregion
    }
}
