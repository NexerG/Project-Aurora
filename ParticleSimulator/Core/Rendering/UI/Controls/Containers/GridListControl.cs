using Silk.NET.Maths;
using static ArctisAurora.Core.Rendering.UI.Controls.Containers.StackPanelLevelSettings;
using HorizontalAlignment = ArctisAurora.Core.Rendering.UI.Controls.Containers.StackPanelLevelSettings.HorizontalAlignment;

namespace ArctisAurora.EngineWork.Rendering.UI.Controls.Containers
{
    [A_VulkanControlElement("GridListRowSettings", "Settings for grid's rows")]
    public class GridListRowSettings
    {
        [A_VulkanControlProperty("Bounds", "Level bounds for the grid's rows")]
        public LevelBounds bounds = LevelBounds.ScaleToContent;
        [A_VulkanControlProperty("Height", "Grid's height")]
        public float height;
        [A_VulkanControlProperty("Alignment", "Horizontal alignment of the grid's rows")]
        public HorizontalAlignment horizontalAlignment = HorizontalAlignment.Left;
        [A_VulkanControlProperty("VerticalAlignment", "Vertical alignment of the grid's rows")]
        public VerticalAlignment verticalAlignment = VerticalAlignment.Center;
    }

    [A_VulkanControlElement("GridListColumnSettings", "Settings for grid's columns")]
    public class GridListColumnSettings
    {
        [A_VulkanControlProperty("Bounds", "Level bounds for the grid's columns")]
        public LevelBounds bounds = LevelBounds.ScaleToContent;
        [A_VulkanControlProperty("Width", "Grid's width")]
        public float width;
        [A_VulkanControlProperty("Alignment", "Horizontal alignment of the grid's columns")]
        public HorizontalAlignment horizontalAlignment = HorizontalAlignment.Left;
        [A_VulkanControlProperty("VerticalAlignment", "Vertical alignment of the grid's columns")]
        public VerticalAlignment verticalAlignment = VerticalAlignment.Center;
    }


    [A_VulkanControl("GridList")]
    internal class GridListControl : AbstractContainerControl
    {
        [A_VulkanControlProperty("CellWidth")]
        public float cellWidth = 40;
        [A_VulkanControlProperty("CellHeight")]
        public float cellHeight = 35;

        [A_VulkanControlProperty("HorizontalSpacing")]
        public float horizontalSpacing = 100;
        [A_VulkanControlProperty("VerticalSpacing")]
        public float verticalSpacing = 15;

        [A_VulkanControlProperty("HorizontalMargin")]
        public float horizontalMargin = 10;
        [A_VulkanControlProperty("VerticalMargin")]
        public float verticalMargin = 10;

        public override void AddControlToContainer(VulkanControl control)
        {
            //organize the list in a alpahebetical order and then position them in a grid
            //children = children.OrderBy(c => c.name).ToList();
            //UpdateLayout();
            Measure();
            MeasureSelf();
            Arrange();
        }

        public override void Arrange()
        {
            //throw new NotImplementedException();
        }

        public override void Measure()
        {
            throw new NotImplementedException();
        }

        public override void MeasureSelf()
        {
            //throw new NotImplementedException();
        }

        public override void RecalculateLayout()
        {
            //throw new NotImplementedException();
        }

        public void UpdateLayout()
        {
            transform.SetWorldPosition(parent.transform.position);
            transform.SetWorldScale(parent.transform.scale);
            float maxRows = MathF.Floor(transform.scale.Y / (cellHeight + verticalSpacing));
                float maxCols = MathF.Floor(transform.scale.X / (cellWidth + horizontalSpacing)) * 2;
            if (parent != null && parent.GetType() == typeof(WindowControl))
            {
                maxRows = MathF.Floor((Engine.window.windowSize.Height - verticalMargin) / (cellHeight + verticalSpacing));
                maxCols = MathF.Floor((Engine.window.windowSize.Width - horizontalMargin) / (cellWidth + horizontalSpacing));
            }
            float depth = parent.transform.position.Z + 0.01f;
            for (int i = 0; i < children.Count; i++)
            {
                int row = (int)(i / maxCols);
                int col = (int)(i % maxCols);
                float xPos = (col * (cellWidth + horizontalSpacing)) + (cellWidth / 2) + horizontalMargin / 2;
                float yPos = (row * (cellHeight + verticalSpacing)) + (cellHeight / 2) + verticalMargin / 2;
                children[i].transform.SetWorldPosition(new Vector3D<float>(xPos, yPos, depth));
                children[i].transform.SetWorldScale(new Vector3D<float>(cellWidth, cellHeight, 1));
            }
        }
    }
}