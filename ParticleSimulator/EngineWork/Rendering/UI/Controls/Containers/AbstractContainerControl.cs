namespace ArctisAurora.EngineWork.Rendering.UI.Controls.Containers
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class A_VulkanContainerAttribute : Attribute
    {
        public string Name { get; }
        public A_VulkanContainerAttribute(string name)
        {
            Name = name;
        }
    }

    internal abstract class AbstractContainerControl : AbstractInteractableControl
    {
        internal AbstractContainerControl(VulkanControl parent)
        {
            this.parent = parent;
        }

        internal abstract void AddControlToContainer(VulkanControl control);
    }
}