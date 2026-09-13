using ArctisAurora.Core.Registry;
using ArctisAurora.Core.UI;
using ArctisAurora.EngineWork;
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

        // The views only ever hear about a capture through here, so no control looks another up.
        private static void Wire(WindowRoot root)
        {
            NextSessionListControl sessions = (NextSessionListControl)root.FindByName("Sessions");
            NextFrameStripControl strip = (NextFrameStripControl)root.FindByName("Strip");
            NextSpanChartControl flame = (NextSpanChartControl)root.FindByName("Flame");
            NextSpanChartControl timeline = (NextSpanChartControl)root.FindByName("Timeline");
            NextZoneTableControl zones = (NextZoneTableControl)root.FindByName("Zones");

            // The strip is last, because taking a session is what makes it choose a frame.
            sessions.onSessionLoaded = session =>
            {
                flame.SetSession(session);
                timeline.SetSession(session);
                zones.SetSession(session);
                strip.SetSession(session);
            };

            strip.onFrameSelected = (thread, frame) =>
            {
                flame.SetFrame(thread, frame);
                timeline.SetFrame(thread, frame);
            };
        }
    }
}
