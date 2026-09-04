using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.UISystem.Controls.Containers;
using ArctisAurora.EngineWork;
using ArctisAurora.EngineWork.Rendering;
using Silk.NET.GLFW;
using Silk.NET.Maths;

namespace ArctisAurora.Core.UISystem.Controls.Interactable
{
    // A grip between two panes of a StackPanel. Between a sized pane and a star one it writes the
    // first pane's size; between two star panes it trades weight between the pair.
    [A_XSDType("Splitter", "UI")]
    public class SplitterControl : ButtonControl
    {
        private Vector2D<float> grab;
        private float grabSize;

        // star against star
        private bool grabStar;
        private float grabTotal;
        private float grabWeight;

        public SplitterControl()
        {
            controlColorHex = "#2A2A2A";
            hoverColorHex = "#3D3D3D";
            pressColorHex = "#4A4A4A";
        }

        public override void ResolveOnEnter()
        {
            RenderWindow.Of(this)?.os.ChangeCursor(IsVertical ? CursorShape.VResize : CursorShape.HResize);
            base.ResolveOnEnter();
        }

        public override void ResolveExit()
        {
            RenderWindow.Of(this)?.os.ChangeCursor(CursorShape.Arrow);
            base.ResolveExit();
        }

        public override void ResolveOnClick(Vector2D<float> oldPos, Vector2D<float> delta)
        {
            VulkanControl pane = PreviousPane();
            if (pane != null)
            {
                bool vertical = IsVertical;
                VulkanControl next = NextPane();

                grab = oldPos + delta;
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
            base.ResolveOnClick(oldPos, delta);
        }

        // Sized from where the grab started rather than by accumulating per-tick deltas, so a clamped
        // pane cannot drift away from the pointer that is dragging it.
        public override void ResolveDrag(Vector2D<float> lastPos, Vector2D<float> delta)
        {
            VulkanControl pane = PreviousPane();
            if (pane == null) return;

            bool vertical = IsVertical;
            Vector2D<float> now = lastPos + delta;
            float wanted = grabSize + (vertical ? now.Y - grab.Y : now.X - grab.X);

            if (grabStar) DragStars(pane, vertical, wanted);
            else if (vertical) pane.preferredHeight = (int)MathF.Max(pane.minHeight, wanted);
            else pane.preferredWidth = (int)MathF.Max(pane.minWidth, wanted);

            base.ResolveDrag(lastPos, delta);
        }

        // Holds the pair's weight sum, so the boundary moves and their combined main size does not. A
        // weight of zero fails IsHeightStar and would drop a pane out of star sizing mid-drag, which
        // is what the one pixel floor on either side is for. The stars are fields, so nothing else
        // invalidates.
        private void DragStars(VulkanControl pane, bool vertical, float wanted)
        {
            VulkanControl next = NextPane();
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
            pane.InvalidateLayout();
        }

        private bool IsVertical =>
            (parent as StackPanelControl)?.orientation == StackPanelControl.Orientation.Vertical;

        private static bool IsStar(VulkanControl control, bool vertical) =>
            vertical ? control.IsHeightStar : control.IsWidthStar;

        // The sibling ahead of this one. Null when the parent is not a StackPanel or nothing precedes
        // it, which is what makes a stray splitter inert rather than throwing.
        private VulkanControl PreviousPane()
        {
            if (parent is not StackPanelControl panel) return null;

            VulkanControl previous = null;
            foreach (Entity e in panel.children)
            {
                if (e == this) return previous;
                if (e is VulkanControl control) previous = control;
            }
            return null;
        }

        // The sibling after this one, null on the same terms.
        private VulkanControl NextPane()
        {
            if (parent is not StackPanelControl panel) return null;

            bool passed = false;
            foreach (Entity e in panel.children)
            {
                if (e == this) { passed = true; continue; }
                if (passed && e is VulkanControl control) return control;
            }
            return null;
        }
    }
}
