using ArctisAurora.EngineWork.ECS.RenderingComponents.OpenTK;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ArctisAurora.GameObject
{
    internal class LightSourceEntity : Entity
    {
        public LightSourceEntity()
        {
            this.CreateComponent<LightSourceComponent_OpenTK>();
            this.name = "Light Source Entity";
        }
        public override void OnStart()
        {
            base.OnStart();
        }

        public override void OnTick()
        {
            base.OnTick();
        }
    }
}
