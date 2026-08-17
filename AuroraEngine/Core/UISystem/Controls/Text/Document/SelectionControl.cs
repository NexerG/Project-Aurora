namespace ArctisAurora.Core.UISystem.Controls.Text.Document
{
    // One highlight box behind the text. A selection is one of these per visual line it covers.
    public class SelectionControl : PanelControl
    {
        public SelectionControl()
        {
            controlColorHex = "#264F78";
            hitTestable = false;
        }
    }
}
