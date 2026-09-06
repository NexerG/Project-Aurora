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
        internal const float depthStep = 0.001f;

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
        public float preferredWidth
        {
            get => arrange.preferredWidth;
            set { if (Set(ref arrange.preferredWidth, value)) InvalidateLayout(); }
        }

        public float preferredHeight
        {
            get => arrange.preferredHeight;
            set { if (Set(ref arrange.preferredHeight, value)) InvalidateLayout(); }
        }

        public float minWidth
        {
            get => arrange.minWidth;
            set { if (Set(ref arrange.minWidth, value)) InvalidateLayout(); }
        }

        public float minHeight
        {
            get => arrange.minHeight;
            set { if (Set(ref arrange.minHeight, value)) InvalidateLayout(); }
        }

        public float widthStar
        {
            get => arrange.widthStar;
            set { if (Set(ref arrange.widthStar, value)) InvalidateLayout(); }
        }

        public float heightStar
        {
            get => arrange.heightStar;
            set { if (Set(ref arrange.heightStar, value)) InvalidateLayout(); }
        }

        public bool IsWidthStar => arrange.widthStar > 0f;
        public bool IsHeightStar => arrange.heightStar > 0f;

        public Thickness margin
        {
            get => arrange.margin;
            set { arrange.margin = value; InvalidateLayout(); }
        }

        public Thickness padding
        {
            get => arrange.padding;
            set { arrange.padding = value; InvalidateLayout(); }
        }

        public HorizontalAlignment horizontalAlignment
        {
            get => (HorizontalAlignment)arrange.horizontalAlignment;
            set { arrange.horizontalAlignment = (byte)value; InvalidateArrange(); }
        }

        public VerticalAlignment verticalAlignment
        {
            get => (VerticalAlignment)arrange.verticalAlignment;
            set { arrange.verticalAlignment = (byte)value; InvalidateArrange(); }
        }

        public float horizontalPosition
        {
            get => arrange.horizontalPosition;
            set { if (Set(ref arrange.horizontalPosition, value)) InvalidateArrange(); }
        }

        public float verticalPosition
        {
            get => arrange.verticalPosition;
            set { if (Set(ref arrange.verticalPosition, value)) InvalidateArrange(); }
        }

        public DockMode dockMode
        {
            get => (DockMode)arrange.dockMode;
            set { arrange.dockMode = (byte)value; InvalidateLayout(); }
        }

        public short gridColumn
        {
            get => arrange.gridColumn;
            set { arrange.gridColumn = value; InvalidateLayout(); }
        }

        public short gridRow
        {
            get => arrange.gridRow;
            set { arrange.gridRow = value; InvalidateLayout(); }
        }

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

        public virtual bool OnPointerEnter(PointerEvent e) => onEnter?.Invoke(e) ?? false;
        public virtual bool OnPointerExit(PointerEvent e) => onExit?.Invoke(e) ?? false;
        public virtual bool OnPointerMove(PointerEvent e) => onMove?.Invoke(e) ?? false;
        public virtual bool OnPointerPress(PointerEvent e) => onPress?.Invoke(e) ?? false;
        public virtual bool OnPointerRelease(PointerEvent e) => onRelease?.Invoke(e) ?? false;
        public virtual bool OnPointerTap(PointerEvent e) => onTap?.Invoke(e) ?? false;
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

        // Both pools hold one row per control in the same DFS order, so both resequence together.
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
