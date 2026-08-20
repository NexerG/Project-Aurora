using ArctisAurora.Core.UISystem.Controls;
using ArctisAurora.EngineWork;
using ArctisAurora.EngineWork.Rendering;
using Silk.NET.Maths;
using Silk.NET.Vulkan;

namespace ArctisAurora.Core.UISystem
{
    // The window an open context menu lives in. One per process, built on the first right click and
    // hidden between them — a swapchain per open would put the build cost inside the gesture.
    //
    // Unlike the drag preview it owns a tree and is clicked, so it is an ordinary window everywhere
    // else: it wires input, hit-tests, and its entries are ordinary buttons.
    public static class ContextMenuWindow
    {
        private const string windowName = "context-menu";

        // the size the window is built at, before the first menu is measured into it
        private const uint seedWidth = 160;
        private const uint seedHeight = 120;

        private static RenderWindow _window;
        private static RenderWindow _source;
        private static ContextMenuControl _menu;

        public static bool isOpen { get; private set; }

        // False means this control offered nothing, which is what tells the walk to keep going up.
        public static unsafe bool Open(VulkanControl owner)
        {
            // right-clicking the menu itself is not a request for another one, and there is nothing
            // above it worth walking to either
            if (_window != null && ReferenceEquals(RenderWindow.Of(owner), _window)) return true;

            List<ContextEntry> entries = ContextMenus.Compose(owner);
            if (entries.Count == 0) return false;

            RenderWindow source = RenderWindow.Of(owner);
            if (source == null) return false;

            Build();
            _source = source;
            _menu.Fill(entries);

            Extent2D size = MenuSize();
            _window.os.Resize(size.Width, size.Height);
            _window.ui.uiRoot.FitTo(size);

            AGlfwWindow._glfw.GetWindowPos(source.os.handle, out int sx, out int sy);
            _window.os.SetPosition(sx + (int)source.mousePos.X, sy + (int)source.mousePos.Y);

            _window.os.Show();
            _window.os.Focus();

            // the menu opens under the pointer, so no crossing happens and no enter callback fires
            _window.os.SeedIsInWindow();
            isOpen = true;
            return true;
        }

        public static void Close()
        {
            if (!isOpen) return;

            _window.os.Hide();
            isOpen = false;

            _source?.os.Focus();
            _source = null;
        }

        // Dismissal is the pointer leaving; isInWindow is kept current by the crossing callback every
        // window wires.
        public static void Tick()
        {
            if (isOpen && !_window.isInWindow) Close();
        }

        private static void Build()
        {
            if (_window != null) return;

            _window = Engine.OpenMenuWindow(windowName, seedWidth, seedHeight);
            _menu = new ContextMenuControl { onEntryInvoked = Close };

            WindowControl root = new WindowControl();
            root.AddChild(_menu);
            _window.ui.uiRoot = root;
        }

        // The menu measures to its widest caption, so the window is sized to it and never the other
        // way round.
        private static Extent2D MenuSize()
        {
            _menu.Measure(new Vector2D<float>(float.MaxValue, float.MaxValue));
            Vector2D<float> desired = _menu.DesiredSize;
            return new Extent2D((uint)MathF.Ceiling(desired.X), (uint)MathF.Ceiling(desired.Y));
        }
    }
}
