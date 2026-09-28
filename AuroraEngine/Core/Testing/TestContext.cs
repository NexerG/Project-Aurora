using ArctisAurora.Core.Diagnostics;
using ArctisAurora.Core.UI;
using ArctisAurora.EngineWork;
using ArctisAurora.EngineWork.Rendering;
using System.Numerics;
using System.Runtime.CompilerServices;
using WindowHandle = Silk.NET.GLFW.WindowHandle;

namespace ArctisAurora.Core.Testing
{
    // What a test is handed: the fixture it shows, the checks it records, and the input it plays.
    public sealed unsafe class TestContext
    {
        internal readonly List<(string message, string file, int line)> failures = new();

        // scripted input, one step per tick
        private readonly Queue<Action> _steps = new();
        private RenderWindow? _window;

        // Shows content as the primary window's whole tree.
        public WindowRoot Show(Control content)
        {
            WindowRoot root = new WindowRoot();
            root.AddChild(content);
            Engine.primary.ui.uiRoot = root;
            return root;
        }

        // Records a failure when condition is false.
        public void Check(bool condition, string what,
            [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        {
            if (!condition) failures.Add((what, Path.GetFileName(file), line));
        }

        #region measure
        // the test's frame capture: where it goes, and whether it ran or why not
        internal string captureDirectory = string.Empty;
        internal bool measured;
        internal bool ending;
        internal bool closed;
        internal string? skipped;

        // Starts recording every frame into this test's capture folder.
        public void StartMeasure()
        {
            if (Engine.isDebug) skipped = "unoptimized JIT";
            else if (!Profiling.compiledIn) skipped = "profiler not compiled in";
            if (skipped != null) return;

            Profiling.CaptureInto(captureDirectory);
            measured = true;
        }

        // Stops recording; the runner holds the test until the capture's files are closed.
        public int EndMeasure()
        {
            if (!measured) return 1;

            Profiling.EndCapture();
            ending = true;
            return 1;
        }
        #endregion

        #region golden
        internal sealed class Shot
        {
            public string name = string.Empty;
            public Control? region;
            public string file = string.Empty;
            public int line;
            public string result = string.Empty;
            public string golden = string.Empty;
            public string? actual;
            public string? diff;
        }

        // the shots this test took, and the one being read back
        internal readonly List<Shot> shots = new();
        internal Shot? pendingShot;

        // Holds the clock and reads back the primary window, cropped to region; the runner compares it with the golden.
        public int Golden(string shot, Control? region = null,
            [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        {
            Engine.clockHeld = true;
            ScreenReadback.Request(Engine.primary);
            pendingShot = new Shot { name = shot, region = region, file = Path.GetFileName(file), line = line };
            return 1;
        }
        #endregion

        #region input
        // Runs the next queued input step; the runner calls it once a tick.
        internal void RunStep()
        {
            if (_steps.Count > 0) _steps.Dequeue()();
        }

        // Moves the pointer to the centre of control.
        public int MoveTo(Control control,
            [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        {
            _steps.Enqueue(() => Point(control, file, line));
            return Queued();
        }

        // Moves the pointer to a design-space point in window.
        public int MoveTo(RenderWindow window, Vector2 point)
        {
            _steps.Enqueue(() => Point(window, point));
            return Queued();
        }

        public int Click(Control control, Keys button = Keys.MouseLeft,
            [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        {
            _steps.Enqueue(() => Point(control, file, line));
            _steps.Enqueue(() => Raw(button, RawAction.Down));
            _steps.Enqueue(() => Raw(button, RawAction.Up));
            return Queued();
        }

        // Presses on control, moves to a design-space point in its window over steps ticks, and releases.
        public int Drag(Control control, Vector2 to, int steps = 8,
            [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        {
            Vector2 from = Vector2.Zero;
            _steps.Enqueue(() => from = Point(control, file, line));
            _steps.Enqueue(() => Raw(Keys.MouseLeft, RawAction.Down));
            for (int i = 1; i <= steps; i++)
            {
                float f = (float)i / steps;
                _steps.Enqueue(() => { if (_window != null) Point(_window, Vector2.Lerp(from, to, f)); });
            }
            _steps.Enqueue(() => Raw(Keys.MouseLeft, RawAction.Up));
            return Queued();
        }

        // Presses key with modifiers held.
        public int Key(Keys key, params Keys[] modifiers)
        {
            if (modifiers.Length > 0)
                _steps.Enqueue(() => { foreach (Keys m in modifiers) Raw(m, RawAction.Down); });
            _steps.Enqueue(() => Raw(key, RawAction.Down));
            _steps.Enqueue(() => Raw(key, RawAction.Up));
            if (modifiers.Length > 0)
                _steps.Enqueue(() => { foreach (Keys m in modifiers) Raw(m, RawAction.Up); });
            return Queued();
        }

        // Types text a character at a time, each as its key going down with the character and then up.
        public int Type(string text)
        {
            foreach (char c in text)
            {
                Keys key = KeyFor(c);
                _steps.Enqueue(() =>
                {
                    Raw(key, RawAction.Down);
                    InputHandler.instance.ProcessCharInput(Handle(), c);
                });
                _steps.Enqueue(() => Raw(key, RawAction.Up));
            }
            return Queued();
        }

        // ticks until everything queued has been handled
        private int Queued() => _steps.Count + 1;

        private Vector2 Point(Control control, string file, int line)
        {
            RenderWindow? window = UIEngine.WindowOf(control);
            if (window == null)
            {
                failures.Add(("the control is in no window", Path.GetFileName(file), line));
                return Vector2.Zero;
            }

            LayoutRect r = control.arrangedRect;
            Vector2 centre = new Vector2(r.x + r.width * 0.5f, r.y + r.height * 0.5f);

            Control? hit = UIEngine.HitTest(window.ui.uiRoot, centre);
            Control? walk = hit;
            while (walk != null && !ReferenceEquals(walk, control)) walk = walk.parent as Control;
            if (walk == null)
                failures.Add(($"the pointer at the control's centre hits {hit?.GetType().Name ?? "nothing"}, not the control",
                    Path.GetFileName(file), line));

            Point(window, centre);
            return centre;
        }

        private void Point(RenderWindow window, Vector2 point)
        {
            _window = window;
            foreach (RenderWindow other in Engine.windows.Values)
                other.isInWindow = ReferenceEquals(other, window);

            Vector2 pixels = window.ui.uiRoot.ToWindowSpace(point, window.os.windowSize);
            InputHandler.instance.ProcessMouseMove(window.os.handle, pixels.X, pixels.Y);
        }

        private static void Raw(Keys key, RawAction action) =>
            InputHandler.instance.keyTracker.EnqueueEvent(key, action, Engine.totalTime);

        private WindowHandle* Handle() => (_window ?? Engine.primary).os.handle;

        private static Keys KeyFor(char c)
        {
            char upper = char.ToUpperInvariant(c);
            if (upper >= 'A' && upper <= 'Z') return Keys.A + (upper - 'A');
            if (c >= '0' && c <= '9') return Keys.Num0 + (c - '0');
            if (c == ' ') return Keys.Space;
            return Keys.AnySymbol;
        }
        #endregion
    }
}
