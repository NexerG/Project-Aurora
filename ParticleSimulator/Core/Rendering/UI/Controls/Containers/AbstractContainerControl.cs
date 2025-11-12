using ArctisAurora.EngineWork.EngineEntity;
using ArctisAurora.EngineWork.Rendering.UI.Controls.Interactable;
using System.Windows.Forms;

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

    public abstract class AbstractContainerControl : PanelControl
    {
        public AbstractContainerControl()
        {

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
    }
}