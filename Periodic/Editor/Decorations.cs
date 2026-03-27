using ArctisAurora.Core.AssetRegistry;
using ArctisAurora.EngineWork;

namespace Periodic.Editor
{
    public class Decorations
    {
        [A_XSDActionDependency("WriteInput", category:"Input", "Writes the input to the active textbox")]
        public static void Write()
        {

        }

        [A_XSDActionDependency("ExitApplication", category: "Input")]
        public static void ExitApplication()
        {
            Console.WriteLine("Exiting application...");
            Environment.Exit(0);
        }

        [A_XSDActionDependency("DummyHover", category: "Input")]
        public static void DummyHover()
        {
            Console.WriteLine("Hovering over button");
        }

        [A_XSDActionDependency("DummyKeyPress", category: "Input")]
        public static void DummyKeyPress()
        {
            Console.WriteLine($"Last character input was: {InputHandler.lastCharInput}");
        }

        [A_XSDActionDependency("ExitApplication2", category: "UI")]
        public static void ExitApplication2()
        {
            Environment.Exit(0);
        }
    }
}
