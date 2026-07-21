using ArctisAurora.Core.Registry;

namespace ArctisAurora.Core.UISystem.Actions
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