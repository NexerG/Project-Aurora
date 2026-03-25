
using ArctisAurora.Core.AssetRegistry;
using ArctisAurora.Core.Rendering.UI.Controls.Text;
using ArctisAurora.EngineWork;
using ArctisAurora.EngineWork.Rendering.Modules;
using ArctisAurora.EngineWork.Rendering.UI.Controls;
using ArctisAurora.EngineWork.Serialization;

namespace AuroraPeriodic
{
    internal class Periodic
    {
        static void Main(string[] args)
        {
            Engine engine = new Engine();
            RenderingModule[] modules = new RenderingModule[]
            {
                new UIModule(),
            };

            XSDGenerator.GenerateXSD();

            engine.Init(modules, false);
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

            EntityManager.uiTree = windowControl;
            //ShortTextControl test = new ShortTextControl();
            //test.transform.position = new Silk.NET.Maths.Vector3D<float>(640, 360, -10);
            //test.text = "somethingBlack";
            //EntityManager.uiTree = test;

            engine.Run();
        }
    }
}