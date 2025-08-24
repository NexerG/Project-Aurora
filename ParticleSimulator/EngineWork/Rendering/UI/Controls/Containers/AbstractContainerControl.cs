using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ArctisAurora.EngineWork.Rendering.UI.Controls.Containers
{
    internal abstract class AbstractContainerControl : AbstractInteractableControl
    {
        internal AbstractContainerControl(VulkanControl parent)
        {
            this.parent = parent;
        }

        internal abstract void AddControlToContainer(VulkanControl control);
    }
}