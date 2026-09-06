using ArctisAurora.Core.Data;
using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Registry.Assets;
using ArctisAurora.Core.UISystem;
using Silk.NET.Maths;

namespace ArctisAurora.Core.UI
{
    // One UI element: an ArrangeData row in UIElements and the VulkanControl rows it draws in
    // VulkanControls. Every control owns rows[0]; a text run appends one per glyph. No transform —
    // the baked matrix lives in ControlGeometry.
    [A_XSDType("NextVulkanControl", "EntityRegistry", isAbstract: true)]
    public partial class Control : Entity
    {
        // The camera's ortho box is z in [-512, -0.01], so a root sits at -10 and depth steps toward
        // the near plane from there. Matches what the outgoing stack writes.
        internal const float rootDepth = -10f;
        internal const float depthStep = 0.001f;

        protected override string PoolName => "UIElements";

        // draw rows, in the order the DFS walk emits them
        internal DataHandle[] rows = null!;

        public ref ArrangeData arrange => ref Pool.GetRef<ArrangeData>(dataHandle);
        public ref ControlGeometry geometry => ref GeometryAt(0);
        public ref VulkanControl visual => ref VisualAt(0);

        public ref ControlGeometry GeometryAt(int row) => ref UIEngine.Controls.GetRef<ControlGeometry>(rows[row]);
        public ref VulkanControl VisualAt(int row) => ref UIEngine.Controls.GetRef<VulkanControl>(rows[row]);

        protected override void AllocatePooledData()
        {
            base.AllocatePooledData();
            rows = new[] { AllocateIn("VulkanControls") };
        }

        // Appends a draw row and returns its index. The pool resequences to DFS order at the frame
        // edge, so the new row lands beside the others whatever dense index it took.
        protected int AllocateRow()
        {
            Array.Resize(ref rows, rows.Length + 1);
            rows[^1] = AllocateIn("VulkanControls");
            MarkTreeOrderDirty();
            return rows.Length - 1;
        }

        // Drops the last count rows. The pool free is deferred to the frame edge; the handle leaves
        // this array now, so the resequence that runs after it never names a dead row.
        protected void TrimRows(int count)
        {
            if (count <= 0) return;

            for (int i = rows.Length - count; i < rows.Length; i++)
                FreeIn(rows[i]);

            Array.Resize(ref rows, rows.Length - count);
            MarkTreeOrderDirty();
        }

        public Control()
        {
            visual.type = VulkanControlType.PanelControl;
            visual.tint = new Vector4D<float>(1, 1, 1, 1);
            visual.textureIndex = VulkanControl.noTexture;
            SetUVRect(0f, 0f, 1f, 1f);

            ref ArrangeData a = ref arrange;
            a.horizontalPosition = 0.5f;
            a.verticalPosition = 0.5f;
            a.horizontalAlignment = (byte)HorizontalAlignment.Left;
            a.verticalAlignment = (byte)VerticalAlignment.Top;
            a.flags = (byte)(ArrangeFlags.MeasureDirty | ArrangeFlags.ArrangeDirty);

            ClipRect = LayoutRect.Infinite;
            Publish();
            UIEngine.RegisterDirtyRoot(this);
        }

        #region ---- authored layout ----
        [A_XSDElementProperty("Width", "UI", "Width in pixels. 0 = auto.")]
        public float preferredWidth
        {
            get => arrange.preferredWidth;
            set { if (Set(ref arrange.preferredWidth, value)) InvalidateLayout(); }
        }

        [A_XSDElementProperty("Height", "UI", "Height in pixels. 0 = auto.")]
        public float preferredHeight
        {
            get => arrange.preferredHeight;
            set { if (Set(ref arrange.preferredHeight, value)) InvalidateLayout(); }
        }

        [A_XSDElementProperty("MinWidth", "UI", "Minimum width in pixels.")]
        public float minWidth
        {
            get => arrange.minWidth;
            set { if (Set(ref arrange.minWidth, value)) InvalidateLayout(); }
        }

