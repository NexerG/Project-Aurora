using ArctisAurora.EngineWork.Rendering.Renderers.OpenTK;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ArctisAurora.EngineWork.ECS.RenderingComponents.OpenTK
{
    internal class SpotlightComponent_OpenTK : LightSourceComponent_OpenTK
    {
        internal float _inncerCone = 90f;
        internal float _outterCone = 120f;

        public override void Draw(ShaderClass shader, Camera camera)
        {
            base.Draw(shader, camera);
        }
    }
}
