using ArctisAurora.Core.Filing.Serialization;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.UISystem.Controls;
using ArctisAurora.EngineWork;
using ArctisAurora.EngineWork.Rendering;
using Periodic.Editor.CustomControls;
using Silk.NET.Maths;

namespace AuroraPeriodic
{
    internal class Periodic
    {
        static void Main(string[] args)
        {
            Engine engine = new Engine();
            XSDGenerator.GenerateXSD();

            SettingsRegistry.SetWriteRoot(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Periodic", "Settings"));

            engine.Init(false);
            InputHandler.SetActiveKeybindGroup("InputMap");
            // prepare level

            // One-shot atlas bake — this is the set currently in Data/Fonts/arial.
            //AssetImporter.ImportFont(
            //    " !\"#$%&'()*+,-./0123456789:;<=>?@" +
            //    "ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`" +
            //    "abcdefghijklmnopqrstuvwxyz{|}~" +
            //    "ĄČĘĖĮŠŲŪŽąčęėįšųūž",
            //    "arial.ttf");

            WindowControl windowControl = (WindowControl)VulkanControl.ParseXML("UI.xml");
            //PanelControl windowControl = new PanelControl();
            //windowControl.width = 1280;
            //windowControl.height = 720;
            //windowControl.transform.position = new Silk.NET.Maths.Vector3D<float>(640, 360, -10);
            //windowControl.controlColor = VulkanControl.ControlColor.purple;
            //windowControl.contentScalingMode = WindowControl.ScalingMode.Vertical;
            //windowControl.fillWindow = true;
            //windowControl.controlColorHex = "#1f6331";

            Engine.primary.ui.uiRoot = windowControl;
            VaultBrowserControl.OpenFirstNote();
            //ShortTextControl test = new ShortTextControl();
            //test.transform.position = new Silk.NET.Maths.Vector3D<float>(640, 360, -10);
            //test.text = "somethingBlack";
            //EntityManager.uiTree = test;

            engine.Run();
        }
    }
}