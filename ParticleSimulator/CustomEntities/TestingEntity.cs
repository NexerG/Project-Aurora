using ArctisAurora.CustomEntityComponents;
using ArctisAurora.EngineWork.ECS.RenderingComponents;
using ArctisAurora.GameObject;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ArctisAurora.CustomEntities
{
    internal class TestingEntity : Entity
    {
        public override void OnStart()
        {
            this.transform.scale = new Vector3(25, 25, 25);
            this.transform.position = new Vector3(350, 350, 350);
            this.CreateComponent<MeshComponent>();
            base.OnStart();
        }
        public override void OnTick()
        {
            base.OnTick();
        }
    }
}
