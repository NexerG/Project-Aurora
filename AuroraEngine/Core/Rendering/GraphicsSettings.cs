using ArctisAurora.Core.Registry;

namespace ArctisAurora.EngineWork.Rendering
{
    [A_XSDType("Device", "Settings")]
    public class DeviceSetting : Setting
    {
        [A_XSDElementProperty("Name", "Settings")]
        public string name { get; set; } = "";
    }

    [A_XSDType("Monitor", "Settings")]
    public class MonitorSetting : Setting
    {
        [A_XSDElementProperty("Name", "Settings")]
        public string name { get; set; } = "";
    }

    [A_XSDType("Window", "Settings")]
    public class WindowSetting : Setting
    {
        [A_XSDType("WindowMode", "Settings")]
        public enum WindowMode
        {
            Windowed, Borderless, Fullscreen
        }

        [A_XSDElementProperty("Mode", "Settings")]
        public WindowMode mode { get; set; } = WindowMode.Windowed;

        [A_XSDElementProperty("Width", "Settings")]
        public uint width { get; set; } = 1280;

        [A_XSDElementProperty("Height", "Settings")]
        public uint height { get; set; } = 720;
    }

    [A_XSDType("VSync", "Settings")]
    public class VSyncSetting : Setting
    {
        [A_XSDElementProperty("On", "Settings")]
        public bool on { get; set; } = true;
    }

    [A_XSDType("Graphics", "Settings", AllowedChildren = typeof(Setting))]
    public class GraphicsSettings : SettingCategory
    {
        public readonly DeviceSetting device = new DeviceSetting();
        public readonly MonitorSetting monitor = new MonitorSetting();
        public readonly WindowSetting window = new WindowSetting();
        public readonly VSyncSetting vsync = new VSyncSetting { onChanged = "Renderer.RequestSwapchainRebuild" };
    }
}
