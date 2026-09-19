using ArctisAurora.Core.Animation;
using ArctisAurora.Core.Data;
using ArctisAurora.Core.Filing.Serialization;
using ArctisAurora.Core.Registry;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Xml.Linq;

namespace ArctisAurora.Core.UI
{
    [A_XSDType("EffectLoop", "UI")]
    public enum EffectLoop
    {
        Once, Loop, PingPong
    }

    [A_XSDType("Effect", "UI")]
    public class EffectDefinition
    {
        [A_XSDElementProperty("Name", "UI", "Name a control or run references this effect by.")]
        public string name = "";

        [A_XSDElementProperty("Duration", "UI", "Seconds per cycle.")]
        public float duration = 1f;

        [A_XSDElementProperty("Loop", "UI", "Once plays and holds its end; Loop restarts; PingPong runs back and forth.")]
        public EffectLoop loop = EffectLoop.Once;

        [A_XSDElementProperty("Stagger", "UI", "Seconds between consecutive glyphs of a run starting.")]
        public float stagger = 0f;

        [A_XSDElementProperty("Ease", "UI", "Easing curve; CubicBezier reads X1 Y1 X2 Y2, Steps reads Steps.")]
        public EaseKind ease = EaseKind.Linear;

        [A_XSDElementProperty("X1", "UI", "")]
        public float x1 = 0f;
        [A_XSDElementProperty("Y1", "UI", "")]
        public float y1 = 0f;
        [A_XSDElementProperty("X2", "UI", "")]
        public float x2 = 1f;
        [A_XSDElementProperty("Y2", "UI", "")]
        public float y2 = 1f;
        [A_XSDElementProperty("Steps", "UI", "")]
        public int steps = 1;

        [A_XSDElementProperty("OffsetFrom", "UI", "Start offset in design pixels, \"x,y\".")]
        public string offsetFrom = "0,0";
        [A_XSDElementProperty("OffsetTo", "UI", "End offset in design pixels, \"x,y\".")]
        public string offsetTo = "0,0";
        [A_XSDElementProperty("ScaleFrom", "UI", "")]
        public float scaleFrom = 1f;
        [A_XSDElementProperty("ScaleTo", "UI", "")]
        public float scaleTo = 1f;
        [A_XSDElementProperty("RotateFrom", "UI", "Degrees.")]
        public float rotateFrom = 0f;
        [A_XSDElementProperty("RotateTo", "UI", "Degrees.")]
        public float rotateTo = 0f;
        [A_XSDElementProperty("AlphaFrom", "UI", "")]
        public float alphaFrom = 1f;
        [A_XSDElementProperty("AlphaTo", "UI", "")]
        public float alphaTo = 1f;
    }

    [A_XSDType("Effects", "UI", typeof(EffectDefinition), Description = "Root container for named effect definitions")]
    public class EffectMap { }

    // One effect as the vertex shader reads it.
    [StructLayout(LayoutKind.Sequential), A_XSDType("GpuEffect", "DataPools")]
    public struct GpuEffect
    {
        public Vector2 offsetFrom;
        public Vector2 offsetTo;
        public float scaleFrom;
        public float scaleTo;
        // radians
        public float rotateFrom;
        public float rotateTo;
        public float alphaFrom;
        public float alphaTo;
        public float duration;
        public float stagger;
        public uint loop;
        public uint ease;
        public uint steps;
        public Vector4 bezier;
    }

    // Named effects authored in Effects/*.effects.xml, a table the vertex shader evaluates against engine time.
    public static class Effects
    {
        private static readonly Diagnostics.LogChannel Log = Diagnostics.LogChannel.For("UI");

        // the effect table; slot 0 is reserved and never named, so a zeroed row has no effect
        public static DataPool Pool { get; private set; } = null!;
        private static readonly Dictionary<string, uint> indices =
            new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);

        // An unnamed effect is index 0. An unknown name is an authoring error, not a fallback.
        public static uint IndexOf(string? name)
        {
            if (string.IsNullOrEmpty(name)) return 0;
            if (indices.TryGetValue(name, out uint index)) return index;
            throw new Exception($"Effect '{name}' is not defined in any Effects/*.effects.xml.");
        }

