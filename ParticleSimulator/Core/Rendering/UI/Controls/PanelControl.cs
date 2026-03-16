using ArctisAurora.Core.AssetRegistry;

namespace ArctisAurora.EngineWork.Rendering.UI.Controls
{
    [A_XSDType("Panel", "UI", AllowedChildren = typeof(IXMLChild_UI), MaxChildren = 1)]
    public class PanelControl : VulkanControl
    {
        public PanelControl() { }
    }
}