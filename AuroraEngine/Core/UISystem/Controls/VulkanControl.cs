using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Data;
using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.Filing.Serialization;
using ArctisAurora.Core.UISystem.Controls.Containers;
using ArctisAurora.EngineWork.Registry;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using ArctisAurora.Core.Registry.Assets;

namespace ArctisAurora.Core.UISystem.Controls
{
    public interface IXMLChild_UI
    { }

    [A_XSDType("VulkanControl", "EntityRegistry", isAbstract: true)]
    public unsafe class VulkanControl : Entity, IXMLParser<VulkanControl>, IXMLChild_UI
    {
        #region ---- STRUCTS ----
        [StructLayout(LayoutKind.Sequential, Pack = 1), A_XSDType("ControlStyle", "AssetRegistry")]
        public struct ControlStyle
        {
            // rgb tint, a opacity
            public Vector4D<float> tint;
            //public Sampler image;
            //public Sampler mask;

            public static ControlStyle Default()
            {
                Dictionary<string, ControlStyle> dStyles = AssetRegistries.GetRegistryByValueType<string, ControlStyle>(typeof(ControlStyle));
                return dStyles.GetValueOrDefault("default", new ControlStyle { tint = new Vector4D<float>(1, 1, 1, 1) });
            }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct QuadUVs
        {
            public Vector2D<float> uv1;
            public Vector2D<float> uv2;
            public Vector2D<float> uv3;
            public Vector2D<float> uv4;

            public QuadUVs(Vector2D<float> uv1, Vector2D<float> uv2, Vector2D<float> uv3, Vector2D<float> uv4)
            {
                this.uv1 = uv1;
                this.uv2 = uv2;
                this.uv3 = uv3;
                this.uv4 = uv4;
            }

            public QuadUVs(Vector2D<float>[] uvs)
            {
                uv1 = uvs[0];
                uv2 = uvs[1];
                uv3 = uvs[2];
                uv4 = uvs[3];
            }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1), A_XSDType("ControlData", "Pools")]
        public struct ControlData()
        {
            public QuadUVs uvs;
            public ControlStyle style;
            public uint textureIndex;
            // clip bounds in design space, as (left, top, right, bottom)
            public Vector4D<float> clip;
            // corner radii in design-space pixels, as (topLeft, topRight, bottomLeft, bottomRight)
            public Vector4D<float> cornerRadius;
            // border band along the rounded silhouette, thickness in design-space pixels
            public Vector3D<float> edgeColor;
            public float edgeThickness;
            // stroke around the mask's own shape, width in screen pixels
            public Vector3D<float> outlineColor;
            public float outlineWidth;
        }

        [A_XSDType("ControlColor", "UI")]
        public enum ControlColor
        {
            red, green, blue, white, black, yellow, cyan, magenta, gray, orange, purple, brown, pink, lime, navy, teal,
        }

        [A_XSDType("FillMode", "UI")]
        public enum ScalingMode
        {
            Uniform, Stretch, Fill, None
        }

        [A_XSDType("HorizontalAlignment", "UI")]
        public enum HorizontalAlignment
        {
            Center, Left, Right, Stretch
        }

        [A_XSDType("VeticalAlignment", "UI")]
        public enum VerticalAlignment
        {
            Top, Center, Bottom, Stretch
        }

        [TypeConverter(typeof(ThicknessConverter))]
        public struct Thickness
        {
            public float top;
            public float right;
            public float bottom;
            public float left;

            public Thickness(float uniform)
            {
                top = right = bottom = left = uniform;
            }

            public Thickness(float horizontal, float vertical)
            {
                left = right = horizontal;
                top = bottom = vertical;
            }

            public Thickness(float top, float right, float bottom, float left)
            {
                this.top = top;
                this.right = right;
                this.bottom = bottom;
                this.left = left;
            }

            public float totalHorizontal => left + right;
            public float totalVertical => top + bottom;

            public static Thickness Zero => new Thickness(0);
        }

        // "8" | "8,4" | "1,2,3,4", one comma-separated value per Thickness constructor.
        public class ThicknessConverter : TypeConverter
        {
            public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
                sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

            public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
            {
                if (value is not string text) return base.ConvertFrom(context, culture, value);

                string[] parts = text.Split(',');
                float[] sides = new float[parts.Length];
                for (int i = 0; i < parts.Length; i++)
                    sides[i] = float.Parse(parts[i].Trim(), culture);

                return parts.Length switch
                {
                    1 => new Thickness(sides[0]),
                    2 => new Thickness(sides[0], sides[1]),
                    4 => new Thickness(sides[0], sides[1], sides[2], sides[3]),
                    _ => throw new FormatException($"Thickness \"{text}\" needs 1, 2 or 4 comma-separated values.")
                };
            }
        }

