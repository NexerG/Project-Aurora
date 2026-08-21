using ArctisAurora.Core.Registry;
using ArctisAurora.Core.UISystem.Controls;
using ArctisAurora.EngineWork;
using ArctisAurora.EngineWork.Rendering;
using Silk.NET.Maths;
using Silk.NET.Vulkan;

namespace ArctisAurora.Core.UISystem
{
    // The window a dragged control is previewed in. One per process, built on the first drag and
    // hidden between them — a swapchain per gesture would put the build cost inside the drag.
    //
    // It holds no tree. Its module is pointed at the control being dragged, which stays where it is
    // in its own window's tree, so this is a second view of the same pool rows rather than a copy.
    public static class DragGhost
    {
        private const string windowName = "drag-ghost";

        private static RenderWindow _window = null!;
        private static RenderWindow? _source;

        public static void Show(VulkanControl control)
        {
            DragGhostSetting setting = SettingsRegistry.Get<UISettings>().dragGhost;

            _source = RenderWindow.Of(control);
            if (_source == null) return;

            Extent2D size = PreviewSize(control);

            if (_window == null)
                _window = Engine.OpenGhostWindow(windowName, size.Width, size.Height);
            else
                _window.os.Resize(size.Width, size.Height);

            _window.ui.rangeRoot = control;
            UILayout.InvalidateWindowRanges();

            _window.os.SetOpacity(control.draggingOpacity >= 0 ? control.draggingOpacity : setting.opacity);
            Follow();
            _window.os.Show();
        }

        public static void Hide()
        {
            if (_window == null || _window.ui.rangeRoot == null) return;

            _window.os.Hide();
            _window.ui.rangeRoot = null;
            _source = null;
            UILayout.InvalidateWindowRanges();
        }

        // The control at the size it is drawn on screen.
        private static Extent2D PreviewSize(VulkanControl control)
        {
            Extent2D window = _source.os.windowSize;
            Vector2D<float> viewport = _source.ui.uiRoot.ViewportSize(window);

            return new Extent2D(
                (uint)(control.arrangedRect.width * window.Width / viewport.X),
                (uint)(control.arrangedRect.height * window.Height / viewport.Y));
        }

        // Centres the preview on the pointer. The drag's own window has the capture, so its reported
        // position stays right however far outside itself the pointer has gone.
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
