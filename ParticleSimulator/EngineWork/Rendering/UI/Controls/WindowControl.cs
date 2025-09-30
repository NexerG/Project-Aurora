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
        [XmlAttribute("Width")]
        public int Width;

        [XmlAttribute("Height")]
        public int Height;
    }
}