using ArctisAurora.EngineWork.ECS.RenderingComponents.Vulkan;
using ArctisAurora.GameObject;
using Silk.NET.Maths;

namespace ArctisAurora.CustomEntities
{
    internal class GlyphEntity : Entity
    {

        internal char character;

        internal GlyphEntity(char character, Vector3D<float> pos)
        {
            this.character = character;
            transform.SetWorldPosition(pos);
            CreateComponent<MeshComponent>();
        }
    }
}
