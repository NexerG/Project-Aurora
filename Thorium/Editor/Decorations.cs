using ArctisAurora.Core.Diagnostics;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.UISystem.Controls;
using ArctisAurora.EngineWork;
using ArctisAurora.EngineWork.Rendering;

namespace Thorium.Editor
{
    public class Decorations
    {
        private static readonly LogChannel Log = LogChannel.For("UI");

        [A_XSDActionDependency("ExitApplication", category: "Input")]
        public static void ExitApplication()
        {
            Log.Info($"exiting application");
            Engine.CloseWindow(Engine.primary);
        }
    }
}
