using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.Registry;
using Silk.NET.Maths;

namespace ArctisAurora.Core.UI
{
    [A_XSDType("NextGridSizeMode", "UI")]
    public enum NextGridSizeMode { Fixed, Auto, Star }

    [A_XSDType("NextRowDefinition", "UI")]
    public class NextRowDefinition
    {
        [A_XSDElementProperty("Height", "UI", "Pixel size for Fixed, weight for Star, ignored for Auto.")]
        public float value = 1f;

        [A_XSDElementProperty("SizeMode", "UI", "Fixed | Auto | Star")]
        public NextGridSizeMode sizeMode = NextGridSizeMode.Auto;

        [A_XSDElementProperty("GapAfter", "UI", "Pixels of space after this row. Ignored on the last row.")]
        public float gapAfter = 0f;

        public float resolvedSize;
    }

    [A_XSDType("NextColumnDefinition", "UI")]
    public class NextColumnDefinition
    {
        [A_XSDElementProperty("Width", "UI", "Pixel size for Fixed, weight for Star, ignored for Auto.")]
        public float value = 1f;

        [A_XSDElementProperty("SizeMode", "UI", "Fixed | Auto | Star")]
        public NextGridSizeMode sizeMode = NextGridSizeMode.Auto;

        [A_XSDElementProperty("GapAfter", "UI", "Pixels of space after this column. Ignored on the last column.")]
        public float gapAfter = 0f;

        public float resolvedSize;
    }

    [A_XSDType("NextGridCell", "UI")]
    public class NextGridCellAssignment
    {
        [A_XSDElementProperty("Row", "UI")]
        public int row = 0;
        [A_XSDElementProperty("Column", "UI")]
        public int column = 0;
        [A_XSDElementProperty("RowSpan", "UI")]
        public int rowSpan = 1;
        [A_XSDElementProperty("ColumnSpan", "UI")]
        public int columnSpan = 1;

        public Control? child;
    }

    // Lays children into a band grid, each cell claimed by a child's Grid.Row and Grid.Column.
    [A_XSDType("NextGridList", "UI")]
    public class NextGridListControl : ContainerControl
    {
        [A_XSDElementProperty("RowDefinition", "UI", "")]
        public List<NextRowDefinition> rowDefinitions = new List<NextRowDefinition>();
        [A_XSDElementProperty("ColumnDefinition", "UI", "")]
        public List<NextColumnDefinition> columnDefinitions = new List<NextColumnDefinition>();

        private readonly List<NextGridCellAssignment> _cellAssignments = new List<NextGridCellAssignment>();

        public override void AddChild(Entity entity)
        {
            if (entity is not Control control)
                throw new Exception("Child entity must be a Control");

            foreach (NextGridCellAssignment cell in _cellAssignments)
                if (cell.column == control.gridColumn && cell.row == control.gridRow)
                    throw new Exception("One cell - one control.");

            children.Add(entity);
            control.parent = this;
            MarkTreeOrderDirty();

            _cellAssignments.Add(new NextGridCellAssignment
            {
                row = control.gridRow,
                column = control.gridColumn,
                child = control
            });

            InvalidateLayout();
        }

        private void EnsureDefaults()
        {
            if (rowDefinitions.Count == 0)
                rowDefinitions.Add(new NextRowDefinition { sizeMode = NextGridSizeMode.Star, value = 1 });
            if (columnDefinitions.Count == 0)
                columnDefinitions.Add(new NextColumnDefinition { sizeMode = NextGridSizeMode.Star, value = 1 });
        }

