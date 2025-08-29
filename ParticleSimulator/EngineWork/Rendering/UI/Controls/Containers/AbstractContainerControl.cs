namespace ArctisAurora.EngineWork.Rendering.UI.Controls.Containers
{
    internal abstract class AbstractContainerControl : AbstractInteractableControl
    {
        internal AbstractContainerControl(VulkanControl parent)
        {
            this.parent = parent;
        }

        internal abstract void AddControlToContainer(VulkanControl control);
    }
}