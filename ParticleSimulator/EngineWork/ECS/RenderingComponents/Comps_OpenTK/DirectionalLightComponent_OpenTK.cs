using ArctisAurora.EngineWork.Rendering.Renderers.OpenTK;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel.VoiceCommands;

namespace ArctisAurora.EngineWork.ECS.RenderingComponents.OpenTK
{
    internal class DirectionalLightComponent_OpenTK : LightSourceComponent_OpenTK
    {
        internal Vector3 direction = new Vector3(1, 1, 1);
        public override void Draw(ShaderClass shader, Camera camera)
        {
            direction.Normalize();
            base.Draw(shader, camera);
        }
    }
}
