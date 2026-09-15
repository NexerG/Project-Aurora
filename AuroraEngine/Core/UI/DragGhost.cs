using ArctisAurora.Core.Registry;
using ArctisAurora.EngineWork;
using ArctisAurora.EngineWork.Rendering;
using System.Numerics;
using Silk.NET.Vulkan;

namespace ArctisAurora.Core.UI
{
    // The window a dragged control is previewed in: one per process, built on the first drag and
    // hidden between them. Its module draws the dragged control where it already is.
    public static class DragGhost
    {
        private const string windowName = "drag-ghost";

        private static RenderWindow? _window;
        private static RenderWindow? _source;

        public static void Show(Control control)
        {
            DragGhostSetting setting = SettingsRegistry.Get<UISettings>().dragGhost;

            _source = UIEngine.WindowOf(control);
            if (_source == null) return;

            Extent2D size = PreviewSize(control);

            if (_window == null)
                _window = Engine.OpenGhostWindow(windowName, size.Width, size.Height);
            else
                _window.os.Resize(size.Width, size.Height);

            _window.ui.rangeRect = control.arrangedRect;
            _window.ui.rangeRoot = control;

            _window.os.SetOpacity(control.draggingOpacity >= 0 ? control.draggingOpacity : setting.opacity);
            Follow();
            _window.os.Show();
        }

        public static void Hide()
        {
            if (_window == null || _window.ui.rangeRoot == null) return;

            _window.os.Hide();
            _window.ui.rangeRoot = null;
            _window.ui.rangeRect = null;
            _source = null;
        }

        // The control at the size it is drawn on screen.
        private static Extent2D PreviewSize(Control control)
        {
            Extent2D window = _source!.os.windowSize;
            Vector2 viewport = _source.ui.uiRoot.ViewportSize(window);

            return new Extent2D(
                (uint)(control.arrangedRect.width * window.Width / viewport.X),
                (uint)(control.arrangedRect.height * window.Height / viewport.Y));
        }

        // Centres the preview on the pointer.
        public static unsafe void Follow()
        {
            if (_window == null || _window.ui.rangeRoot == null || _source == null) return;

            AGlfwWindow._glfw.GetWindowPos(_source.os.handle, out int sx, out int sy);
            _window.os.SetPosition(
                sx + (int)_source.mousePos.X - (int)(_window.os.windowSize.Width / 2),
                sy + (int)_source.mousePos.Y - (int)(_window.os.windowSize.Height / 2));
        }
    }
}
