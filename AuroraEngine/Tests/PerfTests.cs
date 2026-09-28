using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Testing;
using ArctisAurora.Core.UI;

namespace ArctisAurora.Tests
{
    internal static class PerfTests
    {
        // column shape and timeline, in ticks
        private const int labelCount = 1000;
        private const int warmupTicks = 30;
        private const int measuredTicks = 120;

        [A_XSDActionDependency("Perf.RelayoutLabels", "Test")]
        private static IEnumerator<int> RelayoutLabels(TestContext t)
        {
            StackPanelControl column = new StackPanelControl
            {
                preferredWidth = 400f,
                horizontalAlignment = HorizontalAlignment.Left,
                verticalAlignment = VerticalAlignment.Top
            };
            for (int i = 0; i < labelCount; i++)
                column.AddChild(new LabelControl { text = $"Label {i}", preferredHeight = 16f });
            t.Show(column);
            yield return 2;

            for (int i = 0; i < warmupTicks; i++)
            {
                column.preferredWidth = 400f + i % 2;
                yield return 1;
            }

            t.StartMeasure();
            for (int i = 0; i < measuredTicks; i++)
            {
                column.preferredWidth = 400f + i % 2;
                yield return 1;
            }
            yield return t.EndMeasure();
        }
    }
}
