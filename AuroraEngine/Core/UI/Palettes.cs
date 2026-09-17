using ArctisAurora.Core.Filing.Serialization;
using ArctisAurora.Core.Registry;
using System.Globalization;
using System.Numerics;
using System.Xml.Linq;

namespace ArctisAurora.Core.UI
{
    [A_XSDType("PaletteRole", "UI")]
    public enum PaletteRole
    {
        None, Clear,
        Ground, Surface, Chrome, Field, SubField, Line, Accent, Danger,
        Ink, MutedInk
    }

    [A_XSDType("Palette", "UI")]
    public class PaletteDefinition
    {
        [A_XSDElementProperty("Name", "UI", "What a control's Palette attribute calls this palette.")]
        public string name = "";

        // surfaces
        [A_XSDElementProperty("Ground", "UI", "Window background, as a hex code.")]
        public string ground = "";
        [A_XSDElementProperty("Surface", "UI", "Panels and sidebars, as a hex code.")]
        public string surface = "";
        [A_XSDElementProperty("Chrome", "UI", "Title bars, toolbars and tab strips, as a hex code.")]
        public string chrome = "";
        [A_XSDElementProperty("Field", "UI", "Input boxes, as a hex code.")]
        public string field = "";
        [A_XSDElementProperty("SubField", "UI", "Input boxes that sit on a panel or a field, as a hex code.")]
        public string subField = "";
        [A_XSDElementProperty("Line", "UI", "Separators, splitters and borders, as a hex code.")]
        public string line = "";
        [A_XSDElementProperty("Accent", "UI", "Highlights and active state, as a hex code.")]
        public string accent = "";
        [A_XSDElementProperty("Danger", "UI", "Destructive actions, as a hex code.")]
        public string danger = "";
        [A_XSDElementProperty("EdgeAccent", "UI", "Control edges, as a hex code. Accent when left out.")]
        public string edgeAccent = "";

        // text
        [A_XSDElementProperty("DarkInk", "UI", "Text on light backgrounds, as a hex code.")]
        public string darkInk = "";
        [A_XSDElementProperty("LightInk", "UI", "Text on dark backgrounds, as a hex code.")]
        public string lightInk = "";

        // derivation
        [A_XSDElementProperty("Step", "UI", "How far each hover or press step moves a colour toward its text colour, 0 to 1.")]
        public float step = 0.04f;
        [A_XSDElementProperty("Muted", "UI", "How far muted text blends toward the colour behind it, 0 to 1.")]
        public float muted = 0.24f;

        // first slot of this palette's block in the paint table
        internal uint firstSlot;
    }

    // Loaded palettes and the paint table their colours live in. A paint word is a slot in that table,
    // or an 0xRRGGBB colour with inlineBit set.
    public static class Palettes
    {
        private static readonly Diagnostics.LogChannel Log = Diagnostics.LogChannel.For("UI");

        public const uint inlineBit = 0x80000000;

        // block layout: surfaces × states, ink and muted ink per surface, the two raw inks, then the edge accent
        private const uint surfaceCount = 8;
        private const uint stateCount = 3;
        private const uint inkBase = surfaceCount * stateCount;
        private const uint rawInkBase = inkBase + surfaceCount * 2;
        private const uint edgeAccentSlot = rawInkBase + 2;
        private const uint blockSize = edgeAccentSlot + 1;

        private static readonly Dictionary<string, PaletteDefinition> byName =
            new Dictionary<string, PaletteDefinition>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<PaletteDefinition> blocks = new List<PaletteDefinition>();

        // slot 0 is transparent black, so a zeroed row paints nothing
        private static Vector4[] table = { Vector4.Zero };

        // Replaced whole on load, never written in place.
        public static Vector4[] Table => Volatile.Read(ref table);

        public static PaletteDefinition Default { get; internal set; } = null!;

        // Loaded palette names, in load order.
        public static IReadOnlyList<string> Names => blocks.Select(palette => palette.name).ToArray();

        // An empty name is no palette. An unknown name is an authoring error, not a fallback.
        public static PaletteDefinition? Get(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (byName.TryGetValue(name, out PaletteDefinition palette)) return palette;
            throw new Exception($"Palette '{name}' is not defined in any Palettes/*.palette.xml.");
        }

