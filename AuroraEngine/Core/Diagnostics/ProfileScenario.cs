using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.UI;
using ArctisAurora.EngineWork;
using ArctisAurora.EngineWork.Rendering;
using System.Text;

namespace ArctisAurora.Core.Diagnostics
{
    // --profile-scenario: types into, then resizes around, a 1,000,000-character note under one capture.
    public unsafe class ProfileScenario : Entity
    {
        private static readonly LogChannel Log = LogChannel.For("Profiling");

        // timeline, in main ticks
        private const int openTick = 2;
        private const int settleTicks = 30;
        private const int typeTicks = 120;
        private const int resizeTicks = 120;
        private const int resizeStep = 8;

        // document shape
        private const int blockCount = 1000;
        private const int blockChars = 1000;

        private RichTextDocument document = null!;
        private int tick;
        private int startWidth;
        private int startHeight;

        public static void Arm()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--profile-scenario") < 0) return;

            string? hostRoot = SettingsRegistry.WriteRoot;
            if (hostRoot != null)
                SettingsRegistry.SetWriteRoot(Path.Combine(Directory.GetParent(hostRoot)!.FullName, "ProfileScenario"));
            Engine.Post(() => new ProfileScenario());
        }

        // Replaces the primary window's tree with an editor holding the generated note.
        private void Open()
        {
            StringBuilder builder = new StringBuilder(blockChars);
            while (builder.Length < blockChars)
                builder.Append("The quick brown fox jumps over the lazy dog while every line of the note wraps again. ");
            string paragraph = builder.ToString(0, blockChars);

            document = new RichTextDocument();
            for (int i = 0; i < blockCount; i++)
            {
                BlockControl block = new BlockControl();
                block.AppendRun(new Run { text = paragraph });
                document.blocks.Add(block);
            }

            DocumentEditorControl editor = new DocumentEditorControl
            {
                horizontalAlignment = HorizontalAlignment.Stretch,
                verticalAlignment = VerticalAlignment.Stretch
            };
            WindowRoot root = new WindowRoot();
            root.AddChild(editor);
            Engine.primary.ui.uiRoot = root;
            editor.LoadDocument(document);

            BlockControl first = document.blocks[0];
            ((DocumentControl)first.parent).SetCaret(first, first.Length);
            editor.FocusCaret();

            AGlfwWindow._glfw.GetWindowSize(Engine.primary.os.handle, out startWidth, out startHeight);
            Log.Info($"scenario — {document.blocks.Sum(b => b.Length)} chars in {blockCount} blocks, window {startWidth}x{startHeight}");
        }

        public override void OnTick()
        {
            base.OnTick();
            tick++;

            if (tick == openTick)
            {
                Open();
                return;
            }

            if (tick == settleTicks)
            {
                Profiling.Capture(typeTicks + resizeTicks);
                return;
            }

            int step = tick - settleTicks;
            if (step <= 0) return;

            if (step <= typeTicks)
            {
                Profiling.Zone.Start("Scenario.Type");
                InputHandler.charInputReadQueue.Enqueue((char)('a' + step % 26));
                TextInputActions.Write();
                Profiling.Zone.End("Scenario.Type");
            }
            else if (step <= typeTicks + resizeTicks)
            {
                Profiling.Zone.Start("Scenario.Resize");
                int r = step - typeTicks;
                int narrowed = r <= resizeTicks / 2 ? r : resizeTicks - r;
                AGlfwWindow._glfw.SetWindowSize(Engine.primary.os.handle, startWidth - narrowed * resizeStep, startHeight);
                Profiling.Zone.End("Scenario.Resize");
            }
            else if (step == typeTicks + resizeTicks + 1)
            {
                AGlfwWindow._glfw.GetWindowSize(Engine.primary.os.handle, out int width, out int height);
                Log.Info($"scenario done — {document.blocks.Sum(b => b.Length)} chars, window {width}x{height}");
                Engine.Post(Shutdown.Request);
            }
        }
    }
}
