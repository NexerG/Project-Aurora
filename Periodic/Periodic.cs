
using ArctisAurora.Core.AssetRegistry;
using ArctisAurora.EngineWork;
using ArctisAurora.EngineWork.Rendering.Modules;
using ArctisAurora.EngineWork.Rendering.UI.Controls;

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

            engine.Init(modules, false);
            InputHandler.SetActiveKeybindGroup("InputMap");
            //AssetImporter.ImportFont("abcdefghijklmnoprstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ ", "arial.ttf");
            // prepare level

            XSDGenerator.GenerateXSD();

            WindowControl windowControl = (WindowControl)VulkanControl.ParseXML("UI.xml");
            EntityManager.uiTree = windowControl;

            engine.Run();
        }
    }
}