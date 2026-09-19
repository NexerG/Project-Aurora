using ArctisAurora.Core.Data;
using ArctisAurora.Core.Diagnostics;
using ArctisAurora.Core.Filing.Serialization;
using ArctisAurora.Core.Registry;
using System.Globalization;
using System.Numerics;
using System.Xml.Linq;

namespace ArctisAurora.Core.Animation
{
    [A_XSDType("ClipLoop", "Animation")]
    public enum ClipLoop : byte
    {
        Once, Loop, PingPong
    }

    [A_XSDType("AnimationEntry", "Animation", isAbstract: true)]
    public abstract class AnimationDefinition { }

    [A_XSDType("Clip", "Animation", typeof(ClipTrackDefinition))]
    public class ClipDefinition : AnimationDefinition
    {
        [A_XSDElementProperty("Name", "Animation", "Name a control or Animations.Play references this clip by.")]
        public string name = "";

        [A_XSDElementProperty("Loop", "Animation", "Once plays and holds its end; Loop restarts; PingPong runs back and forth.")]
        public ClipLoop loop = ClipLoop.Once;

        public readonly List<ClipTrackDefinition> tracks = new List<ClipTrackDefinition>();
        // the latest key time across tracks
        public float duration;
    }

    [A_XSDType("Track", "Animation", typeof(KeyDefinition))]
    public class ClipTrackDefinition
    {
        [A_XSDElementProperty("Property", "Animation", "Animatable property the keys drive, by its XML name.")]
        public string property = "";

        // key range in the Keyframes pool
        public int firstKey;
        public int keyCount;
    }

    [A_XSDType("Key", "Animation")]
    public class KeyDefinition
    {
        [A_XSDElementProperty("Time", "Animation", "Seconds from the start of the clip.")]
        public float time = 0f;

        [A_XSDElementProperty("Value", "Animation", "1, 2 or 4 comma-separated numbers; a Thickness is top, right, bottom, left.")]
        public string value = "0";

        [A_XSDElementProperty("Ease", "Animation", "Easing from this key to the next; CubicBezier reads X1 Y1 X2 Y2, Steps reads Steps.")]
        public EaseKind ease = EaseKind.Linear;

        [A_XSDElementProperty("X1", "Animation", "")]
        public float x1 = 0f;
        [A_XSDElementProperty("Y1", "Animation", "")]
        public float y1 = 0f;
        [A_XSDElementProperty("X2", "Animation", "")]
        public float x2 = 1f;
        [A_XSDElementProperty("Y2", "Animation", "")]
        public float y2 = 1f;
        [A_XSDElementProperty("Steps", "Animation", "")]
        public int steps = 1;
    }

    [A_XSDType("Binding", "Animation", typeof(BindingTrackDefinition))]
    public class BindingDefinition : AnimationDefinition
    {
        [A_XSDElementProperty("Name", "Animation", "Name a control's StateBinding references this binding by.")]
        public string name = "";

        [A_XSDElementProperty("Frequency", "Animation", "How fast the state eases, in Hz. The palette's StateFrequency when left out.")]
        public float frequency = -1f;

        [A_XSDElementProperty("Damping", "Animation", "1 settles without overshoot, below 1 bounces. The palette's StateDamping when left out.")]
        public float damping = -1f;

        public readonly List<BindingTrackDefinition> tracks = new List<BindingTrackDefinition>();
    }

    [A_XSDType("BindingTrack", "Animation")]
    public class BindingTrackDefinition
    {
        [A_XSDElementProperty("Property", "Animation", "Animatable property the state drives, by its XML name.")]
        public string property = "";

        [A_XSDElementProperty("Rest", "Animation", "Value at rest. The property's value when the binding attaches, when left out.")]
        public string? rest;

        [A_XSDElementProperty("Hover", "Animation", "Value while hovered. Rest when left out.")]
        public string? hover;

        [A_XSDElementProperty("Press", "Animation", "Value while pressed. Hover when left out.")]
        public string? press;
    }

    [A_XSDType("Animations", "Animation", typeof(AnimationDefinition), Description = "Root container for named clips and state bindings")]
    public class AnimationMap { }

    // Clips and state bindings from Animations/*.anim.xml; clip keys live in the Keyframes pool.
    public static class AnimationLibrary
    {
        private static readonly LogChannel Log = LogChannel.For("Animation");

        public static DataPool Keys { get; private set; } = null!;
        private static readonly Dictionary<string, ClipDefinition> clips =
            new Dictionary<string, ClipDefinition>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, BindingDefinition> bindings =
            new Dictionary<string, BindingDefinition>(StringComparer.OrdinalIgnoreCase);

        // An unknown name is an authoring error, not a fallback.
        public static ClipDefinition Clip(string name)
            => clips.TryGetValue(name, out ClipDefinition? clip) ? clip
            : throw new Exception($"Clip '{name}' is not defined in any Animations/*.anim.xml.");

        public static BindingDefinition Binding(string name)
            => bindings.TryGetValue(name, out BindingDefinition? binding) ? binding
            : throw new Exception($"Binding '{name}' is not defined in any Animations/*.anim.xml.");