        [TypeConverter(typeof(CornerRadiiConverter))]
        public struct CornerRadii
        {
            public float topLeft;
            public float topRight;
            public float bottomLeft;
            public float bottomRight;

            public CornerRadii(float uniform)
            {
                topLeft = topRight = bottomLeft = bottomRight = uniform;
            }

            public CornerRadii(float top, float bottom)
            {
                topLeft = topRight = top;
                bottomLeft = bottomRight = bottom;
            }

            public CornerRadii(float topLeft, float topRight, float bottomLeft, float bottomRight)
            {
                this.topLeft = topLeft;
                this.topRight = topRight;
                this.bottomLeft = bottomLeft;
                this.bottomRight = bottomRight;
            }

            public Vector4D<float> AsVector() => new Vector4D<float>(topLeft, topRight, bottomLeft, bottomRight);

            public static CornerRadii Zero => new CornerRadii(0);
        }

        // "8" | "8,4" | "1,2,3,4", one comma-separated value per CornerRadii constructor.
        public class CornerRadiiConverter : TypeConverter
        {
            public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
                sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

            public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
            {
                if (value is not string text) return base.ConvertFrom(context, culture, value);

                string[] parts = text.Split(',');
                float[] corners = new float[parts.Length];
                for (int i = 0; i < parts.Length; i++)
                    corners[i] = float.Parse(parts[i].Trim(), culture);

                return parts.Length switch
                {
                    1 => new CornerRadii(corners[0]),
                    2 => new CornerRadii(corners[0], corners[1]),
                    4 => new CornerRadii(corners[0], corners[1], corners[2], corners[3]),
                    _ => throw new FormatException($"CornerRadii \"{text}\" needs 1, 2 or 4 comma-separated values.")
                };
            }
        }

        public struct LayoutRect
        {
            public float x;
            public float y;
            public float width;
            public float height;

            public LayoutRect(float x, float y, float width, float height)
            {
                this.x = x;
                this.y = y;
                this.width = width;
                this.height = height;
            }

            public LayoutRect(Vector2D<float> position, Vector2D<float> size)
            {
                x = position.X;
                y = position.Y;
                width = size.X;
                height = size.Y;
            }

            public float Right => x + width;
            public float Bottom => y + height;

            public Vector2D<float> Position => new Vector2D<float>(x, y);
            public Vector2D<float> size => new Vector2D<float>(width, height);

            // A rect inset on all sides, clamped so it cannot invert.
            public LayoutRect Shrink(Thickness t) => new LayoutRect(
                x + t.left,
                y + t.top,
                MathF.Max(0, width - t.totalHorizontal),
                MathF.Max(0, height - t.totalVertical)
            );

            public bool Contains(Vector2D<float> point) =>
                point.X >= x && point.X <= Right &&
                point.Y >= y && point.Y <= Bottom;

            public static LayoutRect Intersect(LayoutRect a, LayoutRect b)
            {
                float rx = MathF.Max(a.x, b.x);
                float ry = MathF.Max(a.y, b.y);
                float rr = MathF.Min(a.Right, b.Right);
                float rb = MathF.Min(a.Bottom, b.Bottom);
                return new LayoutRect(rx, ry, MathF.Max(0, rr - rx), MathF.Max(0, rb - ry));
            }

            public static LayoutRect Empty => new LayoutRect(0, 0, 0, 0);
            public static LayoutRect Infinite => new LayoutRect(0, 0, float.MaxValue, float.MaxValue);
        }
        #endregion

        #region ---- UI XML fields ----

        #region ---- scaling ----
        public int width
        {
            get => field;
            set
            {
                if (field == value) return;
                field = value;
                InvalidateLayout();
            }
        } = 72;
        public int height
        {
            get => field;
            set
            {
                if (field == value) return;
                field = value;
                InvalidateLayout();
            }
        } = 72;

        [A_XSDElementProperty("WidthStar", "UI", "Proportional width share when inside a StackPanel (horizontal). 0 = fixed/auto. 1 = equal share, 2 = double share.")]
        public float widthStar = 0f;

        [A_XSDElementProperty("HeightStar", "UI", "Proportional height share when inside a StackPanel (vertical). 0 = fixed/auto. 1 = equal share, 2 = double share.")]
        public float heightStar = 0f;

        public bool IsWidthStar => widthStar > 0f;
        public bool IsHeightStar => heightStar > 0f;

