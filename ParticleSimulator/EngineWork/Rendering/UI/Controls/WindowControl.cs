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
        [A_VulkanControlProperty("Width")]
        public int Width;

        [A_VulkanControlProperty("Height")]
        public int Height;
    }
}