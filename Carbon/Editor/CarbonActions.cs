using ArctisAurora.Core.Filing;
using ArctisAurora.Core.Registry;
using ArctisAurora.EngineWork;
using Carbon.Editor.CustomControls;

namespace Carbon.Editor
{
    public static class CarbonActions
    {
        // control name in UI.ui.xml
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
    }
}
