using ArctisAurora.Core.AssetRegistry;
using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.UISystem.Controls;
using ArctisAurora.EngineWork;
using Silk.NET.Maths;

namespace ArctisAurora.Core.UISystem.Controls.Containers
{
    #region ---- ENUMS & DEFINITIONS ----
    [A_XSDType("GridSizeMode", category:"UI")]
    public enum GridSizeMode { Fixed, Auto, Star }

    [A_XSDType("RowDefinition", "UI")]
    public class RowDefinition
    {
        [A_XSDElementProperty("Height", "UI", "Pixel size for Fixed, weight for Star, ignored for Auto.")]
        public float value = 1f;

        [A_XSDElementProperty("SizeMode", "UI", "Fixed | Auto | Star")]
        public GridSizeMode sizeMode = GridSizeMode.Auto;

        // Resolved during Measure — not serialised
        public float resolvedSize;
    }

    [A_XSDType("ColumnDefinition", "UI")]
    public class ColumnDefinition
    {
        [A_XSDElementProperty("Width", "UI", "Pixel size for Fixed, weight for Star, ignored for Auto.")]
        public float value = 1f;

        [A_XSDElementProperty("SizeMode", "UI", "Fixed | Auto | Star")]
        public GridSizeMode sizeMode = GridSizeMode.Auto;

        public float resolvedSize;
    }

    [A_XSDType("GridCell", "UI")]
    public class GridCellAssignment
    {
        [A_XSDElementProperty("Row", "UI")]
        public int row = 0;
        [A_XSDElementProperty("Column", "UI")]
        public int column = 0;
        [A_XSDElementProperty("RowSpan", "UI")]
        public int rowSpan = 1;
        [A_XSDElementProperty("ColumnSpan", "UI")]
        public int columnSpan = 1;

        // Populated by RecursiveParse when a VulkanControl child is found inside <GridCell>
        public VulkanControl? child;
    }
    #endregion

    [A_XSDType("GridList", "UI", AllowedChildren = typeof(IXMLChild_UI))]
    internal class GridListControl : AbstractContainerControl
    {
        [A_XSDElementProperty("RowDefinition", "UI", "")]
        public List<RowDefinition> rowDefinitions = new List<RowDefinition>();
        [A_XSDElementProperty("ColumnDefinition", "UI", "")]
        public List<ColumnDefinition> columnDefinitions = new List<ColumnDefinition>();
        private List<GridCellAssignment> _cellAssignments = new List<GridCellAssignment>();

        public GridListControl()
        {
            preferredHeight = 0;
            preferredWidth = 0;
        }

        public override void AddChild(Entity entity)
        {
            if (entity is not VulkanControl control)
                throw new Exception("GridControl only accepts VulkanControl children.");
            base.AddChild(entity);
            GridCellAssignment cellAssignment = new GridCellAssignment { row = control.GridRow, column = control.GridColumn, child = control };
            int existing = _cellAssignments.FindIndex(a => a.child == cellAssignment.child);
            if (existing >= 0) _cellAssignments[existing] = cellAssignment;
            else
            {
                base.AddChild(cellAssignment.child);
                _cellAssignments.Add(cellAssignment);
            }

            _cellAssignments.Add(cellAssignment);
        }

        private void EnsureDefaults()
        {
            if (rowDefinitions.Count == 0)
                rowDefinitions.Add(new RowDefinition { sizeMode = GridSizeMode.Star, value = 1 });
            if (columnDefinitions.Count == 0)
                columnDefinitions.Add(new ColumnDefinition { sizeMode = GridSizeMode.Star, value = 1 });
        }

