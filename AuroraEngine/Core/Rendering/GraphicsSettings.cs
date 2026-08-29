using ArctisAurora.Core.Diagnostics;
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

    [A_XSDType("Validation", "Settings")]
    public class VulkanValidationSetting : Setting
    {
        // read once at instance creation, so a change takes effect on the next launch
        [A_XSDElementProperty("Enabled", "Settings")]
        public bool enabled { get; set; } = true;

        [A_XSDElementProperty("Synchronization", "Settings")]
        public bool synchronization { get; set; } = true;

        [A_XSDElementProperty("BestPractices", "Settings")]
        public bool bestPractices { get; set; } = false;

        [A_XSDElementProperty("GpuAssisted", "Settings")]
        public bool gpuAssisted { get; set; } = false;

        // read per message
        [A_XSDElementProperty("MinLevel", "Settings")]
        public LogLevel minLevel { get; set; } = LogLevel.Warn;
    }

    [A_XSDType("Graphics", "Settings", AllowedChildren = typeof(Setting))]
    public class GraphicsSettings : SettingCategory
    {
        public readonly DeviceSetting device = new DeviceSetting();
        public readonly MonitorSetting monitor = new MonitorSetting();
        public readonly WindowSetting window = new WindowSetting();
        public readonly VSyncSetting vsync = new VSyncSetting { onChanged = "Renderer.RequestSwapchainRebuild" };
        public readonly VulkanValidationSetting validation = new VulkanValidationSetting();
    }
}
