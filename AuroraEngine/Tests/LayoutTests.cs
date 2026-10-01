using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Testing;
using ArctisAurora.Core.UI;

namespace ArctisAurora.Tests
{
    internal static class LayoutTests
    {
        [A_XSDActionDependency("Layout.StackPanelArranges", "Test")]
        private static IEnumerator<int> StackPanelArranges(TestContext t)
        {
            StackPanelControl panel = new StackPanelControl
            {
                Spacing = 4f,
                horizontalAlignment = HorizontalAlignment.Left,
                verticalAlignment = VerticalAlignment.Top
            };
            float[] heights = { 20f, 30f, 40f };
            StackPanelControl[] children = new StackPanelControl[heights.Length];
            for (int i = 0; i < heights.Length; i++)
            {
                children[i] = new StackPanelControl { preferredWidth = 50f, preferredHeight = heights[i] };
                panel.AddChild(children[i]);
            }
            t.Show(panel);
            yield return 2;

            float top = panel.arrangedRect.y;
            t.Check(children[0].arrangedRect.y == top, "first child sits at the panel's top");
            t.Check(children[1].arrangedRect.y == top + 24f, "second child sits 20 + 4 below the first");
            t.Check(children[2].arrangedRect.y == top + 58f, "third child sits 30 + 4 below the second");
            for (int i = 0; i < heights.Length; i++)
                t.Check(children[i].arrangedRect.height == heights[i], $"child {i} keeps its {heights[i]} px height");
            t.Check(panel.DesiredSize.Y == 98f, "panel wants its children's heights plus two gaps");
        }

        [A_XSDActionDependency("Layout.StarChildCrossSize", "Test")]
        private static IEnumerator<int> StarChildCrossSize(TestContext t)
        {
            StackPanelControl bar = new StackPanelControl
            {
                orientation = StackPanelControl.Orientation.Horizontal,
                preferredHeight = 32f,
                horizontalAlignment = HorizontalAlignment.Stretch,
                verticalAlignment = VerticalAlignment.Top
            };
            bar.AddChild(new LabelControl { text = "Untitled", fontSize = 14, widthStar = 1f });
            bar.AddChild(new StackPanelControl { preferredWidth = 32f, preferredHeight = 32f });
            t.Show(bar);
            yield return 2;

            t.Check(bar.DesiredSize.Y == 32f, "an 8-character star label leaves a 32 px bar at 32 px");
            t.Check(bar.arrangedRect.height == 32f, "the bar is arranged 32 px tall");
        }

        [A_XSDActionDependency("Layout.GridAutoRowWrapsAtColumn", "Test")]
        private static IEnumerator<int> GridAutoRowWrapsAtColumn(TestContext t)
        {
            GridListControl grid = new GridListControl
            {
                horizontalAlignment = HorizontalAlignment.Left,
                verticalAlignment = VerticalAlignment.Top
            };
            grid.columnDefinitions.Add(new ColumnDefinition { sizeMode = GridSizeMode.Fixed, value = 100f });
            grid.columnDefinitions.Add(new ColumnDefinition { sizeMode = GridSizeMode.Fixed, value = 100f });
            grid.rowDefinitions.Add(new RowDefinition { sizeMode = GridSizeMode.Auto });

            TextRunControl wrapping = new TextRunControl
            {
                text = "The quick brown fox jumps over the lazy dog",
                gridRow = 0,
                gridColumn = 0
            };
            TextRunControl single = new TextRunControl { text = "x", gridRow = 0, gridColumn = 1 };
            grid.AddChild(wrapping);
            grid.AddChild(single);
            t.Show(grid);
            yield return 2;

            t.Check(wrapping.arrangedRect.width <= 100f, "the text is arranged inside its 100 px column");
            t.Check(wrapping.DesiredSize.Y > single.DesiredSize.Y * 1.5f, "the text wraps at its column's width");
            t.Check(grid.rowDefinitions[0].resolvedSize == wrapping.DesiredSize.Y, "the auto row is as tall as the wrapped text");
        }
    }
}
