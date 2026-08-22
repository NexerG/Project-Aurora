using ArctisAurora.EngineWork;
using ArctisAurora.EngineWork.Rendering;
using Silk.NET.Maths;
using Silk.NET.Vulkan;

namespace ArctisAurora.Core.UISystem.Controls
{
    // The same menu in a window of its own, which is what lets it spill past the edges of the window
    // it was opened from. One per process, built on the first open and hidden between them — a
    // swapchain per open would put the build cost inside the gesture.
    //
    // Unlike the drag preview it owns a tree and is clicked, so its window is an ordinary one
    // everywhere else: it wires input, hit-tests, and its entries are ordinary buttons.
    public class WindowedContextMenuControl : ContextMenuControl
    {
        private const string windowName = "context-menu";

        // the size the window is built at, before the first menu is measured into it
        private const uint seedWidth = 160;
        private const uint seedHeight = 120;

        private RenderWindow _window = null!;
        private RenderWindow? _source;

        // Dismissal is the pointer leaving; isInWindow is kept current by the crossing callback every
        // window wires.
        public override void Tick()
        {
            if (isOpen && !_window.isInWindow) Close();
        }

        // The menu measures to its widest caption, so the window is sized to it and never the other
        // way round.
        protected override unsafe void Attach(RenderWindow source, Vector2D<float> point)
        {
            Build();
            _source = source;

            Extent2D size = new Extent2D((uint)MathF.Ceiling(DesiredSize.X), (uint)MathF.Ceiling(DesiredSize.Y));
            _window.os.Resize(size.Width, size.Height);
            _window.ui.uiRoot.FitTo(size);

            AGlfwWindow._glfw.GetWindowPos(source.os.handle, out int sx, out int sy);
            _window.os.SetPosition(sx + (int)source.mousePos.X, sy + (int)source.mousePos.Y);

            _window.os.Show();
            _window.os.Focus();

            // the menu opens under the pointer, so no crossing happens and no enter callback fires
            _window.os.SeedIsInWindow();
        }

        // The window is kept and so is the tree in it — hiding is the whole teardown.
        protected override void Detach()
        {
            _window.os.Hide();

            _source?.os.Focus();
            _source = null;
        }

        private void Build()
        {
            if (_window != null) return;

            _window = Engine.OpenMenuWindow(windowName, seedWidth, seedHeight);

            WindowControl root = new WindowControl();
            root.AddChild(this);
            _window.ui.uiRoot = root;
        }
    }
}
