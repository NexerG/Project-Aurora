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
        public TestingEntity() 
        {
            this.CreateComponent<MeshComponent>();
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
