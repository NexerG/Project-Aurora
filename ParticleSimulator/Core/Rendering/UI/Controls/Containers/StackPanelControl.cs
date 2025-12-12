using ArctisAurora.EngineWork.Rendering.UI.Controls;
using ArctisAurora.EngineWork.Rendering.UI.Controls.Containers;
using Silk.NET.Maths;
using System.Windows.Forms;
using static ArctisAurora.EngineWork.Rendering.UI.Controls.VulkanControl;

namespace ArctisAurora.Core.Rendering.UI.Controls.Containers
{
    [A_VulkanControlElement("LevelSettings")]
    public class StackPanelLevelSettings
    {
        #region enums
        [A_VulkanEnum("LevelScaling")]
        public enum LevelBounds
        {
            ScaleToContent, Fill, HardScale
        }

        [A_VulkanEnum("HorizontalAlignment")]
        public enum HorizontalAlignment
        {
            Center, Left, Right
        }

        [A_VulkanEnum("VeticalAlignment")]
        public enum VerticalAlignment
        {
            Top, Center, Bottom
        }
        #endregion

        [A_VulkanControlProperty("Height", "Sets the height of the level when using scalar.")]
        public float height = 0;
        [A_VulkanControlProperty("Width", "Sets the width of the level when using scalar.")]
        public float width = 0;
        [A_VulkanControlProperty("Spacing")]
        public float spacing = 0;

        [A_VulkanControlProperty("WidthScaling", "Sets how the width scales on this level")]
        public LevelBounds widthScaling = LevelBounds.ScaleToContent;
        [A_VulkanControlProperty("HeightScaling", "Sets how the height scales on this level")]
        public LevelBounds heightScaling = LevelBounds.ScaleToContent;
        [A_VulkanControlProperty("HorizontalAlignment", "Sets the horizontal justification for this level")]
        public HorizontalAlignment horizontalAlignment = HorizontalAlignment.Left;
        [A_VulkanControlProperty("VerticalAlignment", "Sets the vertical alignment for this level")]
        public VerticalAlignment verticalAlignment = VerticalAlignment.Center;

        public Vector3D<float> nextPosition = new Vector3D<float>(0, 0, 0);

        public Vector2D<float> bounds = new Vector2D<float>(0, 0);
        public Vector2D<float> position = new Vector2D<float>(0, 0);
        public List<VulkanControl> children = new List<VulkanControl>();
    }


    [A_VulkanControl("StackPanel")]
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
        [A_VulkanControlProperty("HorizontalMargin")]
        public float horizontalMargin = 10;
        [A_VulkanControlProperty("VerticalMargin")]
        public float verticalMargin = 10;

        // settings
        [A_VulkanControlProperty("Orientation")]
        public Orientation orientation = Orientation.Vertical;
        #endregion

        #region private_elements
        [A_VulkanControlProperty("LevelSettings")]
        private List<StackPanelLevelSettings> _stackPanelLevelSettings = new List<StackPanelLevelSettings>();

        public List<StackPanelLevelSettings> stackPanelLevelSettings
        {
            get => _stackPanelLevelSettings;
            set
            {
                _stackPanelLevelSettings = value;
            }
        }
        #endregion

        /*public override int height
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
        }*/

        //private bool reposition = false;

        public override void AddControlToContainer(VulkanControl control)
        {
            stackPanelLevelSettings[control.stackIndex].children.Add(control);
            Measure(control);
            MeasureSelf();
            Arrange();
        }

        public override void RecalculateLayout()
        {
            //throw new NotImplementedException();
        }
        
        public override void Measure(VulkanControl control)
        {
            if(orientation == Orientation.Vertical)
            {
                stackPanelLevelSettings[control.stackIndex].bounds.Y = Math.Max(stackPanelLevelSettings[control.stackIndex].bounds.Y, control.height);
                switch(stackPanelLevelSettings[control.stackIndex].widthScaling)
                {
                    case StackPanelLevelSettings.LevelBounds.ScaleToContent:
                        stackPanelLevelSettings[control.stackIndex].bounds.X += control.width + stackPanelLevelSettings[control.stackIndex].spacing;
                        break;
                    case StackPanelLevelSettings.LevelBounds.Fill:
                        stackPanelLevelSettings[control.stackIndex].bounds.X = transform.scale.X - (horizontalMargin * 2);
                        break;
                    case StackPanelLevelSettings.LevelBounds.HardScale:
                        stackPanelLevelSettings[control.stackIndex].bounds.X = stackPanelLevelSettings[control.stackIndex].width;
                        break;
                }
            }
            else
            {
                stackPanelLevelSettings[control.stackIndex].bounds.X = Math.Max(stackPanelLevelSettings[control.stackIndex].bounds.X, control.width);
                switch(stackPanelLevelSettings[control.stackIndex].heightScaling)
                {
                    case StackPanelLevelSettings.LevelBounds.ScaleToContent:
                        stackPanelLevelSettings[control.stackIndex].bounds.Y += control.height + stackPanelLevelSettings[control.stackIndex].spacing;
                        break;
                    case StackPanelLevelSettings.LevelBounds.Fill:
                        stackPanelLevelSettings[control.stackIndex].bounds.Y = transform.scale.Y - (verticalMargin * 2);
                        break;
                    case StackPanelLevelSettings.LevelBounds.HardScale:
                        stackPanelLevelSettings[control.stackIndex].bounds.Y = stackPanelLevelSettings[control.stackIndex].height;
                        break;
                }
            }
        }

