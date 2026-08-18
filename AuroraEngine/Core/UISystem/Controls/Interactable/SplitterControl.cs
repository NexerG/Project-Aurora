using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.UISystem.Controls.Containers;
using ArctisAurora.EngineWork.Rendering;
using Silk.NET.GLFW;
using Silk.NET.Maths;

namespace ArctisAurora.Core.UISystem.Controls.Interactable
{
    // A grip between two panes of a StackPanel that resizes the pane before it. A star pane after it
    // absorbs the difference, so only one size is ever written.
    [A_XSDType("Splitter", "UI")]
    public class SplitterControl : ButtonControl
    {
        private Vector2D<float> grab;
        private int grabSize;

        public SplitterControl()
        {
            controlColorHex = "#2A2A2A";
            hoverColorHex = "#3D3D3D";
            pressColorHex = "#4A4A4A";
        }

        public override void ResolveOnEnter()
        {
            AGlfwWindow.ChangeCursor(IsVertical ? CursorShape.VResize : CursorShape.HResize);
            base.ResolveOnEnter();
        }

        public override void ResolveExit()
        {
            AGlfwWindow.ChangeCursor(CursorShape.Arrow);
            base.ResolveExit();
        }

        public override void ResolveOnClick(Vector2D<float> oldPos, Vector2D<float> delta)
        {
            VulkanControl pane = PreviousPane();
            if (pane != null)
            {
                grab = oldPos + delta;
                grabSize = (int)(IsVertical ? pane.arrangedRect.height : pane.arrangedRect.width);
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

            Vector2D<float> now = lastPos + delta;
            if (IsVertical)
                pane.preferredHeight = (int)MathF.Max(pane.minHeight, grabSize + (now.Y - grab.Y));
            else
                pane.preferredWidth = (int)MathF.Max(pane.minWidth, grabSize + (now.X - grab.X));

            base.ResolveDrag(lastPos, delta);
        }

        private bool IsVertical =>
            (parent as StackPanelControl)?.orientation == StackPanelControl.Orientation.Vertical;

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
    }
}
