using ArctisAurora.EngineWork;
using Silk.NET.Maths;

namespace ArctisAurora.Core.UI
{
    // The button for one tab in a NextTabView's strip. It carries the item it stands for, because a
    // drop is resolved by whatever the pointer is over and that control has no other way to learn
    // which tab arrived.
    public class NextTabStripButtonControl : NextButtonControl
    {
        private const float tearThreshold = 12f;

        internal NextTabItemControl item = null!;
        internal NextTabViewControl owner = null!;

        // False until the pointer has travelled far enough that this is a drag and not a sloppy
        // click, so a drop is refused.
        internal bool dragging { get; private set; }

        // press state
        private bool armed;
        private Vector2D<float> grab;

        public override bool OnPointerPress(PointerEvent e)
        {
            grab = e.point;
            armed = true;
            dragging = false;
            return base.OnPointerPress(e);
        }

        // The drag is claimed past the threshold rather than on the press, because a live drag
        // swallows the release that activates the tab.
        public override bool OnPointerMove(PointerEvent e)
        {
            if (armed)
            {
                if (!InputHandler.instance.IsKeyDown(Keys.MouseLeft)) armed = false;
                else if ((e.point - grab).Length >= tearThreshold)
                {
                    armed = false;
                    dragging = true;
                    StartDrag();
                    NextDragGhost.Show(this);
                }
            }
            return base.OnPointerMove(e);
        }

        public override bool OnPointerRelease(PointerEvent e)
        {
            armed = false;
            return base.OnPointerRelease(e);
        }

        // A drag ends without a release, so the activation a click would have made happens here. The
        // tab that moved to another view is no longer ours and SetActive drops it.
        public override void OnDragStop(bool accepted)
        {
            NextDragGhost.Hide();
            dragging = false;
            owner.SetActive(item);
            base.OnDragStop(accepted);
        }
    }
}
