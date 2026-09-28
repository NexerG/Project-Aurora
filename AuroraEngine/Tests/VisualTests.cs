using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Testing;
using ArctisAurora.Core.UI;

namespace ArctisAurora.Tests
{
    internal static class VisualTests
    {
        [A_XSDActionDependency("Visual.PanelAndLabel", "Test")]
        private static IEnumerator<int> PanelAndLabel(TestContext t)
        {
            StackPanelControl panel = new StackPanelControl
            {
                preferredWidth = 200f,
                preferredHeight = 80f,
                colorHex = "#2E5C8A",
                cornerRadius = new CornerRadii(8f),
                horizontalAlignment = HorizontalAlignment.Left,
                verticalAlignment = VerticalAlignment.Top
            };
            panel.AddChild(new LabelControl { text = "Golden", fontSize = 16, colorHex = "#FFFFFF" });
            t.Show(panel);
            yield return 2;

            yield return t.Golden("Default", panel);
        }
    }
}
