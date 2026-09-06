using ArctisAurora.Core.Data;
using ArctisAurora.Core.ECS.EngineEntity;
using Silk.NET.Maths;

namespace ArctisAurora.Core.UI
{
    // One UI element: an ArrangeData row in UIElements and the VulkanControl row it draws in
    // VulkanControls. The draw row becomes a range at landing 4, when a text run starts emitting
    // one per glyph. No transform — the baked matrix lives in ControlGeometry.
    public class Control : Entity
    {
        // The camera's ortho box is z in [-512, -0.01], so a root sits at -10 and depth steps toward
        // the near plane from there. Matches what the outgoing stack writes.
        internal const float rootDepth = -10f;

        protected override string PoolName => "UIElements";

        internal DataHandle controlHandle;

        public ref ArrangeData arrange => ref Pool.GetRef<ArrangeData>(dataHandle);
        public ref ControlGeometry geometry => ref UIEngine.Controls.GetRef<ControlGeometry>(controlHandle);
        public ref VulkanControl visual => ref UIEngine.Controls.GetRef<VulkanControl>(controlHandle);

        protected override void AllocatePooledData()
        {
            base.AllocatePooledData();
            controlHandle = AllocateIn("VulkanControls");
        }

        public Control()
        {
            visual.type = VulkanControlType.PanelControl;
            visual.tint = new Vector4D<float>(1, 1, 1, 1);
            Publish();
        }

        #region ---- paint ----
        public string colorHex
        {
            get => field;
            set
            {
                field = value;
                Vector3D<float> rgb = HexToRGB(value);
                visual.tint = new Vector4D<float>(rgb, visual.tint.W);
                Publish();
            }
        } = "#FFFFFF";

        public float alpha
        {
            get => field;
            set
            {
                field = value;
                visual.tint.W = value;
                Publish();
            }
        } = 1f;

        public float cornerRadius
        {
            get => field;
            set
            {
                field = value;
                visual.cornerRadius = new Vector4D<float>(value, value, value, value);
                Publish();
            }
        }

        public string edgeColorHex
        {
            get => field;
            set
            {
                field = value;
                visual.edgeColor = HexToRGB(value);
                Publish();
            }
        } = "#000000";

        public float edgeThickness
        {
            get => field;
            set
            {
                field = value;
                visual.edgeThickness = value;
                Publish();
            }
        }
        #endregion

        // Writes the arranged rect into the GPU geometry row. Replaced by the arrange pass at landing 2.
        public void SetArranged(LayoutRect rect)
        {
            arrange.arranged = rect;
            arrange.clip = rect;

            Matrix4X4<float> m = Matrix4X4<float>.Identity;
            m *= Matrix4X4.CreateScale(rect.width, rect.height, 1f);
            m *= Matrix4X4.CreateTranslation(rect.x + rect.width * 0.5f, rect.y + rect.height * 0.5f, rootDepth);

            ref ControlGeometry g = ref geometry;
            g.matrix = m;
            g.clip = new Vector4D<float>(rect.x, rect.y, rect.Right, rect.Bottom);
            g.gradientRect = g.clip;
            Publish();
        }

        // Widens the draw pool's dirty range so this row is re-uploaded.
        internal void Publish() => UIEngine.Controls.MarkContentDirty(controlHandle);

        public static Vector3D<float> HexToRGB(string hex)
        {
            if (hex.StartsWith("#")) hex = hex[1..];
            if (hex.Length != 6)
                throw new ArgumentException("Hex color must be 6 characters long.");

            byte r = Convert.ToByte(hex.Substring(0, 2), 16);
            byte g = Convert.ToByte(hex.Substring(2, 2), 16);
            byte b = Convert.ToByte(hex.Substring(4, 2), 16);
            return new Vector3D<float>(r / 255f, g / 255f, b / 255f);
        }
    }
}