        [A_XSDElementProperty("Width", "UI", "Width in pixels. 0 = auto.")]
        public int preferredWidth
        {
            get => field;
            set
            {
                if (field == value) return;
                field = value; InvalidateLayout();
            }
        } = 0;

        [A_XSDElementProperty("Height", "UI", "Height in pixels. 0 = auto.")]
        public int preferredHeight
        {
            get => field;
            set
            {
                if (field == value) return;
                field = value;
                InvalidateLayout();
            }
        } = 0;

        [A_XSDElementProperty("MinHeight", "UI", "Minimum height in pixels.")]
        public int minHeight = 0;
        [A_XSDElementProperty("MinWidth", "UI", "Minimum width in pixels.")]
        public int minWidth = 0;

        public virtual Vector2D<int> size
        {
            get => new Vector2D<int>(width, height);
            set
            {
                bool changed = width != value.X || height != value.Y;
                width = value.X;
                height = value.Y;
                if (changed) InvalidateLayout();
            }
        }

        [A_XSDElementProperty("Margin", "UI", "Space outside the control in pixels.")]
        public Thickness margin
        {
            get => field;
            set
            {
                field = value;
                InvalidateLayout();
            }
        } = Thickness.Zero;

        [A_XSDElementProperty("Padding", "UI", "Space inside the control in pixels.")]
        public Thickness padding
        {
            get => field;
            set
            { 
                field = value;
                InvalidateLayout();
            }
        } = Thickness.Zero;
        #endregion

        #region ---- positioning ----
        [A_XSDElementProperty("HorizontalPos", "UI", "\"Sets the position of the current control within it's parent. [0;1]. Works with non-container controls.\"")]
        public float horizontalPosition = 0.5f;

        [A_XSDElementProperty("VerticalPos", "UI", "Sets the position of the current control within it's parent. [0;1]. Works with non-container controls.")]
        public float verticalPosition = 0.5f;
        #endregion

        #region ---- settings ----
        [A_XSDElementProperty("HorizontalAlignment", "UI", "How this control fills its parent's horizontal slot.")]
        public HorizontalAlignment horizontalAlignment = HorizontalAlignment.Left;

        [A_XSDElementProperty("VerticalAlignment", "UI", "How this control fills its parent's vertical slot.")]
        public VerticalAlignment verticalAlignment = VerticalAlignment.Top;

        [A_XSDElementProperty("ClipToBounds", "UI", "Will not render or hit-test children outside bounds.")]
        public bool clipOutOfBounds = false;

        [A_XSDElementProperty("DockMode", "UI", "Sets the control's dock mode to the desired setting. Fill - fills the entire area.")]
        public DockMode dockMode;

        [A_XSDElementProperty("Grid.Column", "UI", "If present in a grid sets the control's grid column.")]
        public int gridColumn = 0;

        [A_XSDElementProperty("Grid.Row", "UI", "If present in a grid sets the control's grid row.")]
        public int gridRow = 0;

        [A_XSDElementProperty("DraggingOpacity", "UI", "Opacity of this control's drag preview. Negative uses the UI setting.")]
        public float draggingOpacity = -1f;
        #endregion

        #region ---- styling ----
        [A_XSDElementProperty("ColorHex", "UI", "Sets the control color via hex code.")]
        public virtual string controlColorHex
        {
            get => field;
            set
            {
                field = value;
                Vector3D<float> rgb = HexToRGB(value);
                controlData.style.tint = new Vector4D<float>(rgb, controlData.style.tint.W);
                //isDirty = true;
                UpdateControlData();
            }
        } = "#FFFFFF";

        [A_XSDElementProperty("Alpha", "UI", "Opacity of the control, 0 to 1. Multiplies the coverage its mask already carries.")]
        public float alpha
        {
            get => field;
            set
            {
                field = value;
                controlData.style.tint.W = value;
                UpdateControlData();
            }
        } = 1f;

        [A_XSDElementProperty("ControlColor", "UI", "Sets the color of the control.")]
        public ControlColor controlColor
        {
            get => field;
            set
            {
                string hex = EnumColorToHex(value);
                Vector3D<float> rgb = HexToRGB(hex);
                controlData.style.tint = new Vector4D<float>(rgb, controlData.style.tint.W);
                UpdateControlData();
                field = value;
                controlColorHex = hex;
            }
        }

        [A_XSDElementProperty("CornerRadius", "UI", "Rounds the control's corners, in design-space pixels. \"8\", \"top,bottom\" or \"topLeft,topRight,bottomLeft,bottomRight\".")]
        public CornerRadii cornerRadius
        {
            get => field;
            set
            {
                field = value;
                controlData.cornerRadius = value.AsVector();
                UpdateControlData();
            }
        }

