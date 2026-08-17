using ArctisAurora.Core.Registry;

namespace ArctisAurora.EngineWork
{
    [A_XSDType("DoubleClick", "Settings")]
    public class DoubleClickSetting : Setting
    {
        [A_XSDElementProperty("Timeout", "Settings", "Seconds within which a second press counts as the same tap sequence.")]
        public float timeout { get; set; } = 0.3f;
    }

    [A_XSDType("KeyRepeat", "Settings")]
    public class KeyRepeatSetting : Setting
    {
        [A_XSDElementProperty("Delay", "Settings", "Seconds a key is held before it starts repeating.")]
        public float delay { get; set; } = 0.35f;

        [A_XSDElementProperty("Rate", "Settings", "Seconds between repeats once repeating has started.")]
        public float rate { get; set; } = 0.03f;
    }

    [A_XSDType("Input", "Settings", AllowedChildren = typeof(Setting))]
    public class InputSettings : SettingCategory
    {
        public readonly DoubleClickSetting doubleClick = new DoubleClickSetting();
        public readonly KeyRepeatSetting keyRepeat = new KeyRepeatSetting();
    }
}
