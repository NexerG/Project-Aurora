using Silk.NET.Maths;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace ArctisAurora.EngineWork.Rendering.UI.Controls.Containers
{
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
            children = children.OrderBy(c => c.name).ToList();
            UpdateLayout();
        }

        public override void Arrange()
        {
            throw new NotImplementedException();
        }

        public override void Measure(VulkanControl control)
        {
            throw new NotImplementedException();
        }

        public override void RecalculateLayout()
        {
            throw new NotImplementedException();
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