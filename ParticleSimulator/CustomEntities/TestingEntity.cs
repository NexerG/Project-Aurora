﻿using ArctisAurora.EngineWork.ECS.RenderingComponents.OpenTK;
using ArctisAurora.EngineWork.ECS.RenderingComponents.Vulkan;
using ArctisAurora.GameObject;

namespace ArctisAurora.CustomEntities
{
    internal class TestingEntity : Entity
    {
        public TestingEntity() 
        {
            this.CreateComponent<AVulkanMeshComponent>();
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
