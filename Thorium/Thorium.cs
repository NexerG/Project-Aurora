using ArctisAurora.Core.Filing;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Security;
using ArctisAurora.Core.UI;
using ArctisAurora.EngineWork;
using ArctisAurora.EngineWork.Rendering;
using Thorium.Editor.CustomControls;

namespace Thorium
{
    internal class Thorium
    {
        static void Main(string[] args)
        {
            Engine engine = new Engine();
            XSDGenerator.GenerateXSD();

            SettingsRegistry.SetWriteRoot(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Arktis", "Thorium", "Settings"));

            string? file = args.Length > 0 && !args[0].StartsWith('-') && File.Exists(args[0]) ? Path.GetFullPath(args[0]) : null;
            Background.standalone = file != null;
            FileAssociations.enabled = true;

            engine.Init(false);
            InputHandler.SetActiveKeybindGroup("InputMap");

            // The vault booted into is one that has been opened, so the browser lists it.
            KnownVaults known = SettingsRegistry.Get<KnownVaults>();
            known.Prune();
            known.Remember(SettingsRegistry.Get<ThoriumSettings>().vault.path);

            // A layout belongs to the vault it was arranged in.
            SessionLayout.scope = KnownVaults.Resolve(SettingsRegistry.Get<ThoriumSettings>().vault.path);
            SessionLayout.tabFactory = VaultBrowserControl.BuildTab;
            NotePropertiesControl.openLink = VaultBrowserControl.OpenLink;
            SheetBook.findSheet = VaultBrowserControl.FindSheet;
            SheetBook.vaultSheets = VaultBrowserControl.VaultSheets;
            SheetBook.vaultNotes = VaultBrowserControl.VaultNotes;
            TabViewControl.newPressed += VaultBrowserControl.NewInPane;
            // prepare level

            // One-shot atlas bake — this is the set currently in Data/Fonts/arial.
            //AssetImporter.ImportFont(
            //    " !\"#$%&'()*+,-./0123456789:;<=>?@" +
            //    "ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`" +
            //    "abcdefghijklmnopqrstuvwxyz{|}~" +
            //    "ĄČĘĖĮŠŲŪŽąčęėįšųūž",
            //    "arial.ttf");

            if (file != null)
            {
                OpenStandalone(file);
                engine.Run();
                return;
            }

            Engine.primary.uiDocument = "main";
            Engine.primary.ui.uiRoot = (WindowRoot)Control.ParseXML("main");
            if (!Background.canTray && Engine.primary.ui.uiRoot.FindByName("TrayButton") is ButtonControl tray) tray.enabled = false;
            if (VaultLock.HasLock(SessionLayout.scope))
            {
                SessionLayout.paused = true;
                Engine.Post(VaultBrowserControl.UnlockVault);
            }
            else SessionLayout.Restore();

            engine.Run();
        }

        // Shows one file in a bare tab window, leaving the vault's layout unrecorded.
        internal static void OpenStandalone(string path)
        {
            SessionLayout.paused = true;
            Engine.primary.uiDocument = "tab-window";
            WindowRoot root = (WindowRoot)Control.ParseXML("tab-window");
            Engine.primary.ui.uiRoot = root;
            WorkspaceControl.In(root)?.LoadDefault();

            if (root.FindByName("Tabs") is not TabViewControl tabs) return;
            TabItemControl tab = VaultBrowserControl.BuildTab(path, false);
            tabs.AddChild(tab);
            tabs.SetActive(tab);
        }
    }
}