        private void MeasureSelf()
        {
            //figure out how much space is left after fixed sizes
            Vector2D<float> availableSpace = new Vector2D<float>(transform.scale.X - (horizontalMargin * stackPanelLevelSettings.Count), transform.scale.Y - (verticalMargin * stackPanelLevelSettings.Count));
            int fillCount = 0;
            foreach (var level in stackPanelLevelSettings)
            {
                if(orientation == Orientation.Horizontal)
                {
                    if (level.widthScaling == StackPanelLevelSettings.LevelBounds.Fill)
                        fillCount++;
                    else
                        availableSpace.X -= level.bounds.X;
                }
                else
                {
                    if (level.heightScaling == StackPanelLevelSettings.LevelBounds.Fill)
                        fillCount++;
                    else
                        availableSpace.Y -= level.bounds.Y;
                }
            }
            if (fillCount > 0)
            {
                if (orientation == Orientation.Horizontal)
                {
                    availableSpace.X /= fillCount;
                }
                else
                {
                    availableSpace.Y /= fillCount;
                }
                //apply available space to fill levels
                foreach (var level in stackPanelLevelSettings)
                {
                    if (orientation == Orientation.Horizontal)
                    {
                        if (level.widthScaling == StackPanelLevelSettings.LevelBounds.Fill)
                            level.bounds.X = availableSpace.X;
                        if (level.heightScaling == StackPanelLevelSettings.LevelBounds.Fill)
                            level.bounds.Y = availableSpace.Y;
                    }
                    else
                    {
                        if (level.heightScaling == StackPanelLevelSettings.LevelBounds.Fill)
                            level.bounds.Y = availableSpace.Y;
                        if (level.widthScaling == StackPanelLevelSettings.LevelBounds.Fill)
                            level.bounds.X = availableSpace.X;
                    }
                }
            }

            // calculate each level's center position based on bounds and justification
            for (int i = 0; i < stackPanelLevelSettings.Count; i++)
            {
                Vector2D<float> pos = new Vector2D<float>(0, 0);
                if(orientation == Orientation.Horizontal)
                {
                    switch (stackPanelLevelSettings[i].verticalAlignment)
                    {
                        case StackPanelLevelSettings.VerticalAlignment.Center:
                            pos.Y = transform.scale.Y / 2;
                            break;
                        case StackPanelLevelSettings.VerticalAlignment.Top:
                            pos.Y = verticalMargin + (stackPanelLevelSettings[i].bounds.Y / 2);
                            break;
                        case StackPanelLevelSettings.VerticalAlignment.Bottom:
                            pos.Y = transform.scale.Y - (stackPanelLevelSettings[i].bounds.Y / 2) - verticalMargin;
                            break;
                    }
                    for(int k = i - 1; k >= 0; k--)
                    {
                        pos.X += stackPanelLevelSettings[k].bounds.X + stackPanelLevelSettings[k].spacing;
                    }
                    pos.X += horizontalMargin + stackPanelLevelSettings[i].bounds.X / 2;
                }
                else
                {
                    switch (stackPanelLevelSettings[i].horizontalAlignment)
                    {
                        case StackPanelLevelSettings.HorizontalAlignment.Center:
                            pos.X = transform.scale.X / 2;
                            break;
                        case StackPanelLevelSettings.HorizontalAlignment.Left:
                            pos.X = horizontalMargin + (stackPanelLevelSettings[i].bounds.X / 2);
                            break;
                        case StackPanelLevelSettings.HorizontalAlignment.Right:
                            pos.X = transform.scale.X - (stackPanelLevelSettings[i].bounds.X / 2) - horizontalMargin;
                            break;
                    }
                    for (int k = i - 1; k >= 0; k--)
                    {
                        pos.Y += stackPanelLevelSettings[k].bounds.Y + stackPanelLevelSettings[k].spacing;
                    }
                    pos.Y += verticalMargin + stackPanelLevelSettings[i].bounds.Y / 2;
                }
                stackPanelLevelSettings[i].position = pos;
            }

            // calculate starting nextPosition for each level
            for (int i = 0; i < stackPanelLevelSettings.Count; i++)
            {
                Vector3D<float> startPosRelative = new Vector3D<float>();
                switch (stackPanelLevelSettings[i].horizontalAlignment)
                {
                    case StackPanelLevelSettings.HorizontalAlignment.Center:
                        startPosRelative.X = startPosRelative.X - (stackPanelLevelSettings[i].bounds.X / 2);
                        break;
                    case StackPanelLevelSettings.HorizontalAlignment.Right:
                        startPosRelative.X = startPosRelative.X - (stackPanelLevelSettings[i].bounds.X / 2);
                        break;
                    case StackPanelLevelSettings.HorizontalAlignment.Left:
                        startPosRelative.X = startPosRelative.X - (stackPanelLevelSettings[i].bounds.X / 2);
                        break;
                }

                switch (stackPanelLevelSettings[i].verticalAlignment)
                {
                    case StackPanelLevelSettings.VerticalAlignment.Top:
                        startPosRelative.Y = startPosRelative.Y - (stackPanelLevelSettings[i].bounds.Y / 2);
                        break;
                    case StackPanelLevelSettings.VerticalAlignment.Center:
                        startPosRelative.Y += 0;
                        break;
                    case StackPanelLevelSettings.VerticalAlignment.Bottom:
                        startPosRelative.Y = startPosRelative.Y + (stackPanelLevelSettings[i].bounds.Y / 2);
                        break;
                }

                stackPanelLevelSettings[i].nextPosition = startPosRelative;
            }
        }

