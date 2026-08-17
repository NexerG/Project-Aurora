namespace ArctisAurora.Core.UISystem.Controls.Text.Document
{
    // The insertion point — a narrow filled quad, positioned by DocumentControl.
    public class CaretControl : PanelControl
    {
        public const float Width = 2f;

        public CaretControl()
        {
            controlColorHex = "#FFFFFF";
            hitTestable = false;
        }
    }
}
