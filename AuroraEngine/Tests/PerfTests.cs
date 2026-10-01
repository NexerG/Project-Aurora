using ArctisAurora.Core.Animation;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Testing;
using ArctisAurora.Core.UI;
using ArctisAurora.EngineWork;
using System.Numerics;
using System.Text;

namespace ArctisAurora.Tests
{
    internal static class PerfTests
    {
        // column shape and timeline, in ticks
        private const int labelCount = 1000;
        private const int warmupTicks = 30;
        private const int measuredTicks = 120;

        // note shape and rewrap step
        private const int blockCount = 1000;
        private const int blockChars = 1000;
        private const float noteWidth = 800f;
        private const float rewrapStep = 8f;

        // button grid shape
        private const int buttonCount = 5000;
        private const int rowLength = 100;
        private const float buttonSize = 16f;

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

        [A_XSDActionDependency("Perf.TypeLargeNote", "Test")]
        private static IEnumerator<int> TypeLargeNote(TestContext t)
        {
            OpenNote(t);
            yield return 2;

            for (int i = 0; i < warmupTicks; i++)
            {
                TypeOne(i);
                yield return 1;
            }

            t.StartMeasure();
            for (int i = 0; i < measuredTicks; i++)
            {
                TypeOne(i);
                yield return 1;
            }
            yield return t.EndMeasure();
            t.Show(new StackPanelControl());
        }

        [A_XSDActionDependency("Perf.RewrapLargeNote", "Test")]
        private static IEnumerator<int> RewrapLargeNote(TestContext t)
        {
            DocumentEditorControl editor = OpenNote(t);
            yield return 2;

            for (int i = 0; i < warmupTicks; i++)
            {
                editor.preferredWidth = noteWidth - i % 2 * rewrapStep;
                yield return 1;
            }

            t.StartMeasure();
            for (int i = 1; i <= measuredTicks; i++)
            {
                int narrowed = i <= measuredTicks / 2 ? i : measuredTicks - i;
                editor.preferredWidth = noteWidth - narrowed * rewrapStep;
                yield return 1;
            }
            yield return t.EndMeasure();
            t.Show(new StackPanelControl());
        }

        [A_XSDActionDependency("Perf.AnimationBurst", "Test")]
        private static IEnumerator<int> AnimationBurst(TestContext t)
        {
            List<ButtonControl> buttons = ShowGrid(t);
            yield return 2;

            foreach (ButtonControl button in buttons)
                Animations.Tween(button, "state", new Vector4(2f, 0f, 0f, 0f), warmupTicks / 60f, Curve.Ease(EaseKind.CubicInOut));
            yield return warmupTicks;

            t.StartMeasure();
            foreach (ButtonControl button in buttons)
                Animations.Tween(button, "state", Vector4.Zero, measuredTicks / 60f, Curve.Ease(EaseKind.CubicInOut));
            yield return measuredTicks;
            yield return t.EndMeasure();
            StopAll(buttons);
        }

        [A_XSDActionDependency("Perf.AnimationLayoutClip", "Test")]
        private static IEnumerator<int> AnimationLayoutClip(TestContext t)
        {
            List<ButtonControl> buttons = ShowGrid(t);
            yield return 2;

            foreach (ButtonControl button in buttons)
                Animations.Play(button, "profile-margin", false);
            yield return warmupTicks;

            t.StartMeasure();
            yield return measuredTicks;
            yield return t.EndMeasure();
            StopAll(buttons);
        }

        // Shows an editor holding a generated note, caret at the end of the first block.
        private static DocumentEditorControl OpenNote(TestContext t)
        {
            StringBuilder builder = new StringBuilder(blockChars);
            while (builder.Length < blockChars)
                builder.Append("The quick brown fox jumps over the lazy dog while every line of the note wraps again. ");
            string paragraph = builder.ToString(0, blockChars);

            RichTextDocument document = new RichTextDocument();
            for (int i = 0; i < blockCount; i++)
            {
                BlockControl block = new BlockControl();
                block.AppendRun(new Run { text = paragraph });
                document.blocks.Add(block);
            }

            DocumentEditorControl editor = new DocumentEditorControl
            {
                preferredWidth = noteWidth,
                horizontalAlignment = HorizontalAlignment.Left,
                verticalAlignment = VerticalAlignment.Stretch
            };
            t.Show(editor);
            editor.LoadDocument(document);

            BlockControl first = (BlockControl)document.blocks[0];
            ((DocumentControl)first.parent).SetCaret(first, first.Length);
            editor.FocusCaret();
            return editor;
        }

        private static void TypeOne(int i)
        {
            InputHandler.charInputReadQueue.Enqueue((char)('a' + i % 26));
            TextInputActions.Write();
        }

        // Shows buttonCount buttons in rows of rowLength.
        private static List<ButtonControl> ShowGrid(TestContext t)
        {
            List<ButtonControl> buttons = new List<ButtonControl>(buttonCount);
            StackPanelControl rows = new StackPanelControl();
            StackPanelControl row = null!;
            for (int i = 0; i < buttonCount; i++)
            {
                if (i % rowLength == 0)
                {
                    row = new StackPanelControl { orientation = StackPanelControl.Orientation.Horizontal };
                    rows.AddChild(row);
                }
                ButtonControl button = new ButtonControl { preferredWidth = buttonSize, preferredHeight = buttonSize };
                row.AddChild(button);
                buttons.Add(button);
            }
            t.Show(rows);
            return buttons;
        }

        private static void StopAll(List<ButtonControl> buttons)
        {
            foreach (ButtonControl button in buttons)
                Animations.StopAll(button);
        }
    }
}