        [A_XSDElementProperty("EdgeColorHex", "UI", "Sets the control's border color via hex code. Needs EdgeThickness to show.")]
        public string edgeColorHex
        {
            get => field;
            set
            {
                field = value;
                controlData.edgeColor = HexToRGB(value);
                UpdateControlData();
            }
        } = "#000000";

        [A_XSDElementProperty("EdgeThickness", "UI", "Border width in design-space pixels, drawn inward from the control's edge. Zero draws none.")]
        public float edgeThickness
        {
            get => field;
            set
            {
                field = value;
                controlData.edgeThickness = value;
                UpdateControlData();
            }
        }

        [A_XSDElementProperty("OutlineColorHex", "UI", "Sets the outline color of the control's mask shape via hex code. Needs OutlineWidth to show.")]
        public string outlineColorHex
        {
            get => field;
            set
            {
                field = value;
                controlData.outlineColor = HexToRGB(value);
                UpdateControlData();
            }
        } = "#000000";

        [A_XSDElementProperty("OutlineWidth", "UI", "Outline width in screen pixels, stroked outward from the mask's shape. Zero draws none.")]
        public float outlineWidth
        {
            get => field;
            set
            {
                field = value;
                controlData.outlineWidth = value;
                UpdateControlData();
            }
        }
        #endregion

        #endregion

        #region ---- rendering ----
        // A ref into the UIControls pool's ControlData column; every write ends in UpdateControlData().
        public ref ControlData controlData => ref Pool.GetRef<ControlData>(dataHandle);

        public TextureAsset maskAsset
        {
            get => field;
            set
            {
                field = value;
                controlData.textureIndex = value?.textureIndex ?? 0;
                UpdateControlData();
            }
        }

        public Sampler colorSampler;
        public TextureAsset colorAsset = null!;
        #endregion

        #region ---- EVENTS ----
        //fuck do i do with this yet to figure out. tbh idk if this is even a problem
        public event Action<Vector2D<float>>? hover;
        [A_XSDElementProperty("onEnter", "UI")]
        public Action? onEnter;
        [A_XSDElementProperty("BubbleEnter", "UI")]
        public bool bubbleEnter = false;

        [A_XSDElementProperty("onExit", "UI")]
        public Action? onExit;
        [A_XSDElementProperty("BubbleExit", "UI")]
        public bool bubbleExit = false;

        [A_XSDElementProperty("onClick", "UI")]
        public Action? onClick;
        [A_XSDElementProperty("BubbleClick", "UI")]
        public bool bubbleClick = false;

        [A_XSDElementProperty("onAltClick", "UI")]
        public Action? onAltClick;
        [A_XSDElementProperty("BubbleAltClick", "UI")]
        public bool bubbleAltClick = false;

        public Action? onDoubleClick;
        public bool bubbleDoubleClick = false;

        [A_XSDElementProperty("onRelease", "UI")]
        public Action? onRelease;
        [A_XSDElementProperty("BubbleRelease", "UI")]
        public bool bubbleRelease = false;

        [A_XSDElementProperty("onAltRelease", "UI")]
        public Action? onAltRelease;
        [A_XSDElementProperty("BubbleAltRelease", "UI")]
        public bool bubbleAltRelease = false;

        public Action<Vector2D<float>, Vector2D<float>>? onDrag;
        [A_XSDElementProperty("onDragStop", "UI")]
        public Action? onDragStop;

        [A_XSDElementProperty("onScrollUp", "UI")]
        public Action? onScrollUp;
        [A_XSDElementProperty("onScrollDown", "UI")]
        public Action? onScrollDown;
        [A_XSDElementProperty("BubbleScroll", "UI")]
        public bool bubbleScroll = false;

        private DateTime lastClick = DateTime.Now;

        // Decoration drawn inside a control that owns the interaction — a caret, a selection box.
        // It has to be skipped by the hit-test rather than left to swallow the click, since the
        // deepest hit wins and a decoration sits over the thing the pointer is actually aiming at.
        public bool hitTestable = true;

        // False hands the active context to the parent instead.
        public virtual bool canBeActiveContext => true;

        public bool HitTest(Vector2D<float> point) => ClipRect.Contains(point);
        #endregion

        // EXTRAS
        [A_XSDElementProperty("ContextMenu", "UI", "Menus in ContextMenus.xml this control offers on right click, comma separated.")]
        public string contextMenus = "";

        #region ---- Layout State ----
        public Vector2D<float> DesiredSize { get; protected set; }
        public LayoutRect arrangedRect { get; protected set; }

