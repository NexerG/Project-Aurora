namespace ArctisAurora.Core.UISystem.Controls
{
    // A translucent wash over a region — a drop indicator, the ground under a context menu. Whether
    // it takes the pointer is the caller's business, not the wash's.
    public class HintControl : PanelControl
    {
        public HintControl()
        {
            controlColorHex = "#4C8DFF";
            alpha = 0.35f;
        }
    }
}
