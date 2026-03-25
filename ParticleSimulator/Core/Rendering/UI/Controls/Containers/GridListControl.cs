using ArctisAurora.Core.AssetRegistry;
using Silk.NET.Maths;

namespace ArctisAurora.EngineWork.Rendering.UI.Controls.Containers
{
    /*[A_XSDType("GridListRowSettings", "UI", description:"Settings for grid's rows")]
    public class GridListRowSettings
    {
        [A_XSDElementProperty("Bounds", "UI", "Level bounds for the grid's rows")]
        public LevelBounds bounds = LevelBounds.ScaleToContent;
        [A_XSDElementProperty("Height","UI", "Grid's height")]
        public float height;
        [A_XSDElementProperty("Alignment", "UI", "Horizontal alignment of the grid's rows")]
        public HorizontalAlignment horizontalAlignment = HorizontalAlignment.Left;
        [A_XSDElementProperty("VerticalAlignment", "UI", "Vertical alignment of the grid's rows")]
        public VerticalAlignment verticalAlignment = VerticalAlignment.Center;
    }

    [A_XSDType("GridListColumnSettings", "UI", description:"Settings for grid's columns")]
    public class GridListColumnSettings
    {
        [A_XSDElementProperty("Bounds", "UI", "Level bounds for the grid's columns")]
        public LevelBounds bounds = LevelBounds.ScaleToContent;
        [A_XSDElementProperty("Width", "Grid's width")]
        public float width;
        [A_XSDElementProperty("Alignment", "UI", "Horizontal alignment of the grid's columns")]
        public HorizontalAlignment horizontalAlignment = HorizontalAlignment.Left;
        [A_XSDElementProperty("VerticalAlignment", "UI", "Vertical alignment of the grid's columns")]
        public VerticalAlignment verticalAlignment = VerticalAlignment.Center;
    }*/


    [A_XSDType("GridList", "UI", AllowedChildren = typeof(IXMLChild_UI))]
    internal class GridListControl : AbstractContainerControl
    {
        [A_XSDElementProperty("CellWidth", "UI")]
        public float cellWidth = 40;
        [A_XSDElementProperty("CellHeight", "UI")]
        public float cellHeight = 35;

        [A_XSDElementProperty("HorizontalSpacing", "UI")]
        public float horizontalSpacing = 100;
        [A_XSDElementProperty("VerticalSpacing", "UI")]
        public float verticalSpacing = 15;

        [A_XSDElementProperty("HorizontalMargin", "UI")]
        public float horizontalMargin = 10;
        [A_XSDElementProperty("VerticalMargin", "UI")]
        public float verticalMargin = 10;

        /*public override void AddControlToContainer(VulkanControl control)
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
        }*/

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