        [A_XSDElementProperty("MinHeight", "UI", "Minimum height in pixels.")]
        public float minHeight
        {
            get => arrange.minHeight;
            set { if (Set(ref arrange.minHeight, value)) InvalidateLayout(); }
        }

        [A_XSDElementProperty("WidthStar", "UI", "Proportional width share inside a horizontal stack. 0 = fixed/auto.")]
        public float widthStar
        {
            get => arrange.widthStar;
            set { if (Set(ref arrange.widthStar, value)) InvalidateLayout(); }
        }

        [A_XSDElementProperty("HeightStar", "UI", "Proportional height share inside a vertical stack. 0 = fixed/auto.")]
        public float heightStar
        {
            get => arrange.heightStar;
            set { if (Set(ref arrange.heightStar, value)) InvalidateLayout(); }
        }

        public bool IsWidthStar => arrange.widthStar > 0f;
        public bool IsHeightStar => arrange.heightStar > 0f;

        // The size a control is given outright, where preferredWidth/Height are what it asks for.
        public float width
        {
            get => arrange.width;
            set { if (Set(ref arrange.width, value)) InvalidateLayout(); }
        }

        public float height
        {
            get => arrange.height;
            set { if (Set(ref arrange.height, value)) InvalidateLayout(); }
        }

        public virtual Vector2D<float> size
        {
            get => new Vector2D<float>(arrange.width, arrange.height);
            set
            {
                ref ArrangeData a = ref arrange;
                bool changed = a.width != value.X || a.height != value.Y;
                a.width = value.X;
                a.height = value.Y;
                if (changed) InvalidateLayout();
            }
        }

        public virtual void SetSize(Vector2D<float> size) => this.size = size;
        public virtual void SetWidth(float x) => width = x;
        public virtual void SetHeight(float y) => height = y;

        [A_XSDElementProperty("Margin", "UI", "Space outside the control in pixels.")]
        public Thickness margin
        {
            get => arrange.margin;
            set { arrange.margin = value; InvalidateLayout(); }
        }

        [A_XSDElementProperty("Padding", "UI", "Space inside the control in pixels.")]
        public Thickness padding
        {
            get => arrange.padding;
            set { arrange.padding = value; InvalidateLayout(); }
        }

        [A_XSDElementProperty("HorizontalAlignment", "UI", "How this control fills its parent's horizontal slot.")]
        public HorizontalAlignment horizontalAlignment
        {
            get => (HorizontalAlignment)arrange.horizontalAlignment;
            set { arrange.horizontalAlignment = (byte)value; InvalidateArrange(); }
        }

        [A_XSDElementProperty("VerticalAlignment", "UI", "How this control fills its parent's vertical slot.")]
        public VerticalAlignment verticalAlignment
        {
            get => (VerticalAlignment)arrange.verticalAlignment;
            set { arrange.verticalAlignment = (byte)value; InvalidateArrange(); }
        }

        [A_XSDElementProperty("HorizontalPos", "UI", "Position within the parent, [0;1]. Works with non-container controls.")]
        public float horizontalPosition
        {
            get => arrange.horizontalPosition;
            set { if (Set(ref arrange.horizontalPosition, value)) InvalidateArrange(); }
        }

        [A_XSDElementProperty("VerticalPos", "UI", "Position within the parent, [0;1]. Works with non-container controls.")]
        public float verticalPosition
        {
            get => arrange.verticalPosition;
            set { if (Set(ref arrange.verticalPosition, value)) InvalidateArrange(); }
        }

        [A_XSDElementProperty("DockMode", "UI", "Sets the control's dock mode. Fill - fills the entire area.")]
        public DockMode dockMode
        {
            get => (DockMode)arrange.dockMode;
            set { arrange.dockMode = (byte)value; InvalidateLayout(); }
        }

