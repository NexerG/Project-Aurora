using ArctisAurora.Core.Registry;

namespace ArctisAurora.Core.Diagnostics
{
    [A_XSDType("CaptureMode", "Settings")]
    public enum CaptureMode : byte
    {
        Off,
        Continuous,
        Boot
    }

    [A_XSDType("ProfilingReport", "Settings")]
    public class ProfilingReportSetting : Setting
    {
        [A_XSDElementProperty("Enabled", "Settings")]
        public bool enabled { get; set; } = false;
    }

    [A_XSDType("ProfilingCapture", "Settings")]
    public class ProfilingCaptureSetting : Setting
    {
        [A_XSDElementProperty("Mode", "Settings")]
        public CaptureMode mode { get; set; } = CaptureMode.Off;

        // frames a Profiling.Capture burst records, and frames a batch carries to the spool
        [A_XSDElementProperty("BurstFrames", "Settings")]
        public int burstFrames { get; set; } = 300;

        [A_XSDElementProperty("FramesPerBatch", "Settings")]
        public int framesPerBatch { get; set; } = 64;

        // Relative to the folder holding the application's settings; absolute is taken as given.
        [A_XSDElementProperty("Directory", "Settings")]
        public string directory { get; set; } = "Profiling";

        // rotation, in whole capture sessions
        [A_XSDElementProperty("MaxFileMB", "Settings")]
        public int maxFileMB { get; set; } = 64;

        [A_XSDElementProperty("Keep", "Settings")]
        public int keep { get; set; } = 5;
    }

    [A_XSDType("Profiling", "Settings", AllowedChildren = typeof(Setting))]
    public class ProfilingSettings : SettingCategory
    {
        public readonly ProfilingReportSetting report = new ProfilingReportSetting();
        public readonly ProfilingCaptureSetting capture = new ProfilingCaptureSetting();
    }
}
