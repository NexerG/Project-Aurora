using ArctisAurora.Core.Registry;
using ArctisAurora.Core.UI;
using ArctisAurora.EngineWork;
using Carbon.Editor;
using Carbon.Editor.CustomControls;

namespace Carbon
{
    internal class Carbon
    {
        static void Main(string[] args)
        {
            Engine engine = new Engine();
            XSDGenerator.GenerateXSD();

            SettingsRegistry.SetWriteRoot(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Carbon", "Settings"));

            engine.Init(false);
            InputHandler.SetActiveKeybindGroup("InputMap");

            WindowRoot root = (WindowRoot)Control.ParseXML("main");

            Engine.primary.uiDocument = "main";
            Engine.primary.uiNext.uiRoot = root;

            Wire(root);

            engine.Run();
        }

        // The views only ever hear about a capture through Comparison, so no control looks another up.
        private static void Wire(WindowRoot root)
        {
            NextSessionListControl sessions = (NextSessionListControl)root.FindByName("Sessions");

            Comparison.Attach(
                (NextFrameStripControl)root.FindByName("Strip"),
                (NextFrameStripControl)root.FindByName("CompareStrip"),
                (NextSpanChartControl)root.FindByName("Flame"),
                (NextSpanChartControl)root.FindByName("Timeline"),
                (NextZoneTableControl)root.FindByName("Zones"),
                (NextSliderControl)root.FindByName("Scale"),
                (NextLabelControl)root.FindByName("Offset"),
                (NextLabelControl)root.FindByName("ScaleValue"),
                (NextLabelControl)root.FindByName("Pools"));

            sessions.onSessionLoaded = Comparison.Load;
        }
    }
}
