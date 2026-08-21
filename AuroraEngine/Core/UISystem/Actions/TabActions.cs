using ArctisAurora.Core.Registry;
using ArctisAurora.Core.UISystem.Controls;
using ArctisAurora.Core.UISystem.Controls.Containers;

namespace ArctisAurora.Core.UISystem.Actions
{
    // The tab menu. Every entry acts on the strip button the menu was opened on, because that button
    // is the only thing that knows which tab the pointer was over — its view knows only which tab is
    // active.
    public static class TabActions
    {
        [A_XSDActionDependency("Tab.Close", "UI", "Closes the tab the menu was opened on")]
        public static void Close(VulkanControl target)
        {
            TabStripButtonControl tab = target as TabStripButtonControl;
            tab?.owner.CloseTab(tab.item);
        }

        [A_XSDActionDependency("Tab.CloseOthers", "UI", "Closes every tab in the view but this one")]
        public static void CloseOthers(VulkanControl target)
        {
            TabStripButtonControl tab = target as TabStripButtonControl;
            tab?.owner.CloseOthers(tab.item);
        }

        [A_XSDActionDependency("Tab.CloseRight", "UI", "Closes every tab after this one in the strip")]
        public static void CloseRight(VulkanControl target)
        {
            TabStripButtonControl tab = target as TabStripButtonControl;
            tab?.owner.CloseToTheRight(tab.item);
        }

        [A_XSDActionDependency("Tab.SplitRight", "UI", "Moves this tab into a new pane beside its view")]
        public static void SplitRight(VulkanControl target)
        {
            TabStripButtonControl tab = target as TabStripButtonControl;
            tab?.owner.SplitOff(tab.item, SplitViewControl.SplitEdge.Right);
        }

        [A_XSDActionDependency("Tab.SplitDown", "UI", "Moves this tab into a new pane below its view")]
        public static void SplitDown(VulkanControl target)
        {
            TabStripButtonControl tab = target as TabStripButtonControl;
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
    }
}
