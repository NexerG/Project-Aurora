using ArctisAurora.EngineWork.Rendering.UI.Controls.Containers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ArctisAurora.EngineWork.Rendering.UI.Actions
{
    public class UIActions
    {
        [A_VulkanAction]
        public static void TestAction()
        {
            Console.WriteLine("TestAction executed!");
        }
    }
}