        [A_XSDActionDependency("Palettes.LoadPalettes", "Bootstrap")]
        public static bool LoadPalettes()
        {
            byName.Clear();
            blocks.Clear();
            List<Vector4> built = new List<Vector4> { Vector4.Zero };

            foreach (string file in VirtualFileSystem.EnumerateAll("XML/Documents/Palettes", "*.palette.xml"))
            {
                PaletteDefinition palette = Parse(XElement.Load(file));
                palette.firstSlot = (uint)built.Count;
                Bake(palette, built);
                byName[palette.name] = palette;
                blocks.Add(palette);
            }

            string chosen = SettingsRegistry.Get<UISettings>().palette.name;
            if (!byName.TryGetValue(chosen, out PaletteDefinition fallback))
            {
                Log.Error($"no palette named '{chosen}' — a tree that names no palette has nothing to paint with.");
                return false;
            }

            Default = fallback;
            Volatile.Write(ref table, built.ToArray());
            Log.Debug($"loaded {blocks.Count} palette(s) into {built.Count} paint slots.");
            return true;
        }

        #region ---- paint words ----
        public static uint Inline(Vector3 rgb)
        {
            uint r = (uint)MathF.Round(Math.Clamp(rgb.X, 0f, 1f) * 255f);
            uint g = (uint)MathF.Round(Math.Clamp(rgb.Y, 0f, 1f) * 255f);
            uint b = (uint)MathF.Round(Math.Clamp(rgb.Z, 0f, 1f) * 255f);
            return inlineBit | r << 16 | g << 8 | b;
        }

        public static uint Inline(string hex) => Inline(Control.HexToRGB(hex));

        public static bool IsInline(uint paint) => (paint & inlineBit) != 0;

        // The colour a word paints, as the shader reads it.
        public static Vector3 ColorOf(uint paint)
        {
            if (IsInline(paint))
                return new Vector3((paint >> 16) & 0xFF, (paint >> 8) & 0xFF, paint & 0xFF) / 255f;

            Vector4[] current = Table;
            if (paint >= current.Length) return Vector3.Zero;
            return new Vector3(current[paint].X, current[paint].Y, current[paint].Z);
        }
        #endregion

        #region ---- resolving ----
        // A surface role's slot: state 0 rest, 1 hover, 2 press.
        public static uint Surface(PaletteDefinition palette, PaletteRole role, uint state = 0)
            => palette.firstSlot + (uint)(role - PaletteRole.Ground) * stateCount + state;

        // The slot every unauthored edge paints with.
        public static uint EdgeAccent(PaletteDefinition palette) => palette.firstSlot + edgeAccentSlot;

        // Text on a ground. A surface of this palette has its ink baked; any other ground picks between
        // the palette's two inks here and mixes a muted one inline.
        public static uint Ink(PaletteDefinition palette, uint ground, bool muted)
        {
            if (TryBlock(ground, out PaletteDefinition owner, out uint offset) && owner == palette && offset < inkBase)
                return palette.firstSlot + inkBase + offset / stateCount * 2 + (muted ? 1u : 0u);

            Vector3 back = ColorOf(ground);
            uint raw = palette.firstSlot + rawInkBase + (PicksDark(palette, back) ? 0u : 1u);
            if (!muted) return raw;

            return Inline(Vector3.Lerp(ColorOf(raw), back, palette.muted));
        }

        // A resting colour moved toward its text by state steps. A surface slot at rest steps inside its
        // own block; anything else is mixed inline with this palette's inks.
        public static uint Step(PaletteDefinition palette, uint rest, uint state)
        {
            if (state == 0) return rest;
            if (TryBlock(rest, out _, out uint offset) && offset < inkBase && offset % stateCount == 0)
                return rest + state;

            Vector3 color = ColorOf(rest);
            Vector3 ink = ColorOf(palette.firstSlot + rawInkBase + (PicksDark(palette, color) ? 0u : 1u));
            return Inline(Vector3.Lerp(color, ink, palette.step * state));
        }