        // Every assignment mirrors into the pool row the fragment shader discards against.
        public LayoutRect ClipRect
        {
            get => field;
            protected set
            {
                field = value;
                controlData.clip = new Vector4D<float>(value.x, value.y, value.Right, value.Bottom);
                UpdateControlData();
            }
        }

        public bool isMeasureDirty { get => field; internal set => field = value; } = true;
        public bool isArrangeDirty { get => field; internal set => field = value; } = true;

        public void InvalidateLayout()
        {
            if (isMeasureDirty) return;
            isMeasureDirty = true;
            isArrangeDirty = true;
            VulkanControl current = parent as VulkanControl;
            VulkanControl topDirty = this;
            while (current != null)
            {
                if (current.isMeasureDirty) return;
                current.isMeasureDirty = true;
                current.isArrangeDirty = true;
                topDirty = current;
                current = current.parent as VulkanControl;
            }
            UILayout.RegisterDirtyRoot(topDirty);
        }

        public void InvalidateArrange()
        {
            if (isArrangeDirty) return;
            isArrangeDirty = true;
            VulkanControl current = parent as VulkanControl;
            VulkanControl topDirty = this;
            while (current != null)
            {
                if (current.isArrangeDirty) return;
                current.isArrangeDirty = true;
                topDirty = current;
                current = current.parent as VulkanControl;
            }
            UILayout.RegisterDirtyRoot(topDirty);
        }

        // Degenerate rect — contains no point at all.
        private static readonly LayoutRect hiddenClip = new LayoutRect(0, 0, -1, -1);

        public bool hidden { get; private set; }

        // Takes the subtree out of the draw and the hit-test.
        public void Hide()
        {
            if (hidden) return;
            hidden = true;
            CollapseClip(this);
        }

        // The next Arrange rewrites the subtree's clips.
        public void Show()
        {
            if (!hidden) return;
            hidden = false;
            InvalidateLayout();
        }

        private static void CollapseClip(VulkanControl control)
        {
            control.ClipRect = hiddenClip;
            foreach (Entity child in control.children)
                if (child is VulkanControl childControl)
                    CollapseClip(childControl);
        }
        #endregion

        #region ---- Layout API (two-pass) ----
        public virtual Vector2D<float> Measure(Vector2D<float> availableSize)
        {
            float w = preferredWidth > 0 ? preferredWidth : MathF.Max(minWidth, availableSize.X);
            float h = preferredHeight > 0 ? preferredHeight : MathF.Max(minHeight, availableSize.Y); 
            if (children.Count == 1 && children[0] is VulkanControl childControl)
            {
                Vector2D<float> childDesired = childControl.Measure(new Vector2D<float>(
                    MathF.Max(0, w - padding.totalHorizontal),
                    MathF.Max(0, h - padding.totalVertical)));
                if (preferredWidth == 0) w = childDesired.X + padding.totalHorizontal;
                if (preferredHeight == 0) h = childDesired.Y + padding.totalVertical;
            }
            DesiredSize = new Vector2D<float>(w, h);
            isMeasureDirty = false;
            return DesiredSize;
        }

        public virtual void Arrange(LayoutRect finalRect)
        {
            arrangedRect = finalRect;
            WriteArrangedTransform(finalRect);
            if (parent is VulkanControl parentControl)
                ClipRect = clipOutOfBounds
                    ? LayoutRect.Intersect(finalRect, parentControl.ClipRect)
                    : parentControl.ClipRect;
            else
                ClipRect = finalRect;
            if (children.Count == 1 && children[0] is VulkanControl child)
            {
                LayoutRect innerRect = finalRect.Shrink(padding);
                LayoutRect childRect = innerRect.Shrink(child.margin);
                float cx = childRect.x + (childRect.width - child.DesiredSize.X) * child.horizontalPosition;
                float cy = childRect.y + (childRect.height - child.DesiredSize.Y) * child.verticalPosition;
                child.Arrange(new LayoutRect(cx, cy, child.DesiredSize.X, child.DesiredSize.Y));
            }
            isArrangeDirty = false;
        }
        #endregion

        #region ---- data pool ----
        // Transform and ControlData both live in the "UIControls" pool.
        protected override string PoolName => "UIControls";

        // Writes an arranged rect into the pooled transform, z-biased just above the parent.
        protected void WriteArrangedTransform(LayoutRect finalRect)
        {
            float z = parent is VulkanControl pc
                ? pc.transform.position.Z + 0.001f
                : transform.position.Z;
            ref TransformData t = ref transform;
            t.position = new Vector3D<float>(
                finalRect.x + finalRect.width / 2f,
                finalRect.y + finalRect.height / 2f,
                z);
            t.scale = new Vector3D<float>(finalRect.width, finalRect.height, 1);
            CommitTransform();
        }

