using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.Registry.Assets;
using ArctisAurora.Core.UISystem.Controls.Containers;
using ArctisAurora.EngineWork.Registry;
using Silk.NET.Maths;

namespace ArctisAurora.Core.UISystem.Controls.Text.Document
{
    // The document's content area: blocks stacked top to bottom, plus the caret placed over them.
    public class DocumentControl : AbstractContainerControl
    {
        public float blockSpacing;

        // caret target
        private CaretControl caret;
        private TextControl caretRun;

        public DocumentControl()
        {
            maskAsset = AssetRegistries.GetAsset<TextureAsset>("invisible");
            horizontalAlignment = HorizontalAlignment.Stretch;
            BubbleAll();
        }

        // Points the caret at a run; the offset is read off it at Arrange.
        public void SetCaret(TextControl run)
        {
            caretRun = run;

            if (caret == null)
            {
                caret = new CaretControl();
                AddChild(caret);
            }

            InvalidateArrange();
        }

        public override Vector2D<float> Measure(Vector2D<float> availableSize)
        {
            float height = 0f;
            int blocks = 0;

            foreach (Entity child in children)
            {
                if (child is not Block block) continue;

                height += block.Measure(new Vector2D<float>(availableSize.X, float.MaxValue)).Y;
                blocks++;
            }

            if (blocks > 1) height += blockSpacing * (blocks - 1);

            caret?.Measure(availableSize);

            DesiredSize = new Vector2D<float>(availableSize.X, height);
            isMeasureDirty = false;
            return DesiredSize;
        }

        public override void Arrange(LayoutRect finalRect)
        {
            arrangedRect = finalRect;
            WriteArrangedTransform(finalRect);

            ClipRect = parent is VulkanControl parentControl
                ? (clipOutOfBounds ? LayoutRect.Intersect(finalRect, parentControl.ClipRect) : parentControl.ClipRect)
                : finalRect;

            float y = finalRect.y;
            foreach (Entity child in children)
            {
                if (child is not Block block) continue;

                block.Arrange(new LayoutRect(finalRect.x, y, finalRect.width, block.DesiredSize.Y));
                y += block.DesiredSize.Y + blockSpacing;
            }

            // after the blocks, so the run's arrangedRect is this frame's
            if (caret != null && caretRun != null)
            {
                CaretGeometry geometry = caretRun.CaretAt(caretRun.cursorPosition);
                LayoutRect inner = caretRun.arrangedRect.Shrink(caretRun.padding);

                caret.Arrange(new LayoutRect(inner.x + geometry.x, inner.y + geometry.top,
                    CaretControl.Width, geometry.height));
            }

            isArrangeDirty = false;
        }
    }
}
