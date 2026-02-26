using ArctisAurora.Core.AssetRegistry;
using ArctisAurora.EngineWork.Rendering.UI.Controls;
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

        [A_VulkanAction, A_XSDActionDependency("ExitApplication", category: "UI")]
        public static void ExitApplication()
        {
            Console.WriteLine("Exiting application...");
            Environment.Exit(0);
        }

        [A_VulkanAction, A_XSDActionDependency("DummyHover", category:"Input")]
        public static void DummyHover()
        {
            Console.WriteLine("Hovering over button");
        }

        [A_VulkanAction, A_XSDActionDependency("DummyKeyPress")]
        public static void DummyKeyPress()
        {
            Console.WriteLine("Key pressed");
        }
    }
}