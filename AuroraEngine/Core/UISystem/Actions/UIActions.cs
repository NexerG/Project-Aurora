using ArctisAurora.Core.Registry;

namespace ArctisAurora.Core.UISystem.Actions
{
    public class UIActions
    {
        private static readonly Diagnostics.LogChannel Log = Diagnostics.LogChannel.For("UI");

        [A_XSDActionDependency("TestAction", "UI")]
        public static void TestAction()
        {
            Log.Debug($"TestAction executed");
        }
    }
}