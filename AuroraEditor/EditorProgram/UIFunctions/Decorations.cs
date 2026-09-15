using ArctisAurora.Core.Diagnostics;
using ArctisAurora.Core.Registry;
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
    }
}