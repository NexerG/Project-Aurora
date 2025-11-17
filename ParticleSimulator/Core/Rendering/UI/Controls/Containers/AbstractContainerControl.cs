using ABI.System.Numerics;
using ArctisAurora.EngineWork.EngineEntity;
using ArctisAurora.EngineWork.Rendering.UI.Controls.Interactable;
using Silk.NET.Maths;
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
        public abstract void RecalculateLayout();
    }
}