        // WCAG contrast ratio, 1 to 21.
        public static float Contrast(Vector3 a, Vector3 b)
        {
            float la = Luminance(a);
            float lb = Luminance(b);
            return (MathF.Max(la, lb) + 0.05f) / (MathF.Min(la, lb) + 0.05f);
        }

        private static bool PicksDark(PaletteDefinition palette, Vector3 back)
        {
            Vector4[] current = Table;
            Vector4 dark = current[palette.firstSlot + rawInkBase];
            Vector4 light = current[palette.firstSlot + rawInkBase + 1];
            return Contrast(new Vector3(dark.X, dark.Y, dark.Z), back) >= Contrast(new Vector3(light.X, light.Y, light.Z), back);
        }

        // The block a slot sits in, and its offset inside it.
        private static bool TryBlock(uint paint, out PaletteDefinition owner, out uint offset)
        {
            owner = null!;
            offset = 0;
            if (IsInline(paint) || paint == 0) return false;

            int index = (int)((paint - 1) / blockSize);
            if (index >= blocks.Count) return false;

            owner = blocks[index];
            offset = paint - owner.firstSlot;
            return true;
        }

        private static float Luminance(Vector3 c) => 0.2126f * Linear(c.X) + 0.7152f * Linear(c.Y) + 0.0722f * Linear(c.Z);

        private static float Linear(float c) => c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
        #endregion

        #region ---- loading ----
        private static PaletteDefinition Parse(XElement element)
        {
            PaletteDefinition palette = new PaletteDefinition
            {
                name = element.Attribute("Name")?.Value ?? "",
                ground = Required(element, "Ground"),
                surface = Required(element, "Surface"),
                chrome = Required(element, "Chrome"),
                field = Required(element, "Field"),
                subField = Required(element, "SubField"),
                line = Required(element, "Line"),
                accent = Required(element, "Accent"),
                danger = Required(element, "Danger"),
                edgeAccent = element.Attribute("EdgeAccent")?.Value ?? "",
                darkInk = Required(element, "DarkInk"),
                lightInk = Required(element, "LightInk")
            };

            string step = element.Attribute("Step")?.Value;
            if (!string.IsNullOrEmpty(step)) palette.step = float.Parse(step, CultureInfo.InvariantCulture);
            string muted = element.Attribute("Muted")?.Value;
            if (!string.IsNullOrEmpty(muted)) palette.muted = float.Parse(muted, CultureInfo.InvariantCulture);

            return palette;
        }

        private static string Required(XElement element, string attribute)
        {
            string value = element.Attribute(attribute)?.Value;
            if (string.IsNullOrEmpty(value))
                throw new Exception($"Palette '{element.Attribute("Name")?.Value}' has no {attribute}.");
            return value;
        }

        private static void Bake(PaletteDefinition palette, List<Vector4> built)
        {
            Vector3 dark = Control.HexToRGB(palette.darkInk);
            Vector3 light = Control.HexToRGB(palette.lightInk);
            Vector3[] surfaces =
            {
                Control.HexToRGB(palette.ground), Control.HexToRGB(palette.surface), Control.HexToRGB(palette.chrome),
                Control.HexToRGB(palette.field), Control.HexToRGB(palette.subField), Control.HexToRGB(palette.line),
                Control.HexToRGB(palette.accent), Control.HexToRGB(palette.danger)
            };

            foreach (Vector3 color in surfaces)
            {
                Vector3 ink = Contrast(dark, color) >= Contrast(light, color) ? dark : light;
                for (uint state = 0; state < stateCount; state++)
                    built.Add(new Vector4(Vector3.Lerp(color, ink, palette.step * state), 1f));
            }

            foreach (Vector3 color in surfaces)
            {
                Vector3 ink = Contrast(dark, color) >= Contrast(light, color) ? dark : light;
                built.Add(new Vector4(ink, 1f));
                built.Add(new Vector4(Vector3.Lerp(ink, color, palette.muted), 1f));
            }

            built.Add(new Vector4(dark, 1f));
            built.Add(new Vector4(light, 1f));
            built.Add(new Vector4(Control.HexToRGB(string.IsNullOrEmpty(palette.edgeAccent) ? palette.accent : palette.edgeAccent), 1f));
        }
        #endregion
    }
}
