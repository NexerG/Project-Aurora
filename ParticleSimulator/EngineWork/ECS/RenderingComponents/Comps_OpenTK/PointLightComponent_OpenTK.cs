using ArctisAurora.EngineWork.Rendering.Renderers.OpenTK;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ArctisAurora.EngineWork.ECS.RenderingComponents.OpenTK
{
    internal class AVulkanPointLightComponent : LightSourceComponent_OpenTK
    {
        public override void Draw(ShaderClass shader, Camera camera)
        {
            base.Draw(shader, camera);
        }
    }
}
