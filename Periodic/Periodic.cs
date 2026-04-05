using ArctisAurora.Core.Registry;
using ArctisAurora.Core.UISystem.Controls;
using ArctisAurora.EngineWork;

namespace AuroraPeriodic
{
    internal class Periodic
    {
        static void Main(string[] args)
        {
            Engine engine = new Engine();
            XSDGenerator.GenerateXSD();

            engine.Init(false);
            InputHandler.SetActiveKeybindGroup("InputMap");
            //AssetImporter.ImportFont("abcdefghijklmnoprstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ <>", "arial.ttf");
            // prepare level


            WindowControl windowControl = (WindowControl)VulkanControl.ParseXML("UI.xml");
            //PanelControl windowControl = new PanelControl();
            //windowControl.width = 1280;
            //windowControl.height = 720;
            //windowControl.transform.position = new Silk.NET.Maths.Vector3D<float>(640, 360, -10);
            //windowControl.controlColor = VulkanControl.ControlColor.purple;
            //windowControl.contentScalingMode = WindowControl.ScalingMode.Vertical;
            //windowControl.fillWindow = true;
            //windowControl.controlColorHex = "#1f6331";

            EntityRegistry.uiTree = windowControl;
            //ShortTextControl test = new ShortTextControl();
            //test.transform.position = new Silk.NET.Maths.Vector3D<float>(640, 360, -10);
            //test.text = "somethingBlack";
            //EntityManager.uiTree = test;

            engine.Run();
        }
    }
}