        private Vector3D<float> CalcPos(VulkanControl control)
        {
            float z = transform.position.Z;
            Vector3D<float> halfscale = new Vector3D<float>(control.width, control.height, 0) / 2;
            Vector3D<float> pos = new Vector3D<float>(stackPanelLevelSettings[control.stackIndex].position.X, stackPanelLevelSettings[control.stackIndex].position.Y, 0);
            if (orientation == Orientation.Horizontal)
            {
                // vertical alignment
                if (stackPanelLevelSettings[control.stackIndex].verticalAlignment == StackPanelLevelSettings.VerticalAlignment.Bottom)
                {
                    pos = pos + stackPanelLevelSettings[control.stackIndex].nextPosition;
                    pos.Y = pos.Y - halfscale.Y;
                    stackPanelLevelSettings[control.stackIndex].nextPosition.Y = stackPanelLevelSettings[control.stackIndex].nextPosition.Y - halfscale.Y * 2 - stackPanelLevelSettings[control.stackIndex].spacing;
                }
                else
                {
                    pos = pos + stackPanelLevelSettings[control.stackIndex].nextPosition;
                    pos.Y = pos.Y + halfscale.Y;
                    stackPanelLevelSettings[control.stackIndex].nextPosition.Y = stackPanelLevelSettings[control.stackIndex].nextPosition.Y + halfscale.Y * 2 + stackPanelLevelSettings[control.stackIndex].spacing;
                }
                // horizontal alignment
                if(stackPanelLevelSettings[control.stackIndex].horizontalAlignment == StackPanelLevelSettings.HorizontalAlignment.Right)
                {
                    pos.X = pos.X - halfscale.X;
                }
                else if(stackPanelLevelSettings[control.stackIndex].horizontalAlignment == StackPanelLevelSettings.HorizontalAlignment.Left)
                {
                    pos.X = pos.X + halfscale.X;
                }
            }
            else
            {
                if(stackPanelLevelSettings[control.stackIndex].horizontalAlignment == StackPanelLevelSettings.HorizontalAlignment.Right)
                {
                    pos = pos - stackPanelLevelSettings[control.stackIndex].nextPosition;
                    pos.X = pos.X - halfscale.X;
                    stackPanelLevelSettings[control.stackIndex].nextPosition.X = stackPanelLevelSettings[control.stackIndex].nextPosition.X - halfscale.X * 2 + stackPanelLevelSettings[control.stackIndex].spacing;
                }
                else
                {
                    pos = pos + stackPanelLevelSettings[control.stackIndex].nextPosition;
                    pos.X = pos.X + halfscale.X;
                    stackPanelLevelSettings[control.stackIndex].nextPosition.X = stackPanelLevelSettings[control.stackIndex].nextPosition.X + halfscale.X * 2 + stackPanelLevelSettings[control.stackIndex].spacing;
                }
                // vertical alignment
                if (stackPanelLevelSettings[control.stackIndex].verticalAlignment == StackPanelLevelSettings.VerticalAlignment.Bottom)
                {
                    pos.Y = pos.Y - halfscale.Y;
                }
                else if (stackPanelLevelSettings[control.stackIndex].verticalAlignment == StackPanelLevelSettings.VerticalAlignment.Top)
                {
                    pos.Y = pos.Y + halfscale.Y;
                }
            }
            pos.Z = z;

            return pos;
        }

        public override void Arrange()
        {
            foreach (VulkanControl child in children)
            {
                Vector3D<float> pos = CalcPos(child);
                child.transform.SetWorldPosition(pos);
            }
        } 
    }
}