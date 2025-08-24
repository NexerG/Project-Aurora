using Silk.NET.Maths;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ArctisAurora.EngineWork.Rendering.UI.Controls.Interactable
{
    internal class ButtonControl : AbstractInteractableControl
    {
        public ButtonControl()
        {
            controlData.style.tintDefault = new Vector3D<float>(0.55f, 0.55f, 0.55f);
        }

        public override void OnStart()
        {
            UpdateControlData();
            base.OnStart();
        }
    }
}
