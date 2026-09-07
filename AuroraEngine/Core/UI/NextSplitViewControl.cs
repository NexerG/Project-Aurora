using ArctisAurora.Core.Registry;

namespace ArctisAurora.Core.UI
{
    // Two panes with a grip between them. A stack panel in every respect — it is its own type so that
    // collapsing a split can never reach authored chrome.
    [A_XSDType("NextSplitView", "UI")]
    public class NextSplitViewControl : NextStackPanelControl
    {
        public NextSplitViewControl()
        {
            alpha = 0f;
        }
    }
}
