using ArctisAurora.Core.AssetRegistry;
using ArctisAurora.EngineWork.AssetRegistry;
using Silk.NET.Maths;

namespace ArctisAurora.EngineWork.Rendering.UI.Controls
{

    [A_XSDType("Window", "UI", AllowedChildren = typeof(IXMLChild_UI), MaxChildren = 1)]
    public class WindowControl : VulkanControl
    {
        //[A_VulkanEnum("WindowMode")]
        //public enum WindowMode
        //{
        //    Autoscale
        //}

        [A_XSDElementProperty("Fill", "UI")]
        public bool fillWindow = true;

        [A_XSDType("ContentScalingModeEnum", "UI")]
        public enum ScalingMode
        {
            Vertical, Horizontal, Both, None
        }
        [A_XSDElementProperty("ContentScalingMode", "UI")]
        public ScalingMode contentScalingMode = ScalingMode.Vertical;

        public WindowControl()
        {
            maskAsset = AssetRegistries.GetAsset<TextureAsset>("invisible");

            if (fillWindow)
            {
                SetSize(new Vector2D<float>(Engine.window.windowSize.Width, Engine.window.windowSize.Height));
            }
        }
    }
}