        [A_XSDElementProperty("Grid.Column", "UI", "If present in a grid sets the control's grid column.")]
        public short gridColumn
        {
            get => arrange.gridColumn;
            set { arrange.gridColumn = value; InvalidateLayout(); }
        }

        [A_XSDElementProperty("Grid.Row", "UI", "If present in a grid sets the control's grid row.")]
        public short gridRow
        {
            get => arrange.gridRow;
            set { arrange.gridRow = value; InvalidateLayout(); }
        }

        [A_XSDElementProperty("ClipToBounds", "UI", "Will not render or hit-test children outside bounds.")]
        public bool clipOutOfBounds
        {
            get => HasFlag(ArrangeFlags.Clip);
            set { SetFlag(ArrangeFlags.Clip, value); InvalidateArrange(); }
        }

        private static bool Set(ref float field, float value)
        {
            if (field == value) return false;
            field = value;
            return true;
        }
        #endregion

        #region ---- paint ----
        // Virtual because a text run's colour belongs to its spans, not to rows[0].
        [A_XSDElementProperty("ColorHex", "UI", "Sets the control color via hex code.")]
        public virtual string colorHex
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

        [A_XSDElementProperty("Alpha", "UI", "Opacity of the control, 0 to 1. Multiplies the coverage its mask already carries.")]
        public virtual float alpha
        {
            get => field;
            set
            {
                field = value;
                visual.tint.W = value;
                Publish();
            }
        } = 1f;

        [A_XSDElementProperty("ControlColor", "UI", "Sets the color of the control.")]
        public ControlColor controlColor
        {
            get => field;
            set
            {
                field = value;
                colorHex = EnumColorToHex(value);
            }
        }

        [A_XSDElementProperty("CornerRadius", "UI", "Rounds the control's corners, in design-space pixels. \"8\", \"top,bottom\" or \"topLeft,topRight,bottomLeft,bottomRight\".")]
        public CornerRadii cornerRadius
        {
            get => field;
            set
            {
                field = value;
                visual.cornerRadius = value.AsVector();
                Publish();
            }
        }

        [A_XSDElementProperty("EdgeColorHex", "UI", "Sets the control's border color via hex code. Needs EdgeThickness to show.")]
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

        [A_XSDElementProperty("EdgeThickness", "UI", "Border width in design-space pixels, drawn inward from the control's edge. Zero draws none.")]
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

        // Which of the three quad kinds rows[0] draws, and so what its sampler means.
        public VulkanControlType kind
        {
            get => field;
            set
            {
                field = value;
                visual.type = value;
                Publish();
            }
        } = VulkanControlType.PanelControl;

        // The distance field on MTSDFControl, the coverage mask on PanelControl, the colour on
        // ImageControl. Null samples nothing.
        public TextureAsset? sampler
        {
            get => field;
            set
            {
                field = value;
                visual.textureIndex = value?.textureIndex ?? VulkanControl.noTexture;
                Publish();
            }
        }

        // The quad mesh's vertex order — uv1 is the far corner, not the near one.
        public void SetUVRect(float u0, float v0, float u1, float v1)
        {
            ref VulkanControl v = ref visual;
            v.uvs.uv1 = new Vector2D<float>(u1, v1);
            v.uvs.uv2 = new Vector2D<float>(u0, v0);
            v.uvs.uv3 = new Vector2D<float>(u0, v1);
            v.uvs.uv4 = new Vector2D<float>(u1, v0);
            Publish();
        }

        [A_XSDElementProperty("Gradient", "UI", "Name of a gradient in Gradients.gradients.xml, ramped across this control's rect in place of its colour.")]
        public virtual string gradient
        {
            get => field;
            set
            {
                field = value;
                visual.gradientIndex = Gradients.IndexOf(value);
                Publish();
            }
        } = "";
        #endregion

