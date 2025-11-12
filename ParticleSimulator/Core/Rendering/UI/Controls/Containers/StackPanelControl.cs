using ArctisAurora.EngineWork.Rendering.UI.Controls;
using ArctisAurora.EngineWork.Rendering.UI.Controls.Containers;
using Silk.NET.Maths;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace ArctisAurora.Core.Rendering.UI.Controls.Containers
{
    [A_VulkanContainer("StackPanel")]
    public class StackPanelControl : AbstractContainerControl
    {
        [A_VulkanControlProperty("Spacing")]
        public float spacing = 5;
        [A_VulkanControlProperty("HorizontalMargin")]
        public float horizontalMargin = 10;
        [A_VulkanControlProperty("VerticalMargin")]
        public float verticalMargin = 10;

        [A_VulkanEnum("Orientation")]
        public enum Orientation
        {
            Horizontal,
            Vertical
        }

        [A_VulkanControlProperty("Orientation")]
        public Orientation orientation = Orientation.Vertical;

        public List<Vector3D<float>> offsets = new List<Vector3D<float>>();

        public override void AddControlToContainer(VulkanControl control)
        {
            //inserting new object into stackpanel
            while(offsets.Count <= control.stackIndex + 1)
            {
                offsets.Add(new Vector3D<float>(horizontalMargin, verticalMargin, 0));
            }


            control.transform.SetWorldPosition(CalcPos(control));
            if (orientation == Orientation.Horizontal)
            {
                offsets[control.stackIndex] += new Vector3D<float>(0, spacing + control.transform.scale.Y, 0);
                if (offsets[control.stackIndex + 1].X < control.transform.scale.X)
                {
                    Vector3D<float> adjust = new Vector3D<float>(control.transform.scale.X - offsets[control.stackIndex + 1].X + horizontalMargin - spacing, 0, 0) / 2;
                    foreach (VulkanControl child in children)
                    {
                        if (child.stackIndex > control.stackIndex)
                        {
                            child.transform.SetLocalPosition(adjust);
                        }
                    }
                    offsets[control.stackIndex + 1] = new Vector3D<float>(spacing + control.transform.scale.X, verticalMargin, 0);
                }
            }
            else
            {
                offsets[control.stackIndex] += new Vector3D<float>(spacing + control.transform.scale.X, 0, 0);
                if (offsets[control.stackIndex + 1].Y < control.transform.scale.Y)
                {
                    Vector3D<float> adjust = new Vector3D<float>(0, control.transform.scale.Y - offsets[control.stackIndex + 1].Y + verticalMargin - spacing, 0) / 2;
                    foreach (VulkanControl child in children)
                    {
                        if (child.stackIndex > control.stackIndex)
                        {
                            child.transform.SetLocalPosition(adjust);
                        }
                    }
                    offsets[control.stackIndex + 1] = new Vector3D<float>(horizontalMargin, spacing + control.transform.scale.Y, 0);
                }
            }

            children.Add(control);
        }

        private Vector3D<float> CalcPos(VulkanControl control)
        {
            Vector3D<float> halfScale = control.transform.scale / 2;
            float z = transform.position.Z;
            Vector3D<float> pos = halfScale;
            for (int i = 0; i < control.stackIndex; i++)
            {
                if (orientation == Orientation.Horizontal)
                    pos.X += offsets[i].X;
                else
                    pos.Y += offsets[i].Y;
            }
            pos += offsets[control.stackIndex];
            pos.Z = z;
            
            return pos;
        }
    }
}