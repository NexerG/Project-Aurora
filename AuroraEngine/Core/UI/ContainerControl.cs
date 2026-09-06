using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.Registry;

namespace ArctisAurora.Core.UI
{
    // The base every control that holds more than one child derives from. A plain Control takes one;
    // this takes any number and leaves the arranging to whoever derives from it.
    [A_XSDType("NextContainer", "UI")]
    public class ContainerControl : Control
    {
        public ContainerControl()
        {
            horizontalAlignment = HorizontalAlignment.Stretch;
            verticalAlignment = VerticalAlignment.Stretch;
        }

        public override void AddChild(Entity entity)
        {
            if (entity is not Control control)
                throw new Exception("Child entity must be a Control");

            children.Add(entity);
            control.parent = this;
            MarkTreeOrderDirty();
            InvalidateLayout();
        }
    }
}
