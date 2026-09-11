using ArctisAurora.Core.Registry;
using ArctisAurora.Core.UI;
using ArctisAurora.Core.UISystem.Controls;
using ArctisAurora.Core.UISystem.Controls.Containers;
using VulkanControl = ArctisAurora.Core.UISystem.Controls.VulkanControl;

namespace ArctisAurora.Core.UISystem.Actions
{
    // Splitting a view from the keyboard. The drag-to-edge path lives in TabViewControl; this is the
    // same operation under a name XML can bind.
    public static class ViewActions
    {
        // Bound from XML as zero-argument delegates, so the view is the menu's owner when one is
        // open, and otherwise wherever focus last landed.
        private static TabViewControl Acting()
        {
            VulkanControl control = ContextMenus.invoker
                ?? UICollisionHandling.activeControl ?? UICollisionHandling.hovering;
            while (control != null && control is not TabViewControl)
                control = control.parent as VulkanControl;
            return control as TabViewControl;
        }

        // The new stack's view: the first above the menu's target, the active control or the hovered
        // one, in that order.
        private static NextTabViewControl? NextActing() =>
            ViewAbove(NextContextMenus.target) ?? ViewAbove(UIEngine.activeControl) ?? ViewAbove(UIEngine.hovering);

        private static NextTabViewControl? ViewAbove(Control? control)
        {
            for (Control? c = control; c != null; c = c.parent as Control)
                if (c is NextTabViewControl view) return view;

            return null;
        }

        [A_XSDActionDependency("View.SplitRight", "Any", "Moves the focused view's active tab into a new pane beside it")]
        public static void SplitRight() => Split(NextSplitViewControl.SplitEdge.Right, SplitViewControl.SplitEdge.Right);

        [A_XSDActionDependency("View.SplitDown", "Any", "Moves the focused view's active tab into a new pane below it")]
        public static void SplitDown() => Split(NextSplitViewControl.SplitEdge.Bottom, SplitViewControl.SplitEdge.Bottom);

        // The view's own menu and the keybinds have no tab under the pointer, so they act on the
        // active one; a right click on a tab goes through TabActions instead.
        private static void Split(NextSplitViewControl.SplitEdge nextEdge, SplitViewControl.SplitEdge edge)
        {
            NextTabViewControl? next = NextActing();
            if (next != null) { next.SplitOff(next.activeItem, nextEdge); return; }

            TabViewControl source = Acting();
            source?.SplitOff(source.activeItem, edge);
        }
    }
}
