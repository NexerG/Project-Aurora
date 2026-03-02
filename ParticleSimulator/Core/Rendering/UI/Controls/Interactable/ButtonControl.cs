using ArctisAurora.Core.AssetRegistry;
using Silk.NET.Maths;

namespace ArctisAurora.EngineWork.Rendering.UI.Controls.Interactable
{
    [A_XSDElement("Button", "UI", "UI")]
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