        public override Vector2D<float> Measure(Vector2D<float> availableSize)
        {
            EnsureDefaults();

            // A pinned axis is the box the bands divide, not the offer that came in — a stack offers
            // float.MaxValue on its main axis, and a star band would take all of it.
            float boxWidth = preferredWidth > 0 ? preferredWidth : availableSize.X;
            float boxHeight = preferredHeight > 0 ? preferredHeight : availableSize.Y;

            LayoutRect inner = new LayoutRect(0, 0, boxWidth, boxHeight).Shrink(padding);

            int rows = rowDefinitions.Count;
            int cols = columnDefinitions.Count;

            // Pass 1 — Fixed bands.
            for (int r = 0; r < rows; r++)
                rowDefinitions[r].resolvedSize = rowDefinitions[r].sizeMode == NextGridSizeMode.Fixed
                    ? rowDefinitions[r].value : 0;
            for (int c = 0; c < cols; c++)
                columnDefinitions[c].resolvedSize = columnDefinitions[c].sizeMode == NextGridSizeMode.Fixed
                    ? columnDefinitions[c].value : 0;

            // Pass 2 — Auto bands take the largest child living entirely in them.
            foreach (NextGridCellAssignment assignment in _cellAssignments)
            {
                if (assignment.child == null) continue;
                Control child = assignment.child;

                Vector2D<float> childDesired = child.Measure(inner.size);

                if (assignment.rowSpan == 1 && rowDefinitions[assignment.row].sizeMode == NextGridSizeMode.Auto)
                    rowDefinitions[assignment.row].resolvedSize = MathF.Max(
                        rowDefinitions[assignment.row].resolvedSize,
                        childDesired.Y + child.margin.totalVertical);

                if (assignment.columnSpan == 1 && columnDefinitions[assignment.column].sizeMode == NextGridSizeMode.Auto)
                    columnDefinitions[assignment.column].resolvedSize = MathF.Max(
                        columnDefinitions[assignment.column].resolvedSize,
                        childDesired.X + child.margin.totalHorizontal);
            }

            // Pass 3 — what is left goes to the Star bands, by weight.
            float totalRowGaps = rowDefinitions.Take(rows - 1).Sum(r => r.gapAfter);
            float totalColGaps = columnDefinitions.Take(cols - 1).Sum(c => c.gapAfter);
            float fixedAndAutoH = rowDefinitions.Sum(r => r.resolvedSize);
            float fixedAndAutoW = columnDefinitions.Sum(c => c.resolvedSize);
            float starH = MathF.Max(0, inner.height - fixedAndAutoH - totalRowGaps);
            float starW = MathF.Max(0, inner.width - fixedAndAutoW - totalColGaps);

            float totalRowStars = rowDefinitions.Where(r => r.sizeMode == NextGridSizeMode.Star).Sum(r => r.value);
            float totalColStars = columnDefinitions.Where(c => c.sizeMode == NextGridSizeMode.Star).Sum(c => c.value);

            for (int r = 0; r < rows; r++)
                if (rowDefinitions[r].sizeMode == NextGridSizeMode.Star)
                    rowDefinitions[r].resolvedSize = totalRowStars > 0
                        ? starH * (rowDefinitions[r].value / totalRowStars) : 0;

            for (int c = 0; c < cols; c++)
                if (columnDefinitions[c].sizeMode == NextGridSizeMode.Star)
                    columnDefinitions[c].resolvedSize = totalColStars > 0
                        ? starW * (columnDefinitions[c].value / totalColStars) : 0;

            float totalW = columnDefinitions.Sum(c => c.resolvedSize) + totalColGaps + padding.totalHorizontal;
            float totalH = rowDefinitions.Sum(r => r.resolvedSize) + totalRowGaps + padding.totalVertical;

            if (preferredWidth > 0) totalW = MathF.Max(totalW, preferredWidth);
            if (preferredHeight > 0) totalH = MathF.Max(totalH, preferredHeight);

            arrange.desired = new Vector2D<float>(totalW, totalH);
            SetFlag(ArrangeFlags.MeasureDirty, false);
            return arrange.desired;
        }

        public override void Arrange(LayoutRect finalRect)
        {
            EnsureDefaults();
            WriteArranged(finalRect);

            LayoutRect inner = finalRect.Shrink(padding);

            float[] rowOffsets = BuildOffsets(
                rowDefinitions.Select(r => r.resolvedSize).ToArray(),
                rowDefinitions.Select(r => r.gapAfter).ToArray(),
                inner.y);

            float[] colOffsets = BuildOffsets(
                columnDefinitions.Select(c => c.resolvedSize).ToArray(),
                columnDefinitions.Select(c => c.gapAfter).ToArray(),
                inner.x);

            foreach (NextGridCellAssignment assignment in _cellAssignments)
            {
                if (assignment.child == null) continue;
                Control child = assignment.child;

                int r = Math.Clamp(assignment.row, 0, rowDefinitions.Count - 1);
                int c = Math.Clamp(assignment.column, 0, columnDefinitions.Count - 1);
                int rEnd = Math.Clamp(r + assignment.rowSpan, 1, rowDefinitions.Count);
                int cEnd = Math.Clamp(c + assignment.columnSpan, 1, columnDefinitions.Count);

                float cellX = colOffsets[c];
                float cellY = rowOffsets[r];
                LayoutRect cellRect = new LayoutRect(cellX, cellY,
                    colOffsets[cEnd] - cellX, rowOffsets[rEnd] - cellY).Shrink(child.margin);

                float childW = (child.preferredWidth == 0 || child.horizontalAlignment == HorizontalAlignment.Stretch)
                    ? cellRect.width : MathF.Min(child.DesiredSize.X, cellRect.width);
                float childH = (child.preferredHeight == 0 || child.verticalAlignment == VerticalAlignment.Stretch)
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

            SetFlag(ArrangeFlags.ArrangeDirty, false);
        }

        // Band edges, one more than there are bands, so a span reads its end straight off.
        private static float[] BuildOffsets(float[] sizes, float[] gaps, float start)
        {
            float[] offsets = new float[sizes.Length + 1];
            offsets[0] = start;
            for (int i = 0; i < sizes.Length; i++)
            {
                float gap = i < sizes.Length - 1 ? gaps[i] : 0f;
                offsets[i + 1] = offsets[i] + sizes[i] + gap;
            }
            return offsets;
        }
    }
}
