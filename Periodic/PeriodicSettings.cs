using ArctisAurora.Core.Registry;

namespace AuroraPeriodic
{
    [A_XSDType("Vault", "Settings")]
    public class VaultSetting : Setting
    {
        [A_XSDElementProperty("Path", "Settings", "Folder the note browser lists. Relative paths resolve through the mounted Data folders; absolute ones are used as they are.")]
        public string path { get; set; } = "Notes";
    }

    [A_XSDType("Periodic", "Settings", AllowedChildren = typeof(Setting))]
    public class PeriodicSettings : SettingCategory
    {
        public readonly VaultSetting vault = new VaultSetting();
    }
}