        public override Vector2D<float> Measure(Vector2D<float> availableSize)
        {
            EnsureDefaults();

            LayoutRect inner = new LayoutRect(0, 0, availableSize.X, availableSize.Y).Shrink(padding);

            int rows = rowDefinitions.Count;
            int cols = columnDefinitions.Count;

            // Pass 1 — resolve Fixed bands immediately.
            for (int r = 0; r < rows; r++)
                rowDefinitions[r].resolvedSize = rowDefinitions[r].sizeMode == GridSizeMode.Fixed
                    ? rowDefinitions[r].value : 0;
            for (int c = 0; c < cols; c++)
                columnDefinitions[c].resolvedSize = columnDefinitions[c].sizeMode == GridSizeMode.Fixed
                    ? columnDefinitions[c].value : 0;

            // Pass 2 — Auto bands: measure children that live entirely in that band (span == 1).
            foreach (var assignment in _cellAssignments)
            {
                if (assignment.child == null) continue;
                VulkanControl child = assignment.child;

                // Offer the child a rough size: full inner rect (we'll tighten in Arrange).
                Vector2D<float> childDesired = child.Measure(inner.size);

                if (assignment.rowSpan == 1 && rowDefinitions[assignment.row].sizeMode == GridSizeMode.Auto)
                    rowDefinitions[assignment.row].resolvedSize = MathF.Max(
                        rowDefinitions[assignment.row].resolvedSize,
                        childDesired.Y + child.margin.totalVertical);

                if (assignment.columnSpan == 1 && columnDefinitions[assignment.column].sizeMode == GridSizeMode.Auto)
                    columnDefinitions[assignment.column].resolvedSize = MathF.Max(
                        columnDefinitions[assignment.column].resolvedSize,
                        childDesired.X + child.margin.totalHorizontal);
            }

            // Pass 3 — distribute remaining space to Star bands.
            float fixedAndAutoH = rowDefinitions.Sum(r => r.resolvedSize);
            float fixedAndAutoW = columnDefinitions.Sum(c => c.resolvedSize);
            float starH = MathF.Max(0, inner.height - fixedAndAutoH);
            float starW = MathF.Max(0, inner.width - fixedAndAutoW);

            float totalRowStars = rowDefinitions.Where(r => r.sizeMode == GridSizeMode.Star).Sum(r => r.value);
            float totalColStars = columnDefinitions.Where(c => c.sizeMode == GridSizeMode.Star).Sum(c => c.value);

            for (int r = 0; r < rows; r++)
                if (rowDefinitions[r].sizeMode == GridSizeMode.Star)
                    rowDefinitions[r].resolvedSize = totalRowStars > 0
                        ? starH * (rowDefinitions[r].value / totalRowStars) : 0;

            for (int c = 0; c < cols; c++)
                if (columnDefinitions[c].sizeMode == GridSizeMode.Star)
                    columnDefinitions[c].resolvedSize = totalColStars > 0
                        ? starW * (columnDefinitions[c].value / totalColStars) : 0;

            float totalW = columnDefinitions.Sum(c => c.resolvedSize) + padding.totalHorizontal;
            float totalH = rowDefinitions.Sum(r => r.resolvedSize) + padding.totalVertical;

            if (preferredWidth > 0) totalW = preferredWidth;
            if (preferredHeight > 0) totalH = preferredHeight;

            DesiredSize = new Vector2D<float>(totalW, totalH);
            IsMeasureDirty = false;
            return DesiredSize;
        }

        public override void Arrange(LayoutRect finalRect)
        {
            EnsureDefaults();
            arrangedRect = finalRect;

            transform.SetWorldPosition(new Vector3D<float>(
                finalRect.x + finalRect.width / 2f,
                finalRect.y + finalRect.height / 2f,
                parent.transform.GetEntityPosition().Z + 0.001f));
            transform.SetWorldScale(new Vector3D<float>(finalRect.width, finalRect.height, 1));

            ClipRect = parent is VulkanControl p
                ? (clipOutOfBounds ? LayoutRect.Intersect(finalRect, p.ClipRect) : p.ClipRect)
                : finalRect;

            // Build row/col offsets (top-left corner of each band).
            LayoutRect inner = finalRect.Shrink(padding);

            float[] rowOffsets = BuildOffsets(rowDefinitions.Select(r => r.resolvedSize).ToArray(), inner.y);
            float[] colOffsets = BuildOffsets(columnDefinitions.Select(c => c.resolvedSize).ToArray(), inner.x);

            foreach (var assignment in _cellAssignments)
            {
                if (assignment.child == null) continue;
                VulkanControl child = assignment.child;

                int r = Math.Clamp(assignment.row, 0, rowDefinitions.Count - 1);
                int c = Math.Clamp(assignment.column, 0, columnDefinitions.Count - 1);
                int rEnd = Math.Clamp(r + assignment.rowSpan, 1, rowDefinitions.Count);
                int cEnd = Math.Clamp(c + assignment.columnSpan, 1, columnDefinitions.Count);

                float cellX = colOffsets[c];
                float cellY = rowOffsets[r];
                float cellW = colOffsets[cEnd] - cellX;
                float cellH = rowOffsets[rEnd] - cellY;

                // Apply child margin and alignment within the cell.
                LayoutRect cellRect = new LayoutRect(cellX, cellY, cellW, cellH).Shrink(child.margin);

                float childW = child.horizontalAlignment == HorizontalAlignment.Stretch
                    ? cellRect.width : MathF.Min(child.DesiredSize.X, cellRect.width);
                float childH = child.verticalAlignment == VerticalAlignment.Stretch
                    ? cellRect.height : MathF.Min(child.DesiredSize.Y, cellRect.height);

                float ox = child.horizontalAlignment switch
                {
                    HorizontalAlignment.Center => (cellRect.width - childW) * 0.5f,
                    HorizontalAlignment.Right => cellRect.width - childW,
                    _ => 0f
                };
                float oy = child.verticalAlignment switch
                {
                    VerticalAlignment.Center => (cellRect.height - childH) * 0.5f,
                    VerticalAlignment.Bottom => cellRect.height - childH,
                    _ => 0f
                };

                child.Arrange(new LayoutRect(cellRect.x + ox, cellRect.y + oy, childW, childH));
            }

            isArrangeDirty = false;
        }
        private static float[] BuildOffsets(float[] sizes, float start)
        {
            float[] offsets = new float[sizes.Length + 1];
            offsets[0] = start;
            for (int i = 0; i < sizes.Length; i++)
                offsets[i + 1] = offsets[i] + sizes[i];
            return offsets;
        }
    }
}