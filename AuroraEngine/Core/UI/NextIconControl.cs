using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Registry.Assets;
using ArctisAurora.Core.UISystem.Controls.Text;
using ArctisAurora.EngineWork.Registry;
using Silk.NET.Maths;

namespace ArctisAurora.Core.UI
{
    // One cell of an icon set's MTSDF atlas.
    [A_XSDType("NextIcon", "UI")]
    public class NextIconControl : Control
    {
        private static readonly Core.Diagnostics.LogChannel Log = Core.Diagnostics.LogChannel.For("UI");

        private const int defaultSize = 16;

        public override Control? ActiveContextTarget() => (parent as Control)?.ActiveContextTarget();

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

        public NextIconControl()
        {
            kind = VulkanControlType.MTSDFControl;
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

            sampler = set.textureAsset;

            float k = MathF.Ceiling(MathF.Sqrt(set.metaData.iconCount));
            float cellUV = 1f / k;
            float xOffset = index % k * cellUV;
            float yOffset = MathF.Floor(index / k) * cellUV;

            float texelPad = 1f / set.textureAsset.image.Width;

            SetUVRect(xOffset + texelPad, yOffset + texelPad,
                      xOffset + cellUV - texelPad, yOffset + cellUV - texelPad);

            InvalidateLayout();
        }

        // Width/Height are the artwork, not the cell. The bake pads every cell by an eighth of its
        // inner size per side, and GlyphControl.CellScale is that same margin undone.
        public override Vector2D<float> Measure(Vector2D<float> availableSize)
        {
            float w = (preferredWidth > 0 ? preferredWidth : defaultSize) * GlyphControl.CellScale;
            float h = (preferredHeight > 0 ? preferredHeight : defaultSize) * GlyphControl.CellScale;
            arrange.desired = new Vector2D<float>(w, h);
            SetFlag(ArrangeFlags.MeasureDirty, false);
            return arrange.desired;
        }
    }
}
