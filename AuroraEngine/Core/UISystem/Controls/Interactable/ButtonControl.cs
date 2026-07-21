using ArctisAurora.Core.Registry;
using Silk.NET.Maths;

namespace ArctisAurora.Core.UISystem.Controls.Interactable
{
    [A_XSDType("Button", "UI", AllowedChildren = typeof(IXMLChild_UI), MaxChildren = 1)]
    public class ButtonControl : PanelControl
    {
        public ButtonControl()
        {
            controlData.style.tint = new Vector3D<float>(0.55f, 0.55f, 0.55f);
        }

        public override void OnStart()
        {
            base.OnStart();
            UpdateControlData();
        }
    }
}