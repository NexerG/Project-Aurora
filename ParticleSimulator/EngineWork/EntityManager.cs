using ArctisAurora.EngineWork.ECS.RenderingComponents.Vulkan;
using ArctisAurora.EngineWork.EngineEntity;
using ArctisAurora.EngineWork.Renderer.UI.Controls;

namespace ArctisAurora.EngineWork
{
    // Entity manager is a singleton working with entities as data.
    internal class EntityManager
    {
        internal static EntityManager manager;

        private static List<Entity> _entities = new List<Entity>();
        private static List<VulkanControl> _controls = new List<VulkanControl>();

        //private static List<MeshComponent> _entitiesToRender = new List<MeshComponent>();

        //----------

        internal static IReadOnlyList<Entity> entities => _entities;
        internal static IReadOnlyList<Entity> controls => _controls;
        //internal static IReadOnlyList<MeshComponent> entitiesToRender => _entitiesToRender;

        //internal static List<Entity> physicsEntities = new List<Entity>();

        public EntityManager()
        {
            if (manager == null)
            {
                manager = this;
            }
            else
            {
                throw new Exception("EntityManager already exists!");
            }
        }

        public static void AddEntity(Entity entity)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            _entities.Add(entity);
        }

        public static void AddControl(VulkanControl control)
        {
            if (control == null) throw new ArgumentNullException(nameof(control));
            _controls.Add(control);
        }

        //public static void AddMeshComponent(MeshComponent meshComponent)
        //{
        //    if (meshComponent == null) throw new ArgumentNullException(nameof(meshComponent));
        //    _entitiesToRender.Add(meshComponent);
        //}
    }
}
