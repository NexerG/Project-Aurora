using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Testing;
using ArctisAurora.Core.UI;

namespace Thorium.Tests
{
    internal static class TextInputTests
    {
        [A_XSDActionDependency("TextInput.TypeWritesText", "Test")]
        private static IEnumerator<int> TypeWritesText(TestContext t)
        {
            TextBoxControl box = new TextBoxControl
            {
                preferredWidth = 240f,
                preferredHeight = 28f,
                horizontalAlignment = HorizontalAlignment.Left,
                verticalAlignment = VerticalAlignment.Top
            };
            t.Show(box);
            yield return 2;

            yield return t.Click(box);
            t.Check(ReferenceEquals(UIEngine.activeControl, box), "a click focuses the text box");

            yield return t.Type("Hi 5!");
            t.Check(box.text == "Hi 5!", "typing writes letters, digits, a space and a symbol");
        }
    }
}
