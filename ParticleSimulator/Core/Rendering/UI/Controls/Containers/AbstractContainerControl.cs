using ArctisAurora.EngineWork.EngineEntity;

namespace ArctisAurora.EngineWork.Rendering.UI.Controls.Containers
{
    public abstract class AbstractContainerControl : PanelControl
    {
        public AbstractContainerControl()
        {
            scalingMode = ScalingMode.Stretch;
        }

        public AbstractContainerControl(VulkanControl parent)
        {
            this.parent = parent;
        }

        public override void AddChild(Entity entity)
        {
            if (entity is not VulkanControl control)
                throw new Exception("Only VulkanControl entities can be added to a VulkanContainerControl.");
            children.Add(entity);
            AddControlToContainer((VulkanControl)entity);
        }

        public abstract void AddControlToContainer(VulkanControl control);

        public abstract void RecalculateLayout();

        public abstract void Measure();

        public abstract void Arrange();

        public abstract void MeasureSelf();
    }
}