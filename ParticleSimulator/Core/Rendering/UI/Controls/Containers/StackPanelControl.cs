using ArctisAurora.EngineWork.Rendering.UI.Controls;
using ArctisAurora.EngineWork.Rendering.UI.Controls.Containers;
using Silk.NET.Maths;

namespace ArctisAurora.Core.Rendering.UI.Controls.Containers
{
    [A_VulkanUnlistedElement("RowSettings")]
    public class StackPanelRowSettingsElement
    {
        [A_VulkanControlProperty("CurrentX")]
        public float currentX = 0;
        [A_VulkanControlProperty("CurrentY")]
        public float currentY = 0;
        [A_VulkanControlProperty("MaxHeightInRow")]
        public float maxHeightInRow = 0;
        [A_VulkanControlProperty("MaxWidthInColumn")]
        public float maxWidthInColumn = 0;
    }


    [A_VulkanContainer("StackPanel")]
    public class StackPanelControl : AbstractContainerControl
    {
        #region enums
        [A_VulkanEnum("Orientation")]
        public enum Orientation
        {
            Horizontal,
            Vertical
        }

        [A_VulkanEnum("Alignment")]
        public enum Alignment
        {
            TopLeft,
            TopRight,
            BottomLeft,
            BottomRight
        }
        #endregion

        #region properties
        [A_VulkanControlProperty("Spacing")]
        public float spacing = 5;
        [A_VulkanControlProperty("HorizontalMargin")]
        public float horizontalMargin = 10;
        [A_VulkanControlProperty("VerticalMargin")]
        public float verticalMargin = 10;

        // settings
        [A_VulkanControlProperty("Orientation")]
        public Orientation orientation = Orientation.Vertical;

        [A_VulkanControlProperty("Alignment")]
        public Alignment alignment = Alignment.TopLeft;
        #endregion

        #region private_elements
        [A_VulkanControlProperty("RowSettings")]
        public List<StackPanelRowSettingsElement> rowSettings = new List<StackPanelRowSettingsElement>();
        #endregion

        public List<Vector3D<float>> offsets = new List<Vector3D<float>>();

        public override int height
        {
            get => int.MaxValue;
            set
            {
                base.height = value;
                RecalculateLayout();
            }
        }

        public override int width
        {
            get => int.MaxValue;
            set => base.width = value;
        }

        public override void AddControlToContainer(VulkanControl control)
        {
            children.Add(control);
        }

        public override void RecalculateLayout()
        {
            throw new NotImplementedException();
        }

        private void AddManually(VulkanControl control)
        {
            //inserting new object into stackpanel
            while (offsets.Count <= control.stackIndex + 1)
            {
                offsets.Add(new Vector3D<float>(horizontalMargin, verticalMargin, 0));
            }


            Vector3D<float> pos = CalcPos(control);
            switch (alignment)
            {
                case Alignment.TopLeft:
                    control.transform.SetWorldPosition(pos);
                    break;
                case Alignment.TopRight:
                    control.transform.SetWorldPosition(new Vector3D<float>(transform.scale.X - pos.X, pos.Y, pos.Z));
                    break;
                case Alignment.BottomLeft:
                    control.transform.SetWorldPosition(new Vector3D<float>(pos.X, transform.scale.Y - pos.Y, pos.Z));
                    break;
                case Alignment.BottomRight:
                    control.transform.SetWorldPosition(new Vector3D<float>(transform.scale.X - pos.X, transform.scale.Y - pos.Y, pos.Z));
                    break;
            }
            if (orientation == Orientation.Horizontal)
            {
                offsets[control.stackIndex] += new Vector3D<float>(0, spacing + control.transform.scale.Y, 0);
                if (offsets[control.stackIndex + 1].X < control.transform.scale.X)
                {
                    Vector3D<float> adjust = new Vector3D<float>(control.transform.scale.X - offsets[control.stackIndex + 1].X + horizontalMargin - spacing, 0, 0) / 2;
                    switch (alignment)
                    {
                        case Alignment.TopLeft:
                            break;
                        case Alignment.TopRight:
                            adjust = new Vector3D<float>(-adjust.X, adjust.Y, adjust.Z);
                            break;
                        case Alignment.BottomLeft:
                            adjust = new Vector3D<float>(adjust.X, -adjust.Y, adjust.Z);
                            break;
                        case Alignment.BottomRight:
                            adjust = new Vector3D<float>(-adjust.X, -adjust.Y, adjust.Z);
                            break;
                    }
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
                    switch (alignment)
                    {
                        case Alignment.TopLeft:
                            break;
                        case Alignment.TopRight:
                            adjust = new Vector3D<float>(-adjust.X, adjust.Y, adjust.Z);
                            break;
                        case Alignment.BottomLeft:
                            adjust = new Vector3D<float>(adjust.X, -adjust.Y, adjust.Z);
                            break;
                        case Alignment.BottomRight:
                            adjust = new Vector3D<float>(-adjust.X, -adjust.Y, adjust.Z);
                            break;
                    }
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