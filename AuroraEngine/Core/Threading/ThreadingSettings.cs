using ArctisAurora.Core.Registry;

namespace ArctisAurora.Core.Threading
{
    // Threads the frame graph runs on, main included. 0 = every core the dedicated threads leave.
    [A_XSDType("Threads", "Settings")]
    public class ThreadsSetting : Setting
    {
        [A_XSDElementProperty("Count", "Settings")]
        public int count { get; set; } = 0;
    }

    // Frames per second for the graph and the dedicated threads. 0 = uncapped.
    [A_XSDType("FrameCap", "Settings")]
    public class FrameCapSetting : Setting
    {
        [A_XSDElementProperty("MaxFps", "Settings")]
        public int maxFps { get; set; } = 0;
    }

    [A_XSDType("Threading", "Settings", AllowedChildren = typeof(Setting))]
    public class ThreadingSettings : SettingCategory
    {
        public readonly ThreadsSetting threads = new ThreadsSetting();
        public readonly FrameCapSetting frameCap = new FrameCapSetting();
    }
}
