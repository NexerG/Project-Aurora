using ArctisAurora.Core.Registry;
using ArctisAurora.Core.UISystem.Controls;
using ArctisAurora.Core.UISystem.Controls.Containers;

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

        [A_XSDActionDependency("View.SplitRight", "UI", "Moves the focused view's active tab into a new pane beside it")]
        public static void SplitRight() => Split(SplitViewControl.SplitEdge.Right);

        [A_XSDActionDependency("View.SplitDown", "UI", "Moves the focused view's active tab into a new pane below it")]
        public static void SplitDown() => Split(SplitViewControl.SplitEdge.Bottom);

        // Splitting off the only tab would empty the source and collapse the split straight back.
        private static void Split(SplitViewControl.SplitEdge edge)
        {
            TabViewControl source = Acting();
            if (source?.activeItem == null || source.ItemCount < 2) return;

            TabItemControl moving = source.activeItem;
            TabViewControl pane = SplitViewControl.Split(source, edge);
            if (pane == null) return;

            moving.SetParent(pane);
            pane.SetActive(moving);
        }
    }
}