        // Seconds between consecutive glyphs of a run under this effect.
        public static float Stagger(uint id) => id == 0 ? 0f : Pool.GetSpan<GpuEffect>()[(int)id].stagger;

        [A_XSDActionDependency("Effects.LoadEffects", "Bootstrap")]
        public static bool LoadEffects()
        {
            Pool = DataManager.Get("Effects");
            Pool.Rewind();
            int reserved = Pool.Append();
            Pool.GetSpan<GpuEffect>()[reserved] = default;
            indices.Clear();

            foreach (string path in VirtualFileSystem.EnumerateAll("XML/Documents/Effects", "*.effects.xml"))
            {
                foreach (XElement element in XElement.Load(path).Elements())
                {
                    EffectDefinition definition = Parse(element);
                    int row = Pool.Append();
                    indices[definition.name] = (uint)row;
                    Pool.GetSpan<GpuEffect>()[row] = Bake(definition);
                }
            }

            Log.Debug($"loaded {indices.Count} effect(s).");
            return true;
        }

        private static EffectDefinition Parse(XElement element)
        {
            EffectDefinition d = new EffectDefinition();
            d.name = element.Attribute("Name")?.Value ?? "";
            d.duration = Read(element, "Duration", d.duration);
            d.loop = Enum.Parse<EffectLoop>(element.Attribute("Loop")?.Value ?? nameof(EffectLoop.Once), true);
            d.stagger = Read(element, "Stagger", d.stagger);
            d.ease = Enum.Parse<EaseKind>(element.Attribute("Ease")?.Value ?? nameof(EaseKind.Linear), true);
            d.x1 = Read(element, "X1", d.x1);
            d.y1 = Read(element, "Y1", d.y1);
            d.x2 = Read(element, "X2", d.x2);
            d.y2 = Read(element, "Y2", d.y2);
            d.steps = (int)Read(element, "Steps", d.steps);
            d.offsetFrom = element.Attribute("OffsetFrom")?.Value ?? d.offsetFrom;
            d.offsetTo = element.Attribute("OffsetTo")?.Value ?? d.offsetTo;
            d.scaleFrom = Read(element, "ScaleFrom", d.scaleFrom);
            d.scaleTo = Read(element, "ScaleTo", d.scaleTo);
            d.rotateFrom = Read(element, "RotateFrom", d.rotateFrom);
            d.rotateTo = Read(element, "RotateTo", d.rotateTo);
            d.alphaFrom = Read(element, "AlphaFrom", d.alphaFrom);
            d.alphaTo = Read(element, "AlphaTo", d.alphaTo);
            return d;
        }

        private static float Read(XElement element, string name, float fallback)
        {
            string? value = element.Attribute(name)?.Value;
            return string.IsNullOrEmpty(value) ? fallback : float.Parse(value, CultureInfo.InvariantCulture);
        }

        private static Vector2 Pair(string value)
        {
            string[] parts = value.Split(',');
            return new Vector2(float.Parse(parts[0], CultureInfo.InvariantCulture), float.Parse(parts[1], CultureInfo.InvariantCulture));
        }

        private static GpuEffect Bake(EffectDefinition d) => new GpuEffect
        {
            offsetFrom = Pair(d.offsetFrom),
            offsetTo = Pair(d.offsetTo),
            scaleFrom = d.scaleFrom,
            scaleTo = d.scaleTo,
            rotateFrom = d.rotateFrom * MathF.PI / 180f,
            rotateTo = d.rotateTo * MathF.PI / 180f,
            alphaFrom = d.alphaFrom,
            alphaTo = d.alphaTo,
            duration = d.duration,
            stagger = d.stagger,
            loop = (uint)d.loop,
            ease = (uint)d.ease,
            steps = (uint)Math.Max(d.steps, 1),
            bezier = new Vector4(d.x1, d.y1, d.x2, d.y2)
        };
    }
}
