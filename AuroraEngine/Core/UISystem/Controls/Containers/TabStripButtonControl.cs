using ArctisAurora.Core.UISystem.Controls.Interactable;
using System.Numerics;

namespace ArctisAurora.Core.UISystem.Controls.Containers
{
    // The button for one tab in a TabView's strip. It carries the item it stands for, because a drop
    // is resolved by whatever the pointer is over and that control has no other way to learn which
    // tab arrived.
    public class TabStripButtonControl : ButtonControl
    {
        private const float tearThreshold = 12f;

        internal TabItemControl item = null!;
        internal TabViewControl owner = null!;

        // False until the pointer has travelled far enough that this is a drag and not a sloppy
        // click, so a drop is refused and no window is torn off.
        internal bool dragging { get; private set; }

        // Set by whichever view takes the drop. Not "did the item move" — a drop back onto its own
        // view is accepted and moves nothing, and that must not tear a window off.
        internal bool accepted;

        private Vector2 grab;

        public override void ResolveOnClick(Vector2 oldPos, Vector2 delta)
        {
            grab = oldPos;
            dragging = false;
            accepted = false;
            StartDrag();
            base.ResolveOnClick(oldPos, delta);
        }

        public override void ResolveDrag(Vector2 lastPos, Vector2 delta)
        {
            if (!dragging && (lastPos - grab).Length() >= tearThreshold)
            {
                dragging = true;
                DragGhost.Show(this);
            }

            base.ResolveDrag(lastPos, delta);
        }

        // Nothing accepted the tab, so it leaves for a window of its own.
        public override void StopDrag()
        {
            if (dragging && !accepted)
                owner.TearOff(item);

            dragging = false;
            base.StopDrag();
        }
    }
}
