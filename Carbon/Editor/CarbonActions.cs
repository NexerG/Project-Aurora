using ArctisAurora.Core.Filing;
using ArctisAurora.Core.Registry;
using ArctisAurora.EngineWork;
using Carbon.Editor.CustomControls;

namespace Carbon.Editor
{
    public static class CarbonActions
    {
        // control names in UI.ui.xml
        private const string sessionsName = "Sessions";

        [A_XSDActionDependency("Carbon.LoadCapture", "UI", "Picks a capture folder and reads every thread file in it")]
        public static void LoadCapture()
        {
            if (Engine.primary.ui.uiRoot.FindByName(sessionsName) is not SessionListControl sessions) return;

            FolderPicker.Pick(Engine.primary, SettingsRegistry.Get<CarbonSettings>().captureRoot.Resolved, picked =>
            {
                if (picked != null) sessions.Load(picked);
            });
        }

        [A_XSDActionDependency("Carbon.PinBaseline", "UI", "Compares every capture loaded after this against the one loaded now")]
        public static void PinBaseline()
        {
            Comparison.Pin();
        }

        [A_XSDActionDependency("Carbon.ClearBaseline", "UI", "Stops comparing against the pinned baseline")]
        public static void ClearBaseline()
        {
            Comparison.Clear();
        }

        [A_XSDActionDependency("Carbon.SlideLeft", "UI", "Moves the lower frame strip one frame left")]
        public static void SlideLeft()
        {
            Comparison.Slide(-1);
        }

        [A_XSDActionDependency("Carbon.SlideRight", "UI", "Moves the lower frame strip one frame right")]
        public static void SlideRight()
        {
            Comparison.Slide(1);
        }

        [A_XSDActionDependency("Carbon.SwapStrips", "UI", "Swaps which capture sits in the upper and the lower frame strip")]
        public static void SwapStrips()
        {
            Comparison.Swap();
        }
    }
}
