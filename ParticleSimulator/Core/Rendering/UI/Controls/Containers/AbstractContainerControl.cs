using ArctisAurora.EngineWork.Rendering.UI.Controls.Interactable;

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

    [AttributeUsage(AttributeTargets.Enum, Inherited = false)]
    public sealed class A_VulkanEnumAttribute : Attribute
    {
        public string Name { get; }
        public A_VulkanEnumAttribute(string name)
        {
            Name = name;
        }
    }

    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    public sealed class A_VulkanActionAttribute : Attribute
    { }

    public abstract class AbstractContainerControl : AbstractInteractableControl
    {
        public List<VulkanControl> children = new List<VulkanControl>();

        public AbstractContainerControl()
        {

        }

        public AbstractContainerControl(VulkanControl parent)
        {
            this.parent = parent;
        }

        public override void AddChild(VulkanControl control)
        {
            children.Add(control);
            AddControlToContainer(control);
        }

        public abstract void AddControlToContainer(VulkanControl control);
    }
}