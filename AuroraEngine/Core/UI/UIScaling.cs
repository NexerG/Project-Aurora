using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Testing;
using ArctisAurora.EngineWork;
using ArctisAurora.EngineWork.Rendering;

namespace ArctisAurora.Core.UI
{
    // How many window pixels a design unit takes: the monitor's display scale times the user's zoom.
    public static class UIScaling
    {
        // zoom range and step, percent
        private const float minZoom = 50f;
        private const float maxZoom = 300f;
        private const float zoomStep = 10f;

        public static float Zoom => Math.Clamp(SettingsRegistry.Get<UISettings>().zoom.percent, minZoom, maxZoom) / 100f;

        public static float For(RenderWindow window) => (TestRunner.active ? 1f : window.os.contentScale) * Zoom;

        // A design size in the pixels a window opened from source needs.
        public static uint ToPixels(RenderWindow source, uint design) => (uint)MathF.Ceiling(design * For(source));

        // A design coordinate floored onto the device pixel grid of the control's window.
        public static float Snap(Control control, float design)
        {
            float scale = ScaleOf(control);
            return MathF.Floor(design * scale) / scale;
        }

        // A design width rounded to whole device pixels, at least one.
        public static float Hairline(Control control, float design)
        {
            float scale = ScaleOf(control);
            return MathF.Max(1f, MathF.Round(design * scale)) / scale;
        }

        private static float ScaleOf(Control control)
        {
            Control c = control;
            while (c.parent is Control p) c = p;
            return c is WindowRoot root ? root.scale : 1f;
        }

        // Re-lays a window's tree at its current scale.
        public static void Apply(RenderWindow window)
        {
            WindowRoot? root = window.ui?.uiRoot;
            if (root == null) return;

            root.scale = For(window);
            root.FitTo(window.os.windowSize);
        }

        [A_XSDActionDependency("UI.Rescale", "Settings", "Re-lays every window at the current display scale and zoom")]
        public static void Rescale()
        {
            foreach (RenderWindow window in Engine.windows.Values)
                Apply(window);
        }

        [A_XSDActionDependency("UI.ZoomIn", "Input", "Makes the whole UI one step larger")]
        public static void ZoomIn() => SetZoom(SettingsRegistry.Get<UISettings>().zoom.percent + zoomStep);

        [A_XSDActionDependency("UI.ZoomOut", "Input", "Makes the whole UI one step smaller")]
        public static void ZoomOut() => SetZoom(SettingsRegistry.Get<UISettings>().zoom.percent - zoomStep);

        [A_XSDActionDependency("UI.ZoomReset", "Input", "Returns the UI to 100%")]
        public static void ZoomReset() => SetZoom(100f);

        private static void SetZoom(float percent)
        {
            SettingsRegistry.Get<UISettings>().zoom.percent = Math.Clamp(percent, minZoom, maxZoom);
            SettingsRegistry.Apply();
        }
    }
}
