using ArctisAurora.Core.Registry;

namespace ArctisAurora.Core.UI
{
    [A_XSDType("DragGhost", "Settings")]
    public class DragGhostSetting : Setting
    {
        [A_XSDElementProperty("Opacity", "Settings", "Opacity of the preview window, unless the control overrides it.")]
        public float opacity { get; set; } = 0.7f;
    }

    [A_XSDType("Palette", "Settings")]
    public class PaletteSetting : Setting
    {
        [A_XSDElementProperty("Name", "Settings", "Palette a tree that names none paints with, from Palettes/*.palette.xml.")]
        public string name { get; set; } = "default";
    }

    [A_XSDType("UI", "Settings", AllowedChildren = typeof(Setting))]
    public class UISettings : SettingCategory
    {
        public readonly DragGhostSetting dragGhost = new DragGhostSetting();
        public readonly PaletteSetting palette = new PaletteSetting();
    }
}
