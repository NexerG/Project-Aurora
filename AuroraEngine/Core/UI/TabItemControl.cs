using ArctisAurora.Core.Registry;

namespace ArctisAurora.Core.UI
{
    // One page of a TabView: a caption for the strip and the single control it shows.
    [A_XSDType("TabItem", "UI")]
    public class TabItemControl : PanelControl
    {
        [A_XSDElementProperty("Header", "UI", "Caption shown on the tab strip.")]
        public string header = "Tab";

        // What a committed caption edit does. Null on a tab nobody can rename.
        public Action<string>? onRename;

        public TabItemControl()
        {
            alpha = 0f;
        }
    }
}
