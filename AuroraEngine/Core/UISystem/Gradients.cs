using ArctisAurora.Core.Filing.Serialization;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.UISystem.Controls;
using Silk.NET.Maths;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Xml.Linq;

namespace ArctisAurora.Core.UISystem
{
    [A_XSDType("GradientKind", "UI")]
    public enum GradientKind
    {
        Linear, Radial
    }

    [A_XSDType("Stop", "UI")]
    public class GradientStopDefinition
    {
        [A_XSDElementProperty("Color", "UI", "Colour at this point of the ramp, as a hex code.")]
        public string colorHex = "#FFFFFF";

        [A_XSDElementProperty("Alpha", "UI", "Opacity at this point of the ramp, 0 to 1.")]
        public float alpha = 1f;

        [A_XSDElementProperty("Pos", "UI", "Where on the ramp this stop sits, 0 to 1.")]
        public float position = 0f;
    }

    [A_XSDType("Gradient", "UI", typeof(GradientStopDefinition))]
    public class GradientDefinition
    {
        [A_XSDElementProperty("Name", "UI", "Name a control references this gradient by.")]
        public string name = "";

        [A_XSDElementProperty("Kind", "UI", "Linear ramps along Angle; Radial ramps outward from the centre.")]
        public GradientKind kind = GradientKind.Linear;

        [A_XSDElementProperty("Angle", "UI", "Linear direction in degrees. 0 is left to right, 90 is top to bottom.")]
        public float angle = 0f;

        [A_XSDElementProperty("CenterX", "UI", "Radial centre across the control, 0 to 1.")]
        public float centerX = 0.5f;

        [A_XSDElementProperty("CenterY", "UI", "Radial centre down the control, 0 to 1.")]
        public float centerY = 0.5f;

        [A_XSDElementProperty("Stop", "UI", "")]
        public List<GradientStopDefinition> stops = new List<GradientStopDefinition>();
    }

    [A_XSDType("Gradients", "UI", typeof(GradientDefinition), Description = "Root container for named gradient definitions")]
    public class GradientMap { }

    [StructLayout(LayoutKind.Sequential)]
    public struct GpuGradientStop
    {
        public Vector4D<float> color;
        public float position;
    }

    [InlineArray(Gradients.MaxStops)]
    public struct GradientStops
    {
        private GpuGradientStop _element0;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GpuGradient
    {
        // direction is the baked unit vector of Angle; center is normalised across the rect
        public Vector2D<float> direction;
        public Vector2D<float> center;
        public uint kind;
        public uint stopCount;
        public GradientStops stops;
    }

    // Named gradients authored in Gradients.xml, uploaded once as a table the fragment shader
    // indexes. A control names one and stores the index, so a definition is shared rather than
    // copied into every row that uses it.
    public static class Gradients
    {
        public const int MaxStops = 8;

        // Slot 0 is reserved and never named, so a zeroed ControlData row is gradient-free.
        private static readonly List<GpuGradient> table = new List<GpuGradient> { default };
        private static readonly Dictionary<string, uint> indices =
            new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);

        public static int Count => table.Count;

        public static GpuGradient[] Table => table.ToArray();

        // An unnamed gradient is index 0. An unknown name is an authoring error, not a fallback.
        public static uint IndexOf(string name)
        {
            if (string.IsNullOrEmpty(name)) return 0;
            if (indices.TryGetValue(name, out uint index)) return index;
            throw new Exception($"Gradient '{name}' is not defined in Gradients.xml.");
        }

        [A_XSDActionDependency("Gradients.LoadGradients", "Bootstrap")]
        public static bool LoadGradients()
        {
            table.RemoveRange(1, table.Count - 1);
            indices.Clear();

            // Hosts with no gradients of their own ship no file at all.
            if (!VirtualFileSystem.TryResolveFile("XML/Documents/Gradients.xml", out string path))
            {
                Console.WriteLine("No Gradients.xml found - no gradients loaded.");
                return true;
            }

            XElement root = XElement.Load(path);
            foreach (XElement element in root.Elements())
            {
                GradientDefinition definition = ParseGradient(element);
                indices[definition.name] = (uint)table.Count;
                table.Add(Bake(definition));
            }

            return true;
        }

        private static GradientDefinition ParseGradient(XElement element)
        {
            GradientDefinition definition = new GradientDefinition
            {
                name = element.Attribute("Name")?.Value ?? "",
                kind = Enum.Parse<GradientKind>(element.Attribute("Kind")?.Value ?? nameof(GradientKind.Linear), true),
                angle = Read(element, "Angle", 0f),
                centerX = Read(element, "CenterX", 0.5f),
                centerY = Read(element, "CenterY", 0.5f)
            };

            foreach (XElement stopElement in element.Elements())
                definition.stops.Add(new GradientStopDefinition
                {
                    colorHex = stopElement.Attribute("Color")?.Value ?? "#FFFFFF",
                    alpha = Read(stopElement, "Alpha", 1f),
                    position = Read(stopElement, "Pos", 0f)
                });

            if (definition.stops.Count == 0)
                throw new Exception($"Gradient '{definition.name}' declares no stops.");
            if (definition.stops.Count > MaxStops)
                throw new Exception($"Gradient '{definition.name}' has {definition.stops.Count} stops; the limit is {MaxStops}.");

            return definition;
        }

        private static float Read(XElement element, string name, float fallback)
        {
            string value = element.Attribute(name)?.Value;
            return string.IsNullOrEmpty(value) ? fallback : float.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
        }

        // Angle bakes to a direction here rather than in the shader, so the per-pixel path is a dot
        // product. +y is down, so 90 degrees runs top to bottom.
        private static GpuGradient Bake(GradientDefinition definition)
        {
            float radians = definition.angle * MathF.PI / 180f;
            GpuGradient gradient = new GpuGradient
            {
                direction = new Vector2D<float>(MathF.Cos(radians), MathF.Sin(radians)),
                center = new Vector2D<float>(definition.centerX, definition.centerY),
                kind = (uint)definition.kind,
                stopCount = (uint)definition.stops.Count
            };

            for (int i = 0; i < definition.stops.Count; i++)
            {
                GradientStopDefinition stop = definition.stops[i];
                gradient.stops[i] = new GpuGradientStop
                {
                    color = new Vector4D<float>(VulkanControl.HexToRGB(stop.colorHex), stop.alpha),
                    position = stop.position
                };
            }

            return gradient;
        }
    }
}
