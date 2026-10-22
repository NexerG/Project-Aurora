using ArctisAurora.Core.Filing.Serialization;
using ArctisAurora.Core.Registry;

namespace Thorium
{
    [A_XSDType("Vault", "Settings")]
    public class VaultSetting : Setting
    {
        [A_XSDElementProperty("Path", "Settings", "Folder the note browser lists. Relative paths resolve through the mounted Data folders; absolute ones are used as they are.")]
        public string path { get; set; } = "Notes";
    }

    [A_XSDType("Startup", "Settings")]
    public class StartupSetting : Setting
    {
        [A_XSDType("StartupView", "Settings")]
        public enum StartupView
        {
            LastVault, VaultBrowser
        }

        [A_XSDElementProperty("AtLogin", "Settings", "Thorium starts when you sign in to Windows.")]
        public bool atLogin { get; set; } = false;

        [A_XSDElementProperty("Opens", "Settings", "What Thorium opens: the vaults open when it last closed, or the vault browser.")]
        public StartupView opens { get; set; } = StartupView.LastVault;

        [A_XSDElementProperty("Stickies", "Settings", "A start at login opens the stickies that are not in a vault.")]
        public bool stickies { get; set; } = true;

        [A_XSDElementProperty("InTray", "Settings", "A start at login keeps the main window and the vault's windows in the tray.")]
        public bool inTray { get; set; } = false;
    }

    [A_XSDType("Thorium", "Settings", AllowedChildren = typeof(Setting))]
    public class ThoriumSettings : SettingCategory
    {
        public readonly VaultSetting vault = new VaultSetting();
        public readonly StartupSetting startup = new StartupSetting { onChanged = "Startup.Apply" };
    }

    [A_XSDType("KnownVault", "Settings")]
    public class KnownVault
    {
        [A_XSDElementProperty("Path", "Settings", "Absolute folder of a vault that has been opened.")]
        public string path { get; set; } = "";
    }

    // Every vault that has been opened, so the browser has something to list. Not a SettingCategory:
    // a category's children are one Setting each and diff by their attributes, which cannot hold a
    // list — the InputBindings shape.
    [A_XSDType("Vaults", "Settings", AllowedChildren = typeof(KnownVault))]
    public class KnownVaults : ISettingsGroup
    {
        [A_XSDElementProperty("KnownVault", "Settings")]
        public List<KnownVault> vaults = new List<KnownVault>();

        // A relative vault path resolves through the mounted Data folders, the same rule the note
        // browser and DocumentEditorControl.LoadPath already apply to a Source.
        public static string Resolve(string path) =>
            Path.IsPathRooted(path) ? path : VirtualFileSystem.ResolveDir(path);

        public static bool SamePath(string a, string b) =>
            string.Equals(Path.TrimEndingDirectorySeparator(a ?? ""),
                          Path.TrimEndingDirectorySeparator(b ?? ""), StringComparison.OrdinalIgnoreCase);

        public void Remember(string path)
        {
            string full = Resolve(path);
            if (string.IsNullOrEmpty(full) || vaults.Any(v => SamePath(v.path, full))) return;

            vaults.Add(new KnownVault { path = full });
        }

        // A vault that was moved or deleted stops being one.
        public void Prune() => vaults.RemoveAll(v => !Directory.Exists(v.path));
    }
}
