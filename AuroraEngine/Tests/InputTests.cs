using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Testing;
using ArctisAurora.Core.UI;
using ArctisAurora.EngineWork;
using System.Numerics;

namespace ArctisAurora.Tests
{
    internal static class InputTests
    {
        [A_XSDActionDependency("Input.ClickFiresRelease", "Test")]
        private static IEnumerator<int> ClickFiresRelease(TestContext t)
        {
            int released = 0;
            ButtonControl button = new ButtonControl
            {
                preferredWidth = 120f,
                preferredHeight = 40f,
                horizontalAlignment = HorizontalAlignment.Left,
                verticalAlignment = VerticalAlignment.Top
            };
            button.RegisterOnRelease(_ => { released++; return true; });
            t.Show(button);
            yield return 2;

            yield return t.Click(button);
            t.Check(released == 1, "one click fires onRelease once");
        }

        [A_XSDActionDependency("Input.KeyWithModifierFiresKeybind", "Test")]
        private static IEnumerator<int> KeyWithModifierFiresKeybind(TestContext t)
        {
            t.Show(new StackPanelControl());
            yield return 2;

            yield return t.Key(Keys.GraveAccent, Keys.LeftControl);
            ConsoleControl? console = ConsoleControl.current;
            t.Check(console != null && console.parent != null, "Ctrl+` fires Console.Toggle and the console opens");

            if (console != null && console.parent != null) ConsoleControl.Toggle();
        }

        [A_XSDActionDependency("Input.DragMovesSplitter", "Test")]
        private static IEnumerator<int> DragMovesSplitter(TestContext t)
        {
            StackPanelControl row = new StackPanelControl { orientation = StackPanelControl.Orientation.Horizontal };
            StackPanelControl left = new StackPanelControl { preferredWidth = 200f };
            SplitterControl splitter = new SplitterControl { preferredWidth = 6f };
            row.AddChild(left);
            row.AddChild(splitter);
            row.AddChild(new StackPanelControl { widthStar = 1f });
            t.Show(row);
            yield return 2;

            LayoutRect grip = splitter.arrangedRect;
            Vector2 to = new Vector2(grip.x + grip.width * 0.5f + 50f, grip.y + grip.height * 0.5f);
            yield return t.Drag(splitter, to);
            yield return 2;

            t.Check(left.preferredWidth == 250f, "dragging the splitter 50 right widens the pane before it to 250");
            t.Check(left.arrangedRect.width == 250f, "and the pane is arranged 250 wide");
        }
    }
}