        // Bakes the pooled transform into its GpuTransform row. Every transform write must end here.
        protected void CommitTransform()
        {
            ref TransformData t = ref transform;
            Matrix4X4<float> m = Matrix4X4<float>.Identity;
            m *= Matrix4X4.CreateScale(t.scale);
            m *= Matrix4X4.CreateTranslation(t.position);

            Pool.GetRef<GpuTransform>(dataHandle).matrix = m;
            Pool.MarkContentDirty(dataHandle);
        }
        #endregion

        public VulkanControl()
        {
            controlData = new ControlData();
            controlData.style = ControlStyle.Default();
            controlData.uvs = new QuadUVs();
            ClipRect = LayoutRect.Infinite;
            UpdateControlData();

            maskAsset = AssetRegistries.GetAsset<TextureAsset>("default");

            EntityRegistry.AddToGroup("Controls", this);

            CommitTransform();
            InvalidateLayout();
        }

        public override void OnStart()
        {
            base.OnStart();
        }

        // Runs before the pool row is freed, so the contexts drop this control while it is still
        // readable.
        public override void OnDestroy()
        {
            UICollisionHandling.Forget(this);
            base.OnDestroy();
        }

        // Publishes a ControlData edit by widening the pool's dirty range.
        internal void UpdateControlData() => Pool.MarkContentDirty(dataHandle);

        // Flags the pool for a resequence at the frame edge. Inserts only — removals never need one.
        protected void MarkTreeOrderDirty() => Pool.MarkOrderDirty();

        public override void AddChild(Entity entity)
        {
            if (entity is not VulkanControl control)
                throw new Exception("Child entity must be a VulkanControl");
            if (children.Count > 0)
                throw new Exception("Plain VulkanControl supports only one child. Use a container control for multiple children.");
            entity.parent = this;
            children.Add(entity);
            MarkTreeOrderDirty();
            InvalidateLayout();
        }

        public override void RemoveChild(Entity entity)
        {
            base.RemoveChild(entity);
            MarkTreeOrderDirty();
            InvalidateLayout();
        }

        // A control's children are always controls.
        public override VulkanControl FindByName(string querryName) => (VulkanControl)base.FindByName(querryName);

        #region size_setters
        public virtual void SetSize(Vector2D<float> size)
        {
            this.size = (Vector2D<int>)size;
        }
        public virtual void SetSize(Vector2D<int> size)
        {
            this.size = size;
        }
        public virtual void SetWidth(int x)
        {
            width = x;
        }
        public virtual void SetHeight(int y)
        {
            height = y;
        }
        #endregion


        #region mouse_events
        // HOVER
        public void RegisterHover(Action<Vector2D<float>> action) => hover += action;
        public void ResolveHover(Vector2D<float> pos) => hover?.Invoke(pos);

        public void RegisterOnEnter(Action action) => onEnter += action;
        public virtual void ResolveOnEnter()
        {
            onEnter?.Invoke();
            if (bubbleEnter && parent is VulkanControl parentControl)
                parentControl.ResolveOnEnter();
        }

        public void RegisterOnExit(Action action) => onExit += action;
        public virtual void ResolveExit()
        {
            onExit?.Invoke();
            if (bubbleExit && parent is VulkanControl parentControl)
                parentControl.ResolveExit();
        }

        public void RegisterOnDrag(Action<Vector2D<float>, Vector2D<float>> action) => onDrag += action;
        public virtual void ResolveDrag(Vector2D<float> lastPos, Vector2D<float> delta) => onDrag?.Invoke(lastPos, delta);

        // Claims the drag, so ResolveDrag runs every tick until the button comes up. Opt-in from a
        // click handler rather than automatic on press — the deepest hit is a glyph, and what wants
        // the drag is whatever above it knows what dragging means.
        public void StartDrag() => UICollisionHandling.SetDragging(this);

        public virtual void RegisterDragStop(Action action) => onDragStop += action;
        public virtual void StopDrag() => onDragStop?.Invoke();

        // A drag was released over this control at a point in design space. False means "not mine"
        // and the offer walks up.
        public virtual bool ResolveDrop(VulkanControl dropped, Vector2D<float> point) => false;

        // The same offer while the button is still down, so the target can show where it would land.
        // Answered by whoever would take the drop, and walked up the same way.
        public virtual bool ResolveDropHint(VulkanControl dropped, Vector2D<float> point) => false;

        public virtual void ClearDropHint() { }

        // Entries this control adds to its own menu, on top of whatever ContextMenu names. Add-only
        // — conditional entries belong here, static ones in the XML.
        public virtual void BuildContextMenu(ContextMenuBuilder menu) { }

