using ArctisAurora.Core.Registry;

namespace ArctisAurora.Core.UI
{
    // One page of a NextTabView: a caption for the strip and the single control it shows.
    [A_XSDType("NextTabItem", "UI")]
    public class NextTabItemControl : NextPanelControl
    {
        [A_XSDElementProperty("Header", "UI", "Caption shown on the tab strip.")]
        public string header = "Tab";

        // What a committed caption edit does. Null on a tab nobody can rename.
        public Action<string>? onRename;

        public NextTabItemControl()
        {
            alpha = 0f;
        }
    }
}
