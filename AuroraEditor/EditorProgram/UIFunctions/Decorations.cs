using ArctisAurora.EngineWork.Rendering.UI.Controls.Containers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AuroraEditor.EditorProgram.UIFunctions
{
    public class Decorations
    {
        [A_VulkanAction]
        public static void ExitApplication()
        {
            Console.WriteLine("Exiting application...");
            Environment.Exit(0);
        }

        [A_VulkanAction]
        public static void DummyHover()
        {
            Console.WriteLine("Hovering over button");
        }
    }
}