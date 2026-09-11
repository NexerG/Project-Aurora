using ArctisAurora.Core.Registry;
using ArctisAurora.Core.UI;
using ArctisAurora.Core.UISystem.Controls;
using ArctisAurora.Core.UISystem.Controls.Containers;
using VulkanControl = ArctisAurora.Core.UISystem.Controls.VulkanControl;

namespace ArctisAurora.Core.UISystem.Actions
{
    // The tab menu. Every entry acts on the strip button the menu was opened on, because that button
    // is the only thing that knows which tab the pointer was over — its view knows only which tab is
    // active.
    public static class TabActions
    {
        [A_XSDActionDependency("Tab.Close", "UI", "Closes the tab the menu was opened on")]
        public static void Close()
        {
            if (NextTab() is NextTabStripButtonControl next) { next.owner.CloseTab(next.item); return; }

            TabStripButtonControl? tab = Tab();
            tab?.owner.CloseTab(tab.item);
        }

        [A_XSDActionDependency("Tab.CloseOthers", "UI", "Closes every tab in the view but this one")]
        public static void CloseOthers()
        {
            if (NextTab() is NextTabStripButtonControl next) { next.owner.CloseOthers(next.item); return; }

            TabStripButtonControl? tab = Tab();
            tab?.owner.CloseOthers(tab.item);
        }

        [A_XSDActionDependency("Tab.CloseRight", "UI", "Closes every tab after this one in the strip")]
        public static void CloseRight()
        {
            if (NextTab() is NextTabStripButtonControl next) { next.owner.CloseToTheRight(next.item); return; }

            TabStripButtonControl? tab = Tab();
            tab?.owner.CloseToTheRight(tab.item);
        }

        [A_XSDActionDependency("Tab.SplitRight", "UI", "Moves this tab into a new pane beside its view")]
        public static void SplitRight()
        {
            if (NextTab() is NextTabStripButtonControl next) { next.owner.SplitOff(next.item, NextSplitViewControl.SplitEdge.Right); return; }

            TabStripButtonControl? tab = Tab();
            tab?.owner.SplitOff(tab.item, SplitViewControl.SplitEdge.Right);
        }

        [A_XSDActionDependency("Tab.SplitDown", "UI", "Moves this tab into a new pane below its view")]
        public static void SplitDown()
        {
            if (NextTab() is NextTabStripButtonControl next) { next.owner.SplitOff(next.item, NextSplitViewControl.SplitEdge.Bottom); return; }

            TabStripButtonControl? tab = Tab();
            tab?.owner.SplitOff(tab.item, SplitViewControl.SplitEdge.Bottom);
        }

        [A_XSDActionDependency("Tab.MoveToNewWindow", "UI", "Moves this tab into a window of its own")]
        public static void MoveToNewWindow(VulkanControl target)
        {
            TabStripButtonControl tab = target as TabStripButtonControl;
            tab?.owner.TearOff(tab.item);
        }

        #region ---- predicates ----
        [A_XSDActionDependency("Tab.HasSiblings", "UI", "True when the view holds more than this one tab")]
        public static bool HasSiblings(VulkanControl target) =>
            target is TabStripButtonControl tab && tab.owner.ItemCount > 1;

        [A_XSDActionDependency("Tab.HasRight", "UI", "True when another tab follows this one in the strip")]
        public static bool HasRight(VulkanControl target)
        {
            if (target is not TabStripButtonControl tab) return false;

            bool passed = false;
            foreach (TabItemControl item in tab.owner.Items)
            {
                if (ReferenceEquals(item, tab.item)) { passed = true; continue; }
                if (passed) return true;
            }
            return false;
        }

        [A_XSDActionDependency("Tab.CanTearOff", "UI", "True when the view names a document a torn-off tab opens in")]
        public static bool CanTearOff(VulkanControl target) =>
            target is TabStripButtonControl tab && !string.IsNullOrEmpty(tab.owner.tearOffDocument);
        #endregion

        // The strip button the menu was opened on, on the new stack and then the old.
        private static NextTabStripButtonControl? NextTab()
        {
            for (Control? c = NextContextMenus.target; c != null; c = c.parent as Control)
                if (c is NextTabStripButtonControl tab) return tab;

            return null;
        }

        private static TabStripButtonControl? Tab() => ContextMenus.invoker as TabStripButtonControl;
    }
}
