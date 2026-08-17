using ArctisAurora.Core.Registry;
using ArctisAurora.Core.UISystem.Actions;

namespace Periodic.Editor
{
    public class Decorations
    {
        [A_XSDActionDependency("ExitApplication", category: "Input")]
        public static void ExitApplication()
        {
            Console.WriteLine("Exiting application...");
            WindowActions.Close();
        }
    }
}
