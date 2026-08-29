using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Registry.Assets;
using ArctisAurora.Core.UISystem.Controls.Text;
using ArctisAurora.EngineWork.Registry;
using Silk.NET.Maths;

namespace ArctisAurora.Core.UISystem.Controls
{
    // One cell of an icon set's MTSDF atlas. Tint, outline and edge all work as they do on a glyph,
    // because the shader cannot tell the two apart.
    [A_XSDType("Icon", "UI")]
    public class IconControl : VulkanControl
    {
        private static readonly Core.Diagnostics.LogChannel Log = Core.Diagnostics.LogChannel.For("UI");

        private const int defaultSize = 16;

        public override bool canBeActiveContext => false;

        [A_XSDElementProperty("Set", "UI", "Icon set to draw from, as named in the asset manifest.")]
        public string setName
        {
            get => field;
            set { field = value; Rebind(); }
        } = "default";

        [A_XSDElementProperty("Icon", "UI", "Icon within the set — the source .svg's file name.")]
        public string iconName
        {
            get => field;
            set { field = value; Rebind(); }
        } = "";

        public IconControl()
        {
            BubbleAll();
        }

        // Both attributes arrive one at a time during an XML parse, so this runs on each and does
        // nothing until there is a pair to look up.
        private void Rebind()
        {
            if (string.IsNullOrEmpty(setName) || string.IsNullOrEmpty(iconName)) return;

            IconSetAsset set = AssetRegistries.GetAsset<IconSetAsset>(setName);
            (_, int index) = set.metaData.GetIconAndIndex(iconName);
            if (index < 0)
            {
                Log.Warn($"icon '{iconName}' is not in set '{setName}'.");
                return;
            }

            maskAsset = set.textureAsset;

            float k = MathF.Ceiling(MathF.Sqrt(set.metaData.iconCount));
            float cellUV = 1f / k;
            float xOffset = index % k * cellUV;
            float yOffset = MathF.Floor(index / k) * cellUV;

            float texelPad = 1f / set.textureAsset.image.Width;

            float u0 = xOffset + texelPad;
            float v0 = yOffset + texelPad;
            float u1 = xOffset + cellUV - texelPad;
            float v1 = yOffset + cellUV - texelPad;

            controlData.uvs.uv1 = new Vector2D<float>(u1, v1);
            controlData.uvs.uv2 = new Vector2D<float>(u0, v0);
            controlData.uvs.uv3 = new Vector2D<float>(u0, v1);
            controlData.uvs.uv4 = new Vector2D<float>(u1, v0);
            UpdateControlData();

            InvalidateLayout();
        }

        // Width/Height are the artwork, not the cell. The bake pads every cell by an eighth of its
        // inner size per side, and GlyphControl.CellScale is that same margin undone.
        public override Vector2D<float> Measure(Vector2D<float> availableSize)
        {
            float w = (preferredWidth > 0 ? preferredWidth : defaultSize) * GlyphControl.CellScale;
            float h = (preferredHeight > 0 ? preferredHeight : defaultSize) * GlyphControl.CellScale;
            DesiredSize = new Vector2D<float>(w, h);
            isMeasureDirty = false;
            return DesiredSize;
        }
    }
}
