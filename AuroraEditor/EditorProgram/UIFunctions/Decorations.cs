using ArctisAurora.Core.Diagnostics;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.UISystem.Actions;
using ArctisAurora.EngineWork;

namespace AuroraEditor.EditorProgram.UIFunctions
{
    public class Decorations
    {
        private static readonly LogChannel Log = LogChannel.For("UI");

        [A_XSDActionDependency("ExitApplication", category: "Input")]
        public static void ExitApplication()
        {
            Log.Info($"exiting application");
            Environment.Exit(0);
        }

        [A_XSDActionDependency("DummyHover", category:"Input")]
        public static void DummyHover()
        {
            Log.Debug($"hovering over button");
        }

        [A_XSDActionDependency("DummyKeyPress", category:"Input")]
        public static void DummyKeyPress()
        {
            Log.Debug($"last character input was '{InputHandler.lastCharInput}'");
        }

        // A swap target is a named action rather than an argument, so each document a host can show
        // gets one of these.
        [A_XSDActionDependency("UI.ShowAlt", "UI", "Swaps the invoking window to the alternate layout")]
        public static void ShowAlt() => UIActions.Invoking().ui.SetUI("alt");

        [A_XSDActionDependency("UI.ShowMain", "UI", "Swaps the invoking window back to the main layout")]
        public static void ShowMain() => UIActions.Invoking().ui.SetUI("main");
    }
}