        // Opens this control's menu, or hands the right click up when it has nothing to offer. The
        // walk is unconditional: bubbleAltClick gates the callback, not who owns the menu.
        public virtual void OpenContextMenu()
        {
            if (ContextMenuWindow.Open(this)) return;
            (parent as VulkanControl)?.OpenContextMenu();
        }

        public void RegisterOnClick(Action action) => onClick += action;
        public virtual void ResolveOnClick(Vector2D<float> oldPos, Vector2D<float> delta)
        {
            onClick?.Invoke();
            if (bubbleClick && parent is VulkanControl parentControl)
            {
                parentControl.ResolveOnClick(oldPos, delta);
            }
        }

        public void RegisterOnDoubleClick(Action action) => onDoubleClick += action;
        public virtual void ResolveOnDoubleClick()
        {
            onDoubleClick?.Invoke();
            if (bubbleDoubleClick && parent is VulkanControl parentControl)
                parentControl.ResolveOnDoubleClick();
        }

        public void RegisterOnRelease(Action action) => onRelease += action;
        public virtual void ResolveOnRelease()
        {
            onRelease?.Invoke();
            if (bubbleRelease && parent is VulkanControl parentControl)
                parentControl.ResolveOnRelease();
        }

        public void RegisterOnAltClick(Action action) => onAltClick += action;
        public virtual void ResolveOnAltClick()
        {
            onAltClick?.Invoke();
            if (bubbleAltClick && parent is VulkanControl parentControl)
                parentControl.ResolveOnAltClick();
        }

        public void RegisterOnAltRelease(Action action) => onAltRelease += action;
        public virtual void ResolveOnAltRelease()
        {
            onAltRelease?.Invoke();
            if (bubbleAltRelease && parent is VulkanControl parentControl)
                parentControl.ResolveOnAltRelease();
        }

        public void RegisterOnScrollUp(Action action) => onScrollUp += action;
        public void RegisterOnScrollDown(Action action) => onScrollDown += action;

        public virtual bool ResolveOnScrollUp()
        {
            if (onScrollUp != null)
            {
                onScrollUp.Invoke();
                return true;
            }
            return false;
        }

        public virtual bool ResolveOnScrollDown()
        {
            if (onScrollDown != null)
            {
                onScrollDown.Invoke();
                return true;
            }
            return false;
        }

        public void BubbleAll()
        {
            bubbleClick = true;
            bubbleAltClick = true;
            bubbleDoubleClick = true;
            bubbleRelease = true;
            bubbleAltRelease = true;
            bubbleScroll = true;
            bubbleEnter = true;
            bubbleExit = true;
        }
        #endregion


        public static string EnumColorToHex(ControlColor color)
        {
            return color switch
            {
                ControlColor.red => "#FF0000",
                ControlColor.green => "#00FF00",
                ControlColor.blue => "#0000FF",
                ControlColor.white => "#FFFFFF",
                ControlColor.black => "#000000",
                ControlColor.yellow => "#FFFF00",
                ControlColor.cyan => "#00FFFF",
                ControlColor.magenta => "#FF00FF",
                ControlColor.gray => "#808080",
                ControlColor.orange => "#FFA500",
                ControlColor.purple => "#800080",
                ControlColor.brown => "#A52A2A",
                ControlColor.pink => "#FFC0CB",
                ControlColor.lime => "#00FF00",
                ControlColor.navy => "#000080",
                ControlColor.teal => "#008080",
                _ => "#FFFFFF",
            };
        }

        public static Vector3D<float> HexToRGB(string hex)
        {
            if (hex.StartsWith("#"))
            {
                hex = hex[1..];
            }
            if (hex.Length != 6)
            {
                throw new ArgumentException("Hex color must be 6 characters long.");
            }
            byte r = Convert.ToByte(hex.Substring(0, 2), 16);
            byte g = Convert.ToByte(hex.Substring(2, 2), 16);
            byte b = Convert.ToByte(hex.Substring(4, 2), 16);
            return new Vector3D<float>(r / 255f, g / 255f, b / 255f);
        }

        public override void Invalidate()
        {
            base.Invalidate();
            foreach (Entity child in children)
            {
                child.Invalidate();
            }
        }

