using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.Registry;
using Silk.NET.GLFW;
using Silk.NET.Maths;

namespace ArctisAurora.Core.UI
{
    // A grip between two panes of a NextStackPanel. Between a sized pane and a star one it writes the
    // first pane's size; between two star panes it trades weight between the pair.
    [A_XSDType("NextSplitter", "UI")]
    public class NextSplitterControl : NextButtonControl
    {
        private Vector2D<float> grab;
        private float grabSize;

        // star against star
        private bool grabStar;
        private float grabTotal;
        private float grabWeight;

        public NextSplitterControl()
        {
            hoverColorHex = "#3D3D3D";
            pressColorHex = "#4A4A4A";
            colorHex = "#2A2A2A";
        }

        public override bool OnPointerEnter(PointerEvent e)
        {
            UIEngine.WindowOf(this)?.os.ChangeCursor(IsVertical ? CursorShape.VResize : CursorShape.HResize);
            return base.OnPointerEnter(e);
        }

        public override bool OnPointerExit(PointerEvent e)
        {
            UIEngine.WindowOf(this)?.os.ChangeCursor(CursorShape.Arrow);
            return base.OnPointerExit(e);
        }

        public override bool OnPointerPress(PointerEvent e)
        {
            Control pane = PreviousPane();
            if (pane != null)
            {
                bool vertical = IsVertical;
                Control next = NextPane();

                grab = e.point;
                grabSize = vertical ? pane.arrangedRect.height : pane.arrangedRect.width;

                grabStar = false;
                if (next != null && IsStar(pane, vertical) && IsStar(next, vertical))
                {
                    grabTotal = grabSize + (vertical ? next.arrangedRect.height : next.arrangedRect.width);
                    grabWeight = (vertical ? pane.heightStar : pane.widthStar)
                               + (vertical ? next.heightStar : next.widthStar);
                    grabStar = grabTotal > 0f;
                }
                StartDrag();
            }
            return base.OnPointerPress(e);
        }

        // Sized from where the grab started rather than by accumulating per-tick deltas, so a clamped
        // pane cannot drift away from the pointer that is dragging it.
        public override void OnDrag(PointerEvent e)
        {
            Control pane = PreviousPane();
            if (pane == null) return;

            bool vertical = IsVertical;
            float wanted = grabSize + (vertical ? e.point.Y - grab.Y : e.point.X - grab.X);

            if (grabStar) DragStars(pane, vertical, wanted);
            else if (vertical) pane.preferredHeight = MathF.Max(pane.minHeight, wanted);
            else pane.preferredWidth = MathF.Max(pane.minWidth, wanted);

            base.OnDrag(e);
        }

        // Holds the pair's weight sum, so the boundary moves and their combined main size does not. A
        // weight of zero fails IsHeightStar and would drop a pane out of star sizing mid-drag, which
        // is what the one pixel floor on either side is for.
        private void DragStars(Control pane, bool vertical, float wanted)
        {
            Control next = NextPane();
            if (next == null) return;

            float floor = MathF.Max(vertical ? pane.minHeight : pane.minWidth, 1f);
            float ceiling = grabTotal - MathF.Max(vertical ? next.minHeight : next.minWidth, 1f);
            float main = MathF.Max(floor, MathF.Min(wanted, ceiling));

            float share = grabWeight * main / grabTotal;
            if (vertical)
            {
                pane.heightStar = share;
                next.heightStar = grabWeight - share;
            }
            else
            {
                pane.widthStar = share;
                next.widthStar = grabWeight - share;
            }
        }

        private bool IsVertical =>
            (parent as NextStackPanelControl)?.orientation == NextStackPanelControl.Orientation.Vertical;

        private static bool IsStar(Control control, bool vertical) =>
            vertical ? control.IsHeightStar : control.IsWidthStar;

        // The sibling ahead of this one. Null when the parent is not a stack or nothing precedes it,
        // which is what makes a stray splitter inert rather than throwing.
        private Control PreviousPane()
        {
            if (parent is not NextStackPanelControl panel) return null;

            Control previous = null;
            foreach (Entity e in panel.children)
            {
                if (e == this) return previous;
                if (e is Control control) previous = control;
            }
            return null;
        }

        // The sibling after this one, null on the same terms.
        private Control NextPane()
        {
            if (parent is not NextStackPanelControl panel) return null;

            bool passed = false;
            foreach (Entity e in panel.children)
            {
                if (e == this) { passed = true; continue; }
                if (passed && e is Control control) return control;
            }
            return null;
        }
    }
}
