using ArctisAurora.Core.Registry;
using ArctisAurora.EngineWork;

namespace AuroraEditor.EditorProgram.UIFunctions
{
    public class Decorations
    {

        [A_XSDActionDependency("ExitApplication", category: "Input")]
        public static void ExitApplication()
        {
            Console.WriteLine("Exiting application...");
            Environment.Exit(0);
        }

        [A_XSDActionDependency("DummyHover", category:"Input")]
        public static void DummyHover()
        {
            Console.WriteLine("Hovering over button");
        }

        [A_XSDActionDependency("DummyKeyPress", category:"Input")]
        public static void DummyKeyPress()
        {
            Console.WriteLine($"Last character input was: {InputHandler.lastCharInput}");
        }
    }
}