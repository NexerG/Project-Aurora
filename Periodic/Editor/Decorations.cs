using ArctisAurora.Core.AssetRegistry;
using ArctisAurora.EngineWork;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Periodic.Editor
{
    public class Decorations
    {
        [A_XSDActionDependency("WriteInput", "Writer", "Writes the input to the active textbox")]
        public static void Writer()
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
    }
}
