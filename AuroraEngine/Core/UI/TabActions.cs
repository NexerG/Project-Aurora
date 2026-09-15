using ArctisAurora.Core.Registry;

namespace ArctisAurora.Core.UI
{
    // The tab menu. Every entry acts on the strip button the menu was opened on, because that button
    // is the only thing that knows which tab the pointer was over — its view knows only which tab is
    // active.
    public static class TabActions
    {
        [A_XSDActionDependency("Tab.Close", "UI", "Closes the tab the menu was opened on")]
        public static void Close()
        {
            if (Tab() is TabStripButtonControl next) next.owner.CloseTab(next.item);
        }

        [A_XSDActionDependency("Tab.CloseOthers", "UI", "Closes every tab in the view but this one")]
        public static void CloseOthers()
        {
            if (Tab() is TabStripButtonControl next) next.owner.CloseOthers(next.item);
        }

        [A_XSDActionDependency("Tab.CloseRight", "UI", "Closes every tab after this one in the strip")]
        public static void CloseRight()
        {
            if (Tab() is TabStripButtonControl next) next.owner.CloseToTheRight(next.item);
        }

        [A_XSDActionDependency("Tab.SplitRight", "UI", "Moves this tab into a new pane beside its view")]
        public static void SplitRight()
        {
            if (Tab() is TabStripButtonControl next) next.owner.SplitOff(next.item, SplitViewControl.SplitEdge.Right);
        }

        [A_XSDActionDependency("Tab.SplitDown", "UI", "Moves this tab into a new pane below its view")]
        public static void SplitDown()
        {
            if (Tab() is TabStripButtonControl next) next.owner.SplitOff(next.item, SplitViewControl.SplitEdge.Bottom);
        }

        [A_XSDActionDependency("Tab.MoveToNewWindow", "UI", "Moves this tab into a window of its own")]
        public static void MoveToNewWindow()
        {
            if (Tab() is TabStripButtonControl next) next.owner.TearOff(next.item);
        }

        // The strip button the menu was opened on.
        private static TabStripButtonControl? Tab()
        {
            for (Control? c = ContextMenus.target; c != null; c = c.parent as Control)
                if (c is TabStripButtonControl tab) return tab;

            return null;
        }
    }
}
