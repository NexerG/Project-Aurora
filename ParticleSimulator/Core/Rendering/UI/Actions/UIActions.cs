using ArctisAurora.Core.AssetRegistry;
using ArctisAurora.EngineWork.Rendering.UI.Controls;
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
        [A_XSDActionDependency("TestAction", "UI")]
        public static void TestAction()
        {
            Console.WriteLine("TestAction executed!");
        }
    }
}