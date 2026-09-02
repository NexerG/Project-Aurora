using ArctisAurora.Core.Registry;

namespace Carbon
{
    [A_XSDType("CaptureRoot", "Settings")]
    public class CaptureRootSetting : Setting
    {
        [A_XSDElementProperty("Path", "Settings", "Folder holding capture session folders. Environment variables are expanded; the default is where a Thorium capture lands.")]
        public string path { get; set; } = @"%APPDATA%\Thorium\Profiling";

        public string Resolved => Environment.ExpandEnvironmentVariables(path);
    }

    [A_XSDType("Carbon", "Settings", AllowedChildren = typeof(Setting))]
    public class CarbonSettings : SettingCategory
    {
        public readonly CaptureRootSetting captureRoot = new CaptureRootSetting();
    }
}
