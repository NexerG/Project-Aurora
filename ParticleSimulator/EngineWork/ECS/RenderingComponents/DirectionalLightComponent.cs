using ArctisAurora.EngineWork.Rendering;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel.VoiceCommands;

namespace ArctisAurora.EngineWork.ECS.RenderingComponents
{
    internal class DirectionalLightComponent : LightSourceComponent
    {
        internal Vector3 direction = new Vector3(1,1,1);
        public override void Draw(ShaderClass shader, Camera camera)
        {
            direction.Normalize();
            base.Draw(shader, camera);
        }
    }
}
