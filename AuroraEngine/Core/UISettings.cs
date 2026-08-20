using ArctisAurora.Core.Registry;

namespace ArctisAurora.Core.UISystem
{
    [A_XSDType("DragGhost", "Settings")]
    public class DragGhostSetting : Setting
    {
        [A_XSDElementProperty("Opacity", "Settings", "Opacity of the preview window, unless the control overrides it.")]
        public float opacity { get; set; } = 0.7f;
    }

    [A_XSDType("UI", "Settings", AllowedChildren = typeof(Setting))]
    public class UISettings : SettingCategory
    {
        public readonly DragGhostSetting dragGhost = new DragGhostSetting();
    }
}
