using ArctisAurora.EngineWork.ECS.RenderingComponents;
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
            this.transform.position = new Vector3(300, 100, 300);
            this.CreateComponent<LightSourceComponent>();
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
