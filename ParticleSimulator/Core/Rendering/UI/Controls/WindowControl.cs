using ArctisAurora.EngineWork.AssetRegistry;
using ArctisAurora.EngineWork.EngineEntity;
using ArctisAurora.EngineWork.Rendering.UI.Controls.Containers;
using Silk.NET.Maths;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace ArctisAurora.EngineWork.Rendering.UI.Controls
{

    [A_VulkanControl("Window")]
    public class WindowControl : VulkanControl
    {
        //[A_VulkanEnum("WindowMode")]
        //public enum WindowMode
        //{
        //    Autoscale
        //}

        [A_VulkanControlProperty("Fill")]
        public bool fillWindow = true;

        [A_VulkanEnum("ContentScalingModeEnum")]
        public enum ScalingMode
        {
            Vertical, Horizontal, Both, None
        }
        [A_VulkanControlProperty("ContentScalingMode")]
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