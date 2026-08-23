using ArctisAurora.Core.Registry;

namespace ArctisAurora.EngineWork
{
    // One remembered rebind. Modifiers are a single space-separated string rather than child
    // elements, because the settings diff compares list entries by their scalars alone.
    [A_XSDType("Bind", "Settings")]
    public class KeybindOverride
    {
        [A_XSDElementProperty("Action", "Settings", "Name of the action whose bind moved.")]
        public string action { get; set; } = "";

        [A_XSDElementProperty("Trigger", "Settings", "Key the action now fires on.")]
        public Keys trigger { get; set; }

        [A_XSDElementProperty("Modifiers", "Settings", "Space-separated keys that must be held with the trigger.")]
        public string modifiers { get; set; } = "";
    }

    [A_XSDType("InputBindings", "Settings", AllowedChildren = typeof(KeybindOverride))]
    public class InputBindings : ISettingsGroup
    {
        [A_XSDElementProperty("Bind", "Settings")]
        public List<KeybindOverride> binds = new List<KeybindOverride>();

        // Replaces the entry for an action, so a key rebound twice leaves one record behind.
        public void Remember(string action, Keys trigger, List<KeybindModifier> modifiers)
        {
            KeybindOverride entry = binds.FirstOrDefault(
                b => string.Equals(b.action, action, StringComparison.OrdinalIgnoreCase));

            if (entry == null)
            {
                entry = new KeybindOverride { action = action };
                binds.Add(entry);
            }

            entry.trigger = trigger;
            entry.modifiers = string.Join(' ', modifiers.Select(m => m.key));
        }

        public static List<KeybindModifier> ParseModifiers(string modifiers) =>
            modifiers.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(k => new KeybindModifier { key = Enum.Parse<Keys>(k) })
                .ToList();
    }
}
