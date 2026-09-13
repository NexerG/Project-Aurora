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
        private const string zonesName = "Zones";

        [A_XSDActionDependency("Carbon.LoadCapture", "UI", "Picks a capture folder and reads every thread file in it")]
        public static void LoadCapture()
        {
            if (Engine.primary.uiNext.uiRoot.FindByName(sessionsName) is not NextSessionListControl sessions) return;

            FolderPicker.Pick(Engine.primary, SettingsRegistry.Get<CarbonSettings>().captureRoot.Resolved, picked =>
            {
                if (picked != null) sessions.Load(picked);
            });
        }

        [A_XSDActionDependency("Carbon.PinBaseline", "UI", "Compares every capture loaded after this against the one loaded now")]
        public static void PinBaseline()
        {
            if (Engine.primary.uiNext.uiRoot.FindByName(sessionsName) is not NextSessionListControl { Loaded: not null } sessions) return;
            if (Engine.primary.uiNext.uiRoot.FindByName(zonesName) is not NextZoneTableControl zones) return;

            zones.SetBaseline(sessions.Loaded);
        }

        [A_XSDActionDependency("Carbon.ClearBaseline", "UI", "Stops comparing against the pinned baseline")]
        public static void ClearBaseline()
        {
            if (Engine.primary.uiNext.uiRoot.FindByName(zonesName) is NextZoneTableControl zones)
                zones.SetBaseline(null);
        }
    }
}
