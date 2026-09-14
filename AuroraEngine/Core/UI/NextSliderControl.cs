using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.Registry;
using System.Numerics;

namespace ArctisAurora.Core.UI
{
    // A value from 0 to 1, picked by pressing or dragging along a track.
    [A_XSDType("NextSlider", "UI")]
    public class NextSliderControl : ContainerControl
    {
        #region properties
        [A_XSDElementProperty("Value", "UI", "Position of the thumb, from 0 at the left end to 1 at the right.")]
        public float value
        {
            get => field;
            set
            {
                field = Math.Clamp(value, 0f, 1f);
                InvalidateLayout();
            }
        } = 0f;

        // metrics
        [A_XSDElementProperty("TrackHeight", "UI", "Height of the track in pixels.")]
        public float trackHeight = 4f;

        [A_XSDElementProperty("ThumbWidth", "UI", "Width of the thumb in pixels.")]
        public float thumbWidth = 10f;

        // palette
        [A_XSDElementProperty("TrackColorHex", "UI", "Ground of the track.")]
        public string trackColorHex { get => field; set { field = value; if (_track != null) _track.colorHex = value; } } = "#3D3D3D";

        [A_XSDElementProperty("ThumbColorHex", "UI", "Ground of the thumb.")]
        public string thumbColorHex { get => field; set { field = value; if (_thumb != null) _thumb.colorHex = value; } } = "#D8D8D8";
        #endregion

        private readonly NextPanelControl _track;
        private readonly NextPanelControl _thumb;

        public Action<float>? onChanged;

        public NextSliderControl()
        {
            _track = new NextPanelControl { colorHex = trackColorHex, hitTestable = false };
            _thumb = new NextPanelControl { colorHex = thumbColorHex, cornerRadius = new CornerRadii(2), hitTestable = false };

            AddChild(_track);
            AddChild(_thumb);
        }

        public override bool OnPointerPress(PointerEvent e)
        {
            if (e.button != PointerEvent.leftButton) return base.OnPointerPress(e);

            Pick(e.point.X);
            StartDrag();
            return true;
        }

        public override void OnDrag(PointerEvent e)
        {
            Pick(e.point.X);
            base.OnDrag(e);
        }

        // Maps a pointer x onto the thumb's travel and reports the value if it moved.
        private void Pick(float x)
        {
            LayoutRect inner = arrangedRect.Shrink(padding);
            float travel = MathF.Max(1, inner.width - thumbWidth);
            float picked = Math.Clamp((x - inner.x - thumbWidth / 2) / travel, 0f, 1f);
            if (picked == value) return;

            value = picked;
            onChanged?.Invoke(value);
        }

        public override Vector2 Measure(Vector2 availableSize)
        {
            Vector2 size = base.Measure(availableSize);

            foreach (Entity child in children)
                if (child is Control control) control.Measure(size);

            return size;
        }

        public override void Arrange(LayoutRect finalRect)
        {
            base.Arrange(finalRect);

            LayoutRect inner = finalRect.Shrink(padding);
            float travel = MathF.Max(0, inner.width - thumbWidth);

            _track.Arrange(new LayoutRect(inner.x, inner.y + (inner.height - trackHeight) / 2, inner.width, trackHeight));
            _thumb.Arrange(new LayoutRect(inner.x + travel * value, inner.y, thumbWidth, inner.height));
        }
    }
}
