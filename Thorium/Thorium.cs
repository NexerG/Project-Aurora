using ArctisAurora.Core.Registry;
using ArctisAurora.Core.UISystem;
using ArctisAurora.Core.UISystem.Controls;
using ArctisAurora.Core.UISystem.Controls.Containers;
using ArctisAurora.EngineWork;
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
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Thorium", "Settings"));

            engine.Init(false);
            InputHandler.SetActiveKeybindGroup("InputMap");

            // The vault booted into is one that has been opened, so the browser lists it.
            KnownVaults known = SettingsRegistry.Get<KnownVaults>();
            known.Prune();
            known.Remember(SettingsRegistry.Get<ThoriumSettings>().vault.path);

            // A layout belongs to the vault it was arranged in.
            SessionLayout.scope = KnownVaults.Resolve(SettingsRegistry.Get<ThoriumSettings>().vault.path);
            SessionLayout.tabFactory = VaultBrowserControl.BuildTab;

            ContextMenus.menuFactory = () => new WindowedContextMenuControl();
            // prepare level

            // One-shot atlas bake — this is the set currently in Data/Fonts/arial.
            //AssetImporter.ImportFont(
            //    " !\"#$%&'()*+,-./0123456789:;<=>?@" +
            //    "ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`" +
            //    "abcdefghijklmnopqrstuvwxyz{|}~" +
            //    "ĄČĘĖĮŠŲŪŽąčęėįšųūž",
            //    "arial.ttf");

            WindowControl windowControl = (WindowControl)VulkanControl.ParseXML("main");
            //PanelControl windowControl = new PanelControl();
            //windowControl.width = 1280;
            //windowControl.height = 720;
            //windowControl.transform.position = new Silk.NET.Maths.Vector3D<float>(640, 360, -10);
            //windowControl.controlColor = VulkanControl.ControlColor.purple;
            //windowControl.contentScalingMode = WindowControl.ScalingMode.Vertical;
            //windowControl.fillWindow = true;
            //windowControl.controlColorHex = "#1f6331";

            Engine.primary.uiDocument = "main";
            Engine.primary.ui.uiRoot = windowControl;
            SessionLayout.Restore();
            // UI.ui.xml seeds both panes, so this would add a second tab for a note already open.
            //VaultBrowserControl.OpenFirstNote();
            //ShortTextControl test = new ShortTextControl();
            //test.transform.position = new Silk.NET.Maths.Vector3D<float>(640, 360, -10);
            //test.text = "somethingBlack";
            //EntityManager.uiTree = test;

            engine.Run();
        }
    }
}