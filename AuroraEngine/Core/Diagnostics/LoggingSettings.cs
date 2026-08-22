using ArctisAurora.Core.Registry;

namespace ArctisAurora.Core.Diagnostics
{
    [A_XSDType("LogConsole", "Settings")]
    public class LogConsoleSetting : Setting
    {
        [A_XSDElementProperty("MinLevel", "Settings")]
        public LogLevel minLevel { get; set; } = LogLevel.Info;
    }

    [A_XSDType("LogFile", "Settings")]
    public class LogFileSetting : Setting
    {
        [A_XSDElementProperty("MinLevel", "Settings")]
        public LogLevel minLevel { get; set; } = LogLevel.Debug;

        // Relative to the folder holding the application's settings; absolute is taken as given.
        [A_XSDElementProperty("Directory", "Settings")]
        public string directory { get; set; } = "Logs";

        [A_XSDElementProperty("Name", "Settings")]
        public string name { get; set; } = "engine";

        // buffer and its two other spill triggers
        [A_XSDElementProperty("BufferKB", "Settings")]
        public int bufferKB { get; set; } = 64;

        [A_XSDElementProperty("FlushMs", "Settings")]
        public int flushMs { get; set; } = 2000;

        // rotation
        [A_XSDElementProperty("MaxFileMB", "Settings")]
        public int maxFileMB { get; set; } = 16;

        [A_XSDElementProperty("Keep", "Settings")]
        public int keep { get; set; } = 5;
    }

    [A_XSDType("LogRecorder", "Settings")]
    public class LogRecorderSetting : Setting
    {
        [A_XSDElementProperty("Enabled", "Settings")]
        public bool enabled { get; set; } = true;

        [A_XSDElementProperty("MinLevel", "Settings")]
        public LogLevel minLevel { get; set; } = LogLevel.Trace;

        [A_XSDElementProperty("CapacityKB", "Settings")]
        public int capacityKB { get; set; } = 512;
    }

    [A_XSDType("Logging", "Settings", AllowedChildren = typeof(Setting))]
    public class LoggingSettings : SettingCategory
    {
        public readonly LogConsoleSetting console = new LogConsoleSetting();
        public readonly LogFileSetting file = new LogFileSetting();
        public readonly LogRecorderSetting recorder = new LogRecorderSetting();
    }
}
