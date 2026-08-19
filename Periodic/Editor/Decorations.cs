using ArctisAurora.Core.Registry;
using ArctisAurora.Core.UISystem.Controls;
using ArctisAurora.EngineWork;
using ArctisAurora.EngineWork.Rendering;

namespace Periodic.Editor
{
    public class Decorations
    {
        [A_XSDActionDependency("ExitApplication", category: "Input")]
        public static void ExitApplication()
        {
            Console.WriteLine("Exiting application...");
            Engine.CloseWindow(Engine.primary);
        }
    }
}
