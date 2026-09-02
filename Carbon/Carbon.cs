using ArctisAurora.Core.Registry;
using ArctisAurora.Core.UISystem.Controls;
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

            WindowControl windowControl = (WindowControl)VulkanControl.ParseXML("main");

            Engine.primary.uiDocument = "main";
            Engine.primary.ui.uiRoot = windowControl;

            Wire(windowControl);

            engine.Run();
        }

        // The views only ever hear about a capture through here, so no control looks another up.
        private static void Wire(WindowControl root)
        {
            SessionListControl sessions = (SessionListControl)root.FindByName("Sessions");
            FrameStripControl strip = (FrameStripControl)root.FindByName("Strip");
            SpanChartControl flame = (SpanChartControl)root.FindByName("Flame");
            SpanChartControl timeline = (SpanChartControl)root.FindByName("Timeline");
            ZoneTableControl zones = (ZoneTableControl)root.FindByName("Zones");

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
