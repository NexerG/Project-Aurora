using ArctisAurora.Core.Registry;

namespace Carbon
{
    [A_XSDType("CaptureRoot", "Settings")]
    public class CaptureRootSetting : Setting
    {
        [A_XSDElementProperty("Path", "Settings", "Folder holding capture session folders. Environment variables are expanded; the default is where a Thorium capture lands.")]
        public string path { get; set; } = @"%APPDATA%\Arktis\Thorium\Profiling";

        public string Resolved => Environment.ExpandEnvironmentVariables(path);
    }

    [A_XSDType("TestRoot", "Settings")]
    public class TestRootSetting : Setting
    {
        [A_XSDElementProperty("Path", "Settings", "Folder holding test run folders. Environment variables are expanded; the default is where a Thorium --test run lands.")]
        public string path { get; set; } = @"%APPDATA%\Arktis\Thorium\Tests";

        public string Resolved => Environment.ExpandEnvironmentVariables(path);
    }

    [A_XSDType("Carbon", "Settings", AllowedChildren = typeof(Setting))]
    public class CarbonSettings : SettingCategory
    {
        public readonly CaptureRootSetting captureRoot = new CaptureRootSetting();
        public readonly TestRootSetting testRoot = new TestRootSetting();
    }
}
