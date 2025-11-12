using ArctisAurora.EngineWork.EngineEntity;
using ArctisAurora.EngineWork.Rendering;
using ArctisAurora.EngineWork.Rendering.UI.Controls;
using ArctisAurora.EngineWork.Rendering.UI.Controls.Interactable;

namespace ArctisAurora.EngineWork
{
    // Entity manager is a singleton working with entities as data.
    public class EntityManager
    {
        public static EntityManager manager;



        private static List<Entity> _entities = new List<Entity>();

        private static VulkanControl _uiTree;
        private static List<VulkanControl> _controls = new List<VulkanControl>();
        private static List<Entity> _entitiesToRender = new List<Entity>();

        private static List<Entity> _entitiesToUpdate = new List<Entity>();
        private static List<Entity> _onStartEntities = new List<Entity>();
        private static List<Entity> _onDestroyedEntities = new List<Entity>();

        //----------

        public static IReadOnlyList<Entity> entities => _entities;

        public static VulkanControl uiTree
        {
            get => _uiTree;
            set => _uiTree = value;
        }
        public static IReadOnlyList<VulkanControl> controls => _controls;
        public static IReadOnlyList<Entity> entitiesToRender => _entitiesToRender;


        public static IReadOnlyList<Entity> entitiesToUpdate => _entitiesToUpdate;
        public static IReadOnlyList<Entity> onStartEntities => _onStartEntities;
        public static IReadOnlyList<Entity> onDestroyEntities => _onDestroyedEntities;

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

            Renderer.renderer.UpdateModules();
        }

        public static void AddEntityToUpdate(Entity entity)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            _entitiesToUpdate.Add(entity);
        }

        public static void RemoveEntityUpdate(int start, int end)
        {
            _entitiesToUpdate.RemoveRange(start, end - start);
        }

        public static void AddEntityToRender(Entity entity)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            _entitiesToRender.Add(entity);
        }

        public static void EntityCreated(Entity entity)
        {
            _onStartEntities.Add(entity);
        }

        public static void ClearOnStart()
        {
            _onStartEntities.Clear();
        }

        public static void ClearOnDestroy()
        {
            _onDestroyedEntities.Clear();
        }
    }
}
