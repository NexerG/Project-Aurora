using ArctisAurora.Core.Animation;
using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.Registry;
using System.Numerics;

namespace ArctisAurora.Core.UI
{
    // Two options with a knob that slides to the chosen one.
    [A_XSDType("ToggleSwitch", "UI")]
    public class ToggleSwitchControl : ButtonControl
    {
        // geometry
        private const int captionSize = 13;
        private const float trackWidth = 40f;
        private const float trackHeight = 20f;
        private const float knobInset = 3f;
        private const float gap = 8f;
        private const float slideSeconds = 0.18f;

        private readonly LabelControl leftLabel = new LabelControl { fontSize = captionSize, hitTestable = false };
        private readonly LabelControl rightLabel = new LabelControl { fontSize = captionSize, hitTestable = false };

        private readonly PanelControl track = new PanelControl
        {
            gradient = "toggle-track",
            cornerRadius = new CornerRadii(trackHeight * 0.5f),
            hitTestable = false
        };

        private readonly PanelControl knob = new PanelControl
        {
            cornerRadius = new CornerRadii((trackHeight - 2f * knobInset) * 0.5f),
            hitTestable = false
        };

        public Action<bool>? onChanged;

        [A_XSDElementProperty("LeftText", "UI", "Caption of the option on the left.")]
        public string leftText
        {
            get => leftLabel.text;
            set => leftLabel.text = value;
        }

        [A_XSDElementProperty("RightText", "UI", "Caption of the option on the right.")]
        public string rightText
        {
            get => rightLabel.text;
            set => rightLabel.text = value;
        }

        [A_XSDElementProperty("Right", "UI", "Whether the right option is the chosen one.")]
        public bool right
        {
            get => _right;
            set
            {
                _right = value;
                knob.horizontalPosition = value ? 1f : 0f;
                Light();
            }
        }
        private bool _right;

        public ToggleSwitchControl()
        {
            alpha = 0f;
            track.PaintOr(null, PaletteRole.Chrome);
            knob.PaintOr("#FFFFFF", PaletteRole.Ink);
            knob.horizontalPosition = 0f;
            AddChild(leftLabel);
            AddChild(track);
            AddChild(knob);
            AddChild(rightLabel);
            Light();
        }

        public override void AddChild(Entity entity)
        {
            children.Add(entity);
            entity.parent = this;
            MarkTreeOrderDirty();
            InvalidateLayout();
        }

        protected override Vector2 MeasureCore(Vector2 availableSize)
        {
            Vector2 l = leftLabel.Measure(availableSize);
            Vector2 r = rightLabel.Measure(availableSize);
            track.Measure(availableSize);

            float w = preferredWidth > 0 ? preferredWidth : l.X + gap + trackWidth + gap + r.X + padding.totalHorizontal;
            float h = preferredHeight > 0 ? preferredHeight : MathF.Max(trackHeight, MathF.Max(l.Y, r.Y)) + padding.totalVertical;

            arrange.desired = new Vector2(w, h);
            SetFlag(ArrangeFlags.MeasureDirty, false);
            return arrange.desired;
        }

        protected override void ArrangeCore(LayoutRect finalRect)
        {
            WriteArranged(finalRect);
            LayoutRect inner = finalRect.Shrink(padding);
            float middle = inner.y + inner.height * 0.5f;

            Vector2 l = leftLabel.DesiredSize;
            leftLabel.Arrange(new LayoutRect(inner.x, middle - l.Y * 0.5f, l.X, l.Y));

            float trackX = inner.x + l.X + gap;
            track.Arrange(new LayoutRect(trackX, middle - trackHeight * 0.5f, trackWidth, trackHeight));

            float knobSize = trackHeight - 2f * knobInset;
            float travel = trackWidth - 2f * knobInset - knobSize;
            knob.Measure(new Vector2(knobSize, knobSize));
            knob.Arrange(new LayoutRect(trackX + knobInset + travel * knob.horizontalPosition,
                middle - knobSize * 0.5f, knobSize, knobSize));

            Vector2 r = rightLabel.DesiredSize;
            rightLabel.Arrange(new LayoutRect(trackX + trackWidth + gap, middle - r.Y * 0.5f, r.X, r.Y));
            SetFlag(ArrangeFlags.ArrangeDirty, false);
        }

        public override bool OnPointerRelease(PointerEvent e)
        {
            base.OnPointerRelease(e);
            if (e.button != PointerEvent.leftButton || !enabled) return true;

            _right = !_right;
            Animations.Tween(knob, "horizontalPosition", new Vector4(_right ? 1f : 0f, 0f, 0f, 0f), slideSeconds, Curve.Ease(EaseKind.CubicInOut));
            Light();
            onChanged?.Invoke(right);
            return true;
        }

        // The chosen side's caption in Ink, the other muted.
        private void Light()
        {
            leftLabel.role = right ? PaletteRole.MutedInk : PaletteRole.Ink;
            rightLabel.role = right ? PaletteRole.Ink : PaletteRole.MutedInk;
        }
    }
}