        #region ---- XML ----
        public static VulkanControl ParseXML(string xmlName)
        {
            string path = Paths.Doc(xmlName);
            XDocument doc = XDocument.Load(path);
            XElement root = doc.Root;
            WindowControl window = new WindowControl();
            ResolveAttributes(root, window);

            window.arrangedRect = new LayoutRect(0, 0, window.preferredWidth, window.preferredHeight);
            UILayout.RegisterDirtyRoot(window);
            ref TransformData wt = ref window.transform;
            wt.position = new Vector3D<float>(window.preferredWidth / 2f, window.preferredHeight / 2f, wt.position.Z);
            wt.scale = new Vector3D<float>(window.preferredWidth, window.preferredHeight, 1);
            window.CommitTransform();
            RecursiveParse(root, window);

            return window;
        }

        private static void RecursiveParse(XElement root, VulkanControl topControl)
        {
            foreach (var element in root.Elements())
            {
                Type type = AnyXMLType.FindType(element.Name.LocalName);
                var control = Activator.CreateInstance(type);
                ResolveAttributes(element, control);
                if (!typeof(VulkanControl).IsAssignableFrom(type))
                {
                    FieldInfo field = topControl.GetType()
                        .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        .FirstOrDefault(f => f.FieldType.IsGenericType &&
                                             f.FieldType.GetGenericTypeDefinition() == typeof(List<>)
                                             && f.FieldType.GetGenericArguments()[0].IsAssignableFrom(control.GetType()));
                    IList list = (IList)field.GetValue(topControl);

                    list.Add(control);
                    continue;
                }
                topControl.AddChild((VulkanControl)control);
                RecursiveParse(element, (VulkanControl)control);
            }
        }

        private static void ResolveAttributes(XElement root, object topControl)
        {
            foreach (XAttribute attr in root.Attributes())
            {
                var prop = topControl.GetType().GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase).FirstOrDefault(m =>
                {
                    var a = m.GetCustomAttributes(typeof(A_XSDElementPropertyAttribute), true).FirstOrDefault() as A_XSDElementPropertyAttribute;
                    if (a != null)
                    {
                        return string.Equals(a.Name, attr.Name.LocalName, StringComparison.OrdinalIgnoreCase);
                    }
                    return false;
                });

                if (prop != null)
                {
                    Type memberType = prop.MemberType == MemberTypes.Field ? ((FieldInfo)prop).FieldType : ((PropertyInfo)prop).PropertyType;
                    if (memberType == typeof(Action))
                    {
                        MethodInfo? methodInfo = AppDomain.CurrentDomain.GetAssemblies()
                        .SelectMany(a => a.GetTypes())
                        .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                        .FirstOrDefault(m =>
                        {
                            A_XSDActionDependencyAttribute actionDep = m.GetCustomAttribute<A_XSDActionDependencyAttribute>();
                            return actionDep != null && string.Equals(actionDep.Name, attr.Value, StringComparison.OrdinalIgnoreCase);
                        });

                        if (methodInfo == null)
                            throw new Exception($"Action method '{attr.Value}' not found in A_XSDActionDependency.");

                        Action actionDelegate = (Action)Delegate.CreateDelegate(typeof(Action), methodInfo);
                        if (prop is PropertyInfo propertyInfo)
                        {
                            Action current = (Action?)propertyInfo.GetValue(topControl);
                            current += actionDelegate;
                            propertyInfo.SetValue(topControl, current);
                            continue;
                        }
                        if(prop is FieldInfo fieldInfo)
                        {
                            Action current = (Action?)fieldInfo.GetValue(topControl);
                            current += actionDelegate;
                            fieldInfo.SetValue(topControl, current);
                            continue;
                        }
                    }
                    else if (memberType.IsEnum)
                    {
                        if (prop is PropertyInfo propertyInfo)
                        {
                            object enumValue = Enum.Parse(propertyInfo.PropertyType, attr.Value);
                            propertyInfo.SetValue(topControl, enumValue);
                            continue;
                        }
                        if (prop is FieldInfo fieldInfo)
                        {
                            object enumValue = Enum.Parse(fieldInfo.FieldType, attr.Value);
                            fieldInfo.SetValue(topControl, enumValue);
                            continue;
                        }
                        continue;
                    }
                    else
                    {
                        if (prop is PropertyInfo propertyInfo)
                        {
                            object value = TypeDescriptor.GetConverter(propertyInfo.PropertyType).ConvertFromInvariantString(attr.Value);
                            propertyInfo.SetValue(topControl, value);
                            continue;
                        }
                        if (prop is FieldInfo fieldInfo)
                        {
                            object value = TypeDescriptor.GetConverter(fieldInfo.FieldType).ConvertFromInvariantString(attr.Value);
                            fieldInfo.SetValue(topControl, value);
                            continue;
                        }
                    }
                }
                if (topControl.GetType() == typeof(VulkanControl))
                {
                    ((VulkanControl)topControl).UpdateControlData();
                }
            }
        }
        #endregion
    }
}