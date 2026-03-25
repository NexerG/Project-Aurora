using ArctisAurora.Core.ECS.EngineEntity;

namespace ArctisAurora.EngineWork.Rendering.UI.Controls.Containers
{
    public abstract class AbstractContainerControl : PanelControl
    {
        public AbstractContainerControl()
        {
            verticalAlignment = VerticalAlignment.Stretch;
            horizontalAlignment = HorizontalAlignment.Stretch;
        }

        public AbstractContainerControl(VulkanControl parent)
        {
            this.parent = parent;
        }

        public override void AddChild(Entity entity)
        {
            if (entity is not VulkanControl control)
                throw new Exception("Child entity must be a VulkanControl");
            children.Add(entity);
            control.parent = this;
            InvalidateLayout();
        }
    }
}