        [A_XSDActionDependency("AnimationLibrary.LoadAnimations", "Bootstrap")]
        public static bool LoadAnimations()
        {
            Keys = DataManager.Get("Keyframes");
            Keys.Rewind();
            clips.Clear();
            bindings.Clear();

            foreach (string file in VirtualFileSystem.EnumerateAll("XML/Documents/Animations", "*.anim.xml"))
            {
                foreach (XElement element in XElement.Load(file).Elements())
                {
                    if (element.Name.LocalName == "Clip")
                    {
                        ClipDefinition clip = ParseClip(element);
                        clips[clip.name] = clip;
                    }
                    else if (element.Name.LocalName == "Binding")
                    {
                        BindingDefinition binding = ParseBinding(element);
                        bindings[binding.name] = binding;
                    }
                }
            }

            Log.Debug($"loaded {clips.Count} clip(s) with {Keys.Count} key(s), {bindings.Count} binding(s).");
            return true;
        }

        // A track's value at local time t.
        public static Vector4 Sample(ReadOnlySpan<Keyframe> keys, int first, int count, float t)
        {
            ReadOnlySpan<Keyframe> track = keys.Slice(first, count);
            if (t <= track[0].time) return track[0].value;

            for (int i = 0; i < track.Length - 1; i++)
            {
                if (t >= track[i + 1].time) continue;
                float span = track[i + 1].time - track[i].time;
                return Vector4.Lerp(track[i].value, track[i + 1].value, Curve.Evaluate(track[i].curve, (t - track[i].time) / span));
            }
            return track[^1].value;
        }

        // Where in one run of the clip elapsed falls.
        public static float LocalTime(float elapsed, float duration, ClipLoop loop)
        {
            if (duration <= 0f) return 0f;
            switch (loop)
            {
                case ClipLoop.Loop: return elapsed % duration;
                case ClipLoop.PingPong:
                    float p = elapsed % (2f * duration);
                    return p <= duration ? p : 2f * duration - p;
                default: return Math.Min(elapsed, duration);
            }
        }

        // "a", "a,b", "a,b,c" or "a,b,c,d"; missing components are 0.
        public static Vector4 ParseValue(string value)
        {
            string[] parts = value.Split(',');
            Span<float> v = stackalloc float[4];
            for (int i = 0; i < parts.Length && i < 4; i++)
                v[i] = float.Parse(parts[i], CultureInfo.InvariantCulture);
            return new Vector4(v[0], v[1], v[2], v[3]);
        }

        private static ClipDefinition ParseClip(XElement element)
        {
            ClipDefinition clip = new ClipDefinition();
            clip.name = element.Attribute("Name")?.Value ?? "";
            clip.loop = Enum.Parse<ClipLoop>(element.Attribute("Loop")?.Value ?? nameof(ClipLoop.Once), true);

            foreach (XElement trackElement in element.Elements())
            {
                ClipTrackDefinition track = new ClipTrackDefinition();
                track.property = trackElement.Attribute("Property")?.Value ?? "";
                track.firstKey = Keys.Count;

                foreach (XElement keyElement in trackElement.Elements())
                {
                    int row = Keys.Append();
                    Keys.GetSpan<Keyframe>()[row] = ParseKey(keyElement);
                    clip.duration = Math.Max(clip.duration, Keys.GetSpan<Keyframe>()[row].time);
                }

                track.keyCount = Keys.Count - track.firstKey;
                if (track.keyCount == 0) throw new Exception($"Clip '{clip.name}' has a {track.property} track with no keys.");
                clip.tracks.Add(track);
            }
            return clip;
        }

        private static Keyframe ParseKey(XElement element)
        {
            EaseKind ease = Enum.Parse<EaseKind>(element.Attribute("Ease")?.Value ?? nameof(EaseKind.Linear), true);
            Curve curve = ease switch
            {
                EaseKind.CubicBezier => Curve.Bezier(Read(element, "X1", 0f), Read(element, "Y1", 0f), Read(element, "X2", 1f), Read(element, "Y2", 1f)),
                EaseKind.Steps => Curve.Stepped((int)Read(element, "Steps", 1f)),
                _ => Curve.Ease(ease)
            };
            return new Keyframe
            {
                time = Read(element, "Time", 0f),
                curve = curve,
                value = ParseValue(element.Attribute("Value")?.Value ?? "0")
            };
        }

        private static BindingDefinition ParseBinding(XElement element)
        {
            BindingDefinition binding = new BindingDefinition();
            binding.name = element.Attribute("Name")?.Value ?? "";
            binding.frequency = Read(element, "Frequency", binding.frequency);
            binding.damping = Read(element, "Damping", binding.damping);

            foreach (XElement trackElement in element.Elements())
            {
                binding.tracks.Add(new BindingTrackDefinition
                {
                    property = trackElement.Attribute("Property")?.Value ?? "",
                    rest = trackElement.Attribute("Rest")?.Value,
                    hover = trackElement.Attribute("Hover")?.Value,
                    press = trackElement.Attribute("Press")?.Value
                });
            }
            return binding;
        }

        private static float Read(XElement element, string name, float fallback)
        {
            string? value = element.Attribute(name)?.Value;
            return string.IsNullOrEmpty(value) ? fallback : float.Parse(value, CultureInfo.InvariantCulture);
        }
    }
}
