using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.EngineWork.Rendering.UI.Controls;
using ArctisAurora.EngineWork.Rendering.UI.Controls.Containers;
using Silk.NET.Maths;

namespace ArctisAurora.Core.Rendering.UI.Controls.Text
{
    public class TextControl : VulkanControl
    {
        public override void AddChild(Entity entity)
        {
            if (entity is not VulkanControl control) throw new Exception("Child entity must be a VulkanControl");

            children.Add(control);
            control.parent = this;

            // transform child
            Vector3D<float> transformedLoc = transform.position;
            if (control is not AbstractContainerControl container)
            {
                // map child horizontal and vertical pos to parent size
                transformedLoc.X += (control.horizontalPosition - 0.5f) * width;
                transformedLoc.Y += (control.verticalPosition - 0.5f) * height;
                //transformedLoc.Z = transform.position.Z + 0.01f;
            }
            control.transform.MoveToPosition(transformedLoc);
            //control.SetControlScale(new Vector2D<float>(width, height));
        }
    }
}