        #region ---- layout state ----
        public LayoutRect arrangedRect => arrange.arranged;
        public Vector2D<float> DesiredSize => arrange.desired;

        // The z the children step down from — the translation the last Arrange baked.
        internal float depth => geometry.matrix.M43;

        // Every assignment mirrors into the GPU row the fragment shader discards against.
        public LayoutRect ClipRect
        {
            get => arrange.clip;
            protected set
            {
                arrange.clip = value;
                geometry.clip = new Vector4D<float>(value.x, value.y, value.Right, value.Bottom);
                Publish();
            }
        }

        // The rect a gradient ramps across, when it is not this control's own — a run hands its
        // glyphs its own rect so the ramp spans the text instead of restarting per letter.
        internal void SetGradientSpace(LayoutRect rect)
        {
            geometry.gradientRect = new Vector4D<float>(rect.x, rect.y, rect.Right, rect.Bottom);
            Publish();
        }

        public bool isMeasureDirty => HasFlag(ArrangeFlags.MeasureDirty);
        public bool isArrangeDirty => HasFlag(ArrangeFlags.ArrangeDirty);
        public bool hidden => HasFlag(ArrangeFlags.Hidden);

        protected bool HasFlag(ArrangeFlags flag) => ((ArrangeFlags)arrange.flags & flag) != 0;

        protected void SetFlag(ArrangeFlags flag, bool state)
        {
            ref ArrangeData a = ref arrange;
            a.flags = (byte)(state ? (ArrangeFlags)a.flags | flag : (ArrangeFlags)a.flags & ~flag);
        }

        public void InvalidateLayout()
        {
            if (isMeasureDirty) return;
            SetFlag(ArrangeFlags.MeasureDirty, true);
            SetFlag(ArrangeFlags.ArrangeDirty, true);
            Control current = parent as Control;
            Control topDirty = this;
            while (current != null)
            {
                if (current.isMeasureDirty) return;
                current.SetFlag(ArrangeFlags.MeasureDirty, true);
                current.SetFlag(ArrangeFlags.ArrangeDirty, true);
                topDirty = current;
                current = current.parent as Control;
            }
            UIEngine.RegisterDirtyRoot(topDirty);
        }

        public void InvalidateArrange()
        {
            if (isArrangeDirty) return;
            SetFlag(ArrangeFlags.ArrangeDirty, true);
            Control current = parent as Control;
            Control topDirty = this;
            while (current != null)
            {
                if (current.isArrangeDirty) return;
                current.SetFlag(ArrangeFlags.ArrangeDirty, true);
                topDirty = current;
                current = current.parent as Control;
            }
            UIEngine.RegisterDirtyRoot(topDirty);
        }

        // Degenerate rect — contains no point at all.
        private static readonly LayoutRect hiddenClip = new LayoutRect(0, 0, -1, -1);

        // Takes the subtree out of the draw and the hit-test.
        public void Hide()
        {
            if (hidden) return;
            SetFlag(ArrangeFlags.Hidden, true);
            CollapseClip(this);
        }

        // The next Arrange rewrites the subtree's clips.
        public void Show()
        {
            if (!hidden) return;
            SetFlag(ArrangeFlags.Hidden, false);
            InvalidateLayout();
        }

        private static void CollapseClip(Control control)
        {
            control.ClipRect = hiddenClip;
            foreach (Entity child in control.children)
                if (child is Control childControl)
                    CollapseClip(childControl);
        }
        #endregion

        #region ---- layout (two-pass) ----
        public virtual Vector2D<float> Measure(Vector2D<float> availableSize)
        {
            ref ArrangeData a = ref arrange;
            float w = a.preferredWidth > 0 ? a.preferredWidth : MathF.Max(a.minWidth, availableSize.X);
            float h = a.preferredHeight > 0 ? a.preferredHeight : MathF.Max(a.minHeight, availableSize.Y);
            if (children.Count == 1 && children[0] is Control childControl)
            {
                Vector2D<float> childDesired = childControl.Measure(new Vector2D<float>(
                    MathF.Max(0, w - a.padding.totalHorizontal),
                    MathF.Max(0, h - a.padding.totalVertical)));
                if (a.preferredWidth == 0) w = childDesired.X + a.padding.totalHorizontal;
                if (a.preferredHeight == 0) h = childDesired.Y + a.padding.totalVertical;
            }
            arrange.desired = new Vector2D<float>(w, h);
            SetFlag(ArrangeFlags.MeasureDirty, false);
            return arrange.desired;
        }

        public virtual void Arrange(LayoutRect finalRect)
        {
            WriteArranged(finalRect);
            if (children.Count == 1 && children[0] is Control child)
            {
                ref ArrangeData a = ref arrange;
                LayoutRect innerRect = finalRect.Shrink(a.padding);
                LayoutRect childRect = innerRect.Shrink(child.arrange.margin);
                ref ArrangeData ca = ref child.arrange;
                float cx = childRect.x + (childRect.width - ca.desired.X) * ca.horizontalPosition;
                float cy = childRect.y + (childRect.height - ca.desired.Y) * ca.verticalPosition;
                child.Arrange(new LayoutRect(cx, cy, ca.desired.X, ca.desired.Y));
            }
            SetFlag(ArrangeFlags.ArrangeDirty, false);
        }

        // Bakes an arranged rect into the GPU geometry row and inherits or intersects the clip.
        protected void WriteArranged(LayoutRect finalRect)
        {
            arrange.arranged = finalRect;

            Control parentControl = parent as Control;
            float z = parentControl != null ? parentControl.depth + depthStep : rootDepth;

            Matrix4X4<float> m = Matrix4X4<float>.Identity;
            m *= Matrix4X4.CreateScale(finalRect.width, finalRect.height, 1f);
            m *= Matrix4X4.CreateTranslation(finalRect.x + finalRect.width * 0.5f,
                                             finalRect.y + finalRect.height * 0.5f, z);
            geometry.matrix = m;

            ClipRect = parentControl == null ? finalRect
                : clipOutOfBounds ? LayoutRect.Intersect(finalRect, parentControl.ClipRect)
                : parentControl.ClipRect;

            SetGradientSpace(finalRect);
        }

        // Union of this subtree's arranged rects, and how many rows it holds. Written by the pass
        // UIEngine runs after Arrange, so no override has to remember to maintain them.
        internal void RefreshSubtreeCache()
        {
            LayoutRect bounds = arrange.arranged;
            int count = 1;

            foreach (Entity e in children)
            {
                if (e is not Control child) continue;

                child.RefreshSubtreeCache();
                ref ArrangeData ca = ref child.arrange;
                bounds = LayoutRect.Union(bounds, ca.subtreeBounds);
                count += ca.subtreeCount;
            }

            ref ArrangeData a = ref arrange;
            a.subtreeBounds = bounds;
            a.subtreeCount = count;
        }
        #endregion

        #region ---- pointer ----
        // One handler per event, and the last registration wins. A subclass overrides the virtual;
        // outside code registers a delegate.
        public Func<PointerEvent, bool>? onEnter;
        public Func<PointerEvent, bool>? onExit;
        public Func<PointerEvent, bool>? onMove;
        public Func<PointerEvent, bool>? onPress;
        public Func<PointerEvent, bool>? onRelease;
        public Func<PointerEvent, bool>? onTap;

        public void RegisterOnEnter(Func<PointerEvent, bool> handler) => onEnter = handler;
        public void RegisterOnExit(Func<PointerEvent, bool> handler) => onExit = handler;
        public void RegisterOnMove(Func<PointerEvent, bool> handler) => onMove = handler;
        public void RegisterOnPress(Func<PointerEvent, bool> handler) => onPress = handler;
        public void RegisterOnRelease(Func<PointerEvent, bool> handler) => onRelease = handler;
        public void RegisterOnTap(Func<PointerEvent, bool> handler) => onTap = handler;

        public Func<PointerEvent, bool>? onScroll;
        public void RegisterOnScroll(Func<PointerEvent, bool> handler) => onScroll = handler;

        public virtual bool OnPointerEnter(PointerEvent e) => onEnter?.Invoke(e) ?? false;
        public virtual bool OnPointerExit(PointerEvent e) => onExit?.Invoke(e) ?? false;
        public virtual bool OnPointerMove(PointerEvent e) => onMove?.Invoke(e) ?? false;
        public virtual bool OnPointerPress(PointerEvent e)
        {
            if (draggable && e.button == PointerEvent.leftButton) StartDrag();
            return onPress?.Invoke(e) ?? false;
        }
        public virtual bool OnPointerRelease(PointerEvent e) => onRelease?.Invoke(e) ?? false;
        public virtual bool OnPointerTap(PointerEvent e) => onTap?.Invoke(e) ?? false;

        // The wheel, carried in PointerEvent.delta. Walks up like every other phase.
        public virtual bool OnPointerScroll(PointerEvent e) => onScroll?.Invoke(e) ?? false;

        // Decoration drawn inside a control that owns the interaction — a caret, a selection box.
        // Skipped by the hit-test so it does not swallow the click it sits over.
        public bool hitTestable = true;

        // False hands the active context to the parent instead.
        public virtual bool canBeActiveContext => true;

        // False leaves the active control where it was when this one is pressed.
        public virtual bool takesActiveControl => true;

        // Whether a left press on this control begins a drag.
        [A_XSDElementProperty("Draggable", "UI", "A left press on this control begins a drag.")]
        public bool draggable = false;

        // Claims the drag and tells the parent it lost a child. Also callable directly, for a drag
        // that starts on something other than a plain press.
        public void StartDrag()
        {
            UIEngine.SetDragging(this);
            (parent as Control)?.ChildDraggedOut(this);
        }

        // A drag arrived over this control, is still over it, and has left it. All three walk up
        // until one returns true, like every other pointer event.
        public virtual bool DraggingOverStart(Control dragged, Vector2D<float> point) => false;
        public virtual bool DraggingOver(Control dragged, Vector2D<float> point) => false;
        public virtual bool DraggingOverEnd(Control dragged) => false;

        // A drag was released on this control.
        public virtual void FinishDrag(Control dragged, Vector2D<float> point) { }

        // This control is being dragged, and the pointer left every window or entered one. An
        // overlap counts as neither — the window under the pointer is not knowable there.
        public virtual void DraggedOutOfWindow() { }
        public virtual void DraggedIntoWindow() { }

        // A child of this control was dragged out of it.
        public virtual void ChildDraggedOut(Control child) { }
        #endregion

        #region ---- tree ----
        public override void AddChild(Entity entity)
        {
            if (entity is not Control)
                throw new Exception("Child entity must be a Control");
            if (children.Count > 0)
                throw new Exception("Plain Control supports only one child. Use a container control for multiple children.");

            base.AddChild(entity);
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
        public override Control FindByName(string querryName) => (Control)base.FindByName(querryName);

        // Both pools resequence together — UIElements holds one row per control, VulkanControls one
        // per drawn quad, and the same DFS walk keys them.
        protected void MarkTreeOrderDirty()
        {
            Pool.MarkOrderDirty();
            UIEngine.Controls.MarkOrderDirty();
        }
        #endregion

        public override void OnDestroy()
        {
            base.OnDestroy();
            UIEngine.Forget(this);
        }

        // Widens the draw pool's dirty range so this row is re-uploaded.
        internal void Publish() => Publish(0);

        internal void Publish(int row) => UIEngine.Controls.MarkContentDirty(rows[row]);

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
