using ArctisAurora.Core.Animation;
using ArctisAurora.Core.Data;
using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Registry.Assets;
using ArctisAurora.EngineWork;
using System.Numerics;

namespace ArctisAurora.Core.UI
{
    // One UI element: an ArrangeData row in UIElements and the quads it emits into UIQuads.
    // No transform — the matrix is built at emit.
    [A_XSDType("Control", "EntityRegistry", isAbstract: true)]
    public partial class Control : Entity
    {
        // The camera's ortho box is z in [-512, -0.01], so a root sits at -10 and depth steps toward
        // the near plane from there. Matches what the outgoing stack writes.
        internal const float rootDepth = -10f;
        internal const float depthStep = 0.001f;

        protected override string PoolName => "UIElements";

        public ref ArrangeData arrange => ref Pool.GetRef<ArrangeData>(dataHandle);
        internal ref LayoutNode node => ref Pool.GetRef<LayoutNode>(dataHandle);
        public ref VulkanControl visual => ref Pool.GetRef<VulkanControl>(dataHandle);

        public Control()
        {
            visual.type = VulkanControlType.PanelControl;
            visual.paint = Palettes.Inline(Vector3.One);
            visual.alpha = 1f;
            visual.textureIndex = VulkanControl.noTexture;
            SetUVRect(0f, 0f, 1f, 1f);

            ref ArrangeData a = ref arrange;
            a.horizontalPosition = 0.5f;
            a.verticalPosition = 0.5f;
            a.horizontalAlignment = (byte)HorizontalAlignment.Left;
            a.verticalAlignment = (byte)VerticalAlignment.Top;
            a.flags = (byte)(ArrangeFlags.Clip | ArrangeFlags.MeasureDirty | ArrangeFlags.ArrangeDirty);

            ClipRect = LayoutRect.Infinite;
            UIEngine.RegisterDirtyRoot(this);
        }

        #region ---- authored layout ----
        [A_XSDElementProperty("Width", "UI", "Width in pixels. 0 = auto.")]
        [A_Animatable(typeof(ArrangeData), nameof(ArrangeData.preferredWidth), LayoutChange.Measure)]
        public float preferredWidth
        {
            get => arrange.preferredWidth;
            set { if (Set(ref arrange.preferredWidth, value)) InvalidateLayout(); }
        }

        [A_XSDElementProperty("Height", "UI", "Height in pixels. 0 = auto.")]
        [A_Animatable(typeof(ArrangeData), nameof(ArrangeData.preferredHeight), LayoutChange.Measure)]
        public float preferredHeight
        {
            get => arrange.preferredHeight;
            set { if (Set(ref arrange.preferredHeight, value)) InvalidateLayout(); }
        }

        [A_XSDElementProperty("MinWidth", "UI", "Minimum width in pixels.")]
        [A_Animatable(typeof(ArrangeData), nameof(ArrangeData.minWidth), LayoutChange.Measure)]
        public float minWidth
        {
            get => arrange.minWidth;
            set { if (Set(ref arrange.minWidth, value)) InvalidateLayout(); }
        }

        [A_XSDElementProperty("MinHeight", "UI", "Minimum height in pixels.")]
        [A_Animatable(typeof(ArrangeData), nameof(ArrangeData.minHeight), LayoutChange.Measure)]
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

        public virtual Vector2 size
        {
            get => new Vector2(arrange.width, arrange.height);
            set
            {
                ref ArrangeData a = ref arrange;
                bool changed = a.width != value.X || a.height != value.Y;
                a.width = value.X;
                a.height = value.Y;
                if (changed) InvalidateLayout();
            }
        }

        public virtual void SetSize(Vector2 size) => this.size = size;
        public virtual void SetWidth(float x) => width = x;
        public virtual void SetHeight(float y) => height = y;

        [A_XSDElementProperty("Margin", "UI", "Space outside the control in pixels.")]
        [A_Animatable(typeof(ArrangeData), nameof(ArrangeData.margin), LayoutChange.Measure)]
        public Thickness margin
        {
            get => arrange.margin;
            set { arrange.margin = value; InvalidateLayout(); }
        }

        [A_XSDElementProperty("Padding", "UI", "Space inside the control in pixels.")]
        [A_Animatable(typeof(ArrangeData), nameof(ArrangeData.padding), LayoutChange.Measure)]
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
        [A_Animatable(typeof(ArrangeData), nameof(ArrangeData.horizontalPosition), LayoutChange.Arrange)]
        public float horizontalPosition
        {
            get => arrange.horizontalPosition;
            set { if (Set(ref arrange.horizontalPosition, value)) InvalidateArrange(); }
        }

        [A_XSDElementProperty("VerticalPos", "UI", "Position within the parent, [0;1]. Works with non-container controls.")]
        [A_Animatable(typeof(ArrangeData), nameof(ArrangeData.verticalPosition), LayoutChange.Arrange)]
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

        [A_XSDElementProperty("ClipToBounds", "UI", "On by default. Will not render or hit-test children outside bounds.")]
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
        // Virtual because a text run's colour belongs to its spans, not to its own quad.
        [A_XSDElementProperty("ColorHex", "UI", "Sets the control color via hex code.")]
        public virtual string colorHex
        {
            get => field;
            set
            {
                field = value;
                colorAuthored = true;
                SetPaint(Palettes.Inline(value));
            }
        } = "#FFFFFF";

        [A_XSDElementProperty("Alpha", "UI", "Opacity of the control, 0 to 1. Multiplies the coverage its mask already carries.")]
        [A_Animatable(typeof(VulkanControl), nameof(VulkanControl.alpha))]
        public virtual float alpha
        {
            get => visual.alpha;
            set => visual.alpha = value;
        }

        [A_XSDElementProperty("Role", "UI", "The palette colour this control paints with. ColorHex wins over it.")]
        public PaletteRole role
        {
            get => field;
            set
            {
                field = value;
            }
        }

        [A_XSDElementProperty("Palette", "UI", "Name of a palette in Palettes/*.palette.xml, for this control and everything under it that names none.")]
        public string paletteName
        {
            get => field;
            set
            {
                field = value;
                ownPalette = Palettes.Get(value);
            }
        } = "";

        // palette inheritance, resolved as the control is drawn
        internal PaletteDefinition? ownPalette;
        internal PaletteDefinition? palette;
        internal uint groundBelow;
        protected bool colorAuthored;

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
            }
        }

        [A_XSDElementProperty("EdgeColorHex", "UI", "Sets the control's border color via hex code. Needs EdgeThickness to show.")]
        public string edgeColorHex
        {
            get => field;
            set
            {
                field = value;
                edgeColorAuthored = true;
                visual.edgePaint = Palettes.Inline(value);
            }
        } = "#000000";
        private bool edgeColorAuthored;

        // palette-resolved shape for controls built in code
        public CornerRole cornerRole
        {
            get => field;
            set
            {
                field = value;
                ApplyShape();
            }
        }

        public AccentRole accentRole
        {
            get => field;
            set
            {
                if (value == AccentRole.None && field != AccentRole.None) edgeThickness = Thickness.Zero;
                field = value;
                ApplyShape();
            }
        }

        // Sets the corners and accent bar the palette gives the shape roles.
        private void ApplyShape()
        {
            PaletteDefinition scheme = palette ?? Palettes.Default;

            if (cornerRole != CornerRole.None)
                cornerRadius = cornerRole switch
                {
                    CornerRole.Row => new CornerRadii(scheme.rowRadius),
                    CornerRole.Tab => new CornerRadii(scheme.tabRadius, 0f),
                    CornerRole.TabEnd => new CornerRadii(0f, scheme.tabRadius, 0f, 0f),
                    CornerRole.Control => new CornerRadii(scheme.controlRadius),
                    _ => new CornerRadii(scheme.popupRadius)
                };

            if (accentRole == AccentRole.Row) edgeThickness = new Thickness(0f, 0f, 0f, scheme.rowAccentWidth);
            else if (accentRole == AccentRole.Tab) edgeThickness = new Thickness(scheme.tabAccentWidth, 0f, 0f, 0f);
        }

        [A_XSDElementProperty("EdgeRole", "UI", "The palette surface colour this control's edges paint with. EdgeColorHex wins over it; EdgeAccent when left out.")]
        public PaletteRole edgeRole
        {
            get => field;
            set
            {
                field = value;
            }
        }

        [A_XSDElementProperty("EdgeThickness", "UI", "Border widths in design-space pixels, drawn inward from each side. Zero draws none.")]
        [A_Animatable(typeof(VulkanControl), nameof(VulkanControl.edgeThickness))]
        public Thickness edgeThickness
        {
            get => visual.edgeThickness;
            set => visual.edgeThickness = value;
        }

        // Which of the three quad kinds this control draws, and so what its sampler means.
        public VulkanControlType kind
        {
            get => field;
            set
            {
                field = value;
                visual.type = value;
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
            }
        }

        // The quad mesh's vertex order — uv1 is the far corner, not the near one.
        public void SetUVRect(float u0, float v0, float u1, float v1)
        {
            ref VulkanControl v = ref visual;
            v.uvs.uv1 = new Vector2(u1, v1);
            v.uvs.uv2 = new Vector2(u0, v0);
            v.uvs.uv3 = new Vector2(u0, v1);
            v.uvs.uv4 = new Vector2(u1, v0);
        }

        [A_XSDElementProperty("Gradient", "UI", "Name of a gradient in Gradients.gradients.xml, ramped across this control's rect in place of its colour.")]
        public virtual string gradient
        {
            get => field;
            set
            {
                field = value;
                gradientId = Gradients.IndexOf(value);
                gradientWord = Gradients.Word(gradientId, palette ?? Palettes.Default);
            }
        } = "";

        [A_XSDElementProperty("EdgeGradient", "UI", "Name of a gradient in Gradients.gradients.xml, ramped across this control's rect in place of its edge colour.")]
        public string edgeGradient
        {
            get => field;
            set
            {
                field = value;
                edgeGradientId = Gradients.IndexOf(value);
                edgeGradientWord = Gradients.Word(edgeGradientId, palette ?? Palettes.Default);
            }
        } = "";

        [A_XSDElementProperty("Effect", "UI", "Name of an effect in Effects/*.effects.xml, played on this control from when it is set.")]
        public string effect
        {
            get => field;
            set
            {
                field = value;
                visual.effect = Effects.IndexOf(value);
                RestartEffect();
            }
        } = "";

        // Replays the effect from the current engine time.
        public void RestartEffect() => visual.effectStart = (float)Engine.totalTime;

        [A_XSDElementProperty("Clip", "UI", "Name of a clip in Animations/*.anim.xml, played once when this control starts, or at once when set on a started control.")]
        public string clip
        {
            get => field;
            set
            {
                field = value;
                StopClip(ref _clipRun);
                if (_begun) PlayClip();
            }
        } = "";

        [A_XSDElementProperty("HoverClip", "UI", "Name of a clip in Animations/*.anim.xml, played while the pointer is over this control and run back when it leaves.")]
        public string hoverClip
        {
            get => field;
            set
            {
                field = value;
                StopClip(ref _hoverRun);
            }
        } = "";

        [A_XSDElementProperty("PressClip", "UI", "Name of a clip in Animations/*.anim.xml, played while this control is pressed and run back when it is released.")]
        public string pressClip
        {
            get => field;
            set
            {
                field = value;
                StopClip(ref _pressRun);
            }
        } = "";

        [A_XSDElementProperty("StateBinding", "UI", "Name of a binding in Animations/*.anim.xml that eases this control between rest, hover and press.")]
        public string stateBinding
        {
            get => field;
            set
            {
                field = value;
                _binding?.Detach();
                _binding = null;
            }
        } = "";

        // clip runs, the state binding, and the interaction they follow
        private bool _begun;
        private AnimationHandle[] _clipRun = Array.Empty<AnimationHandle>();
        private AnimationHandle[] _hoverRun = Array.Empty<AnimationHandle>();
        private AnimationHandle[] _pressRun = Array.Empty<AnimationHandle>();
        private StateBinding? _binding;
        private bool _pointerOver;
        private bool _pointerDown;

        public override void OnStart()
        {
            base.OnStart();
            _begun = true;
            PlayClip();
        }

        private void PlayClip()
        {
            if (!string.IsNullOrEmpty(clip)) _clipRun = Animations.Play(this, clip, false);
        }

        // Runs a clip forward, playing it first when it is not running.
        private void RunClip(string name, ref AnimationHandle[] run)
        {
            if (string.IsNullOrEmpty(name)) return;
            if (run.Length == 0) run = Animations.Play(this, name, true);
            else Animations.Direct(run, true);
        }

        private static void StopClip(ref AnimationHandle[] run)
        {
            foreach (AnimationHandle handle in run) Animations.Stop(handle);
            run = Array.Empty<AnimationHandle>();
        }

        // Points the state binding at the current interaction, attaching it on first use.
        private void Interacted()
        {
            if (string.IsNullOrEmpty(stateBinding)) return;
            if (_binding == null)
            {
                PaletteDefinition feel = palette ?? Palettes.Default;
                _binding = StateBinding.Attach(this, AnimationLibrary.Binding(stateBinding), feel.stateFrequency, feel.stateDamping);
            }
            _binding.Set(_pointerOver, _pointerDown);
        }

        // gradient ids and the paint words Emit puts on the row in place of visual's
        protected uint gradientId;
        private uint gradientWord;
        private uint edgeGradientId;
        private uint edgeGradientWord;

        // Paints an authored hex, or the role when there is none.
        public void PaintOr(string? hex, PaletteRole fallback)
        {
            if (hex != null)
            {
                colorHex = hex;
                return;
            }

            colorAuthored = false;
            role = fallback;
        }

        // Paints like source: its authored hex, or its role.
        public void CopyPaint(Control source) => PaintOr(source.colorAuthored ? source.colorHex : null, source.role);

        // Writes the word this control paints with.
        protected virtual void SetPaint(uint paint) => visual.paint = paint;

        // The word a role paints with; Clear shows the ground it sits on.
        protected uint RolePaint(PaletteDefinition scheme, uint ground) => role switch
        {
            PaletteRole.None or PaletteRole.Clear => ground,
            PaletteRole.Ink => Palettes.Ink(scheme, ground, false),
            PaletteRole.MutedInk => Palettes.Ink(scheme, ground, true),
            _ => Palettes.Surface(scheme, role)
        };

        // Paints an unauthored control from its role.
        protected virtual void ApplyRole(PaletteDefinition scheme, uint ground)
        {
            if (role == PaletteRole.None) return;
            SetPaint(RolePaint(scheme, ground));
        }

        // Finishes the row Emit copied from visual.
        internal virtual void PaintRow(ref VulkanControl row)
        {
            if (role == PaletteRole.Clear && !colorAuthored) row.alpha = 0f;
        }

        // Takes the palette and ground from the parent and paints the role against them.
        internal void InheritPaint()
        {
            Control? p = parent as Control;
            palette = ownPalette ?? p?.palette ?? Palettes.Default;
            uint ground = GroundBehind();
            if (!edgeColorAuthored)
                visual.edgePaint = edgeRole >= PaletteRole.Ground && edgeRole <= PaletteRole.Danger
                    ? Palettes.Surface(palette, edgeRole)
                    : Palettes.EdgeAccent(palette);
            ApplyShape();
            gradientWord = Gradients.Word(gradientId, palette);
            edgeGradientWord = Gradients.Word(edgeGradientId, palette);
            if (!colorAuthored) ApplyRole(palette, ground);
            groundBelow = GroundBelow(ground);
        }

        // The ground under this control: its parent's, or its palette's Ground at a root.
        private uint GroundBehind()
        {
            Control? p = parent as Control;
            return p?.palette != null ? p.groundBelow : Palettes.Surface(palette!, PaletteRole.Ground);
        }

        // The ground this control leaves for its children.
        private uint GroundBelow(uint ground)
            => visual.alpha > 0f && kind == VulkanControlType.PanelControl && sampler == null ? visual.paint : ground;
        #endregion

        #region ---- layout state ----
        public LayoutRect arrangedRect => arrange.arranged;
        public Vector2 DesiredSize => arrange.desired;

        public LayoutRect ClipRect
        {
            get => arrange.clip;
            protected set => arrange.clip = value;
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
            SetFlag(ArrangeFlags.MeasureDirty, false);
            InvalidateLayout();
        }

        private static void CollapseClip(Control control)
        {
            control.ClipRect = hiddenClip;
            foreach (Entity child in control.children)
                if (child is Control childControl)
                    CollapseClip(childControl);
        }

        // Narrows the clip of a control and everything under it to rect.
        protected static void ClipSubtree(Control control, LayoutRect rect)
        {
            control.ClipRect = LayoutRect.Intersect(control.ClipRect, rect);
            foreach (Entity child in control.children)
                if (child is Control childControl)
                    ClipSubtree(childControl, rect);
        }
        #endregion

        #region ---- layout (two-pass) ----
        public Vector2 Measure(Vector2 availableSize) => MeasureCore(availableSize);

        public void Arrange(LayoutRect finalRect) => ArrangeCore(finalRect);

        // What a control with its own layout overrides.
        protected virtual Vector2 MeasureCore(Vector2 availableSize)
        {
            ref ArrangeData a = ref arrange;
            float w = a.preferredWidth > 0 ? a.preferredWidth : MathF.Max(a.minWidth, availableSize.X);
            float h = a.preferredHeight > 0 ? a.preferredHeight : MathF.Max(a.minHeight, availableSize.Y);
            if (children.Count == 1 && children[0] is Control childControl)
            {
                Vector2 childDesired = childControl.Measure(new Vector2(
                    MathF.Max(0, w - a.padding.totalHorizontal),
                    MathF.Max(0, h - a.padding.totalVertical)));
                if (a.preferredWidth == 0) w = childDesired.X + a.padding.totalHorizontal;
                if (a.preferredHeight == 0) h = childDesired.Y + a.padding.totalVertical;
            }
            arrange.desired = new Vector2(w, h);
            SetFlag(ArrangeFlags.MeasureDirty, false);
            return arrange.desired;
        }

        protected virtual void ArrangeCore(LayoutRect finalRect)
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

        // Records an arranged rect and inherits or intersects the clip.
        protected void WriteArranged(LayoutRect finalRect)
        {
            ref ArrangeData a = ref arrange;
            a.arranged = finalRect;

            Control parentControl = parent as Control;
            a.clip = parentControl == null ? finalRect
                : ((ArrangeFlags)a.flags & ArrangeFlags.Clip) != 0 ? LayoutRect.Intersect(finalRect, parentControl.ClipRect)
                : parentControl.ClipRect;
        }

        // Places one child in a box by its own alignment, stretching it on either axis that asks.
        protected static void ArrangeByAlignment(Control child, LayoutRect inner)
        {
            ref ArrangeData ca = ref child.arrange;
            HorizontalAlignment ha = (HorizontalAlignment)ca.horizontalAlignment;
            VerticalAlignment va = (VerticalAlignment)ca.verticalAlignment;

            float childW = ha == HorizontalAlignment.Stretch
                ? inner.width
                : MathF.Min(ca.desired.X, inner.width);

            float childH = va == VerticalAlignment.Stretch
                ? inner.height
                : MathF.Min(ca.desired.Y, inner.height);

            float childX = ha switch
            {
                HorizontalAlignment.Left => inner.x,
                HorizontalAlignment.Right => inner.x + inner.width - childW,
                HorizontalAlignment.Center => inner.x + (inner.width - childW) * 0.5f,
                _ => inner.x,
            };

            float childY = va switch
            {
                VerticalAlignment.Top => inner.y,
                VerticalAlignment.Bottom => inner.y + inner.height - childH,
                VerticalAlignment.Center => inner.y + (inner.height - childH) * 0.5f,
                _ => inner.y,
            };

            child.Arrange(new LayoutRect(childX, childY, childW, childH));
        }

        // Union of this subtree's arranged rects, and how many controls it holds. Written by the pass
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

        // Appends this control's quads to UIQuads. A control off its own clip emits
        // nothing; its children are still offered the walk, because a clip is inherited and theirs
        // may sit somewhere else entirely.
        internal virtual void Emit(float z)
        {
            ref ArrangeData a = ref arrange;
            if (!a.arranged.Overlaps(a.clip)) return;

            LayoutRect r = a.arranged;
            LayoutRect c = a.clip;
            DataPool quads = UIEngine.Quads;
            int row = quads.Append();

            ref ControlGeometry g = ref quads.GetSpan<ControlGeometry>()[row];
            g.matrix = Matrix4x4.CreateScale(r.width, r.height, 1f)
                     * Matrix4x4.CreateTranslation(r.x + r.width * 0.5f, r.y + r.height * 0.5f, z);
            g.clip = new Vector4(c.x, c.y, c.Right, c.Bottom);
            g.gradientRect = new Vector4(r.x, r.y, r.Right, r.Bottom);

            ref VulkanControl v = ref quads.GetSpan<VulkanControl>()[row];
            v = visual;
            PaintRow(ref v);
            if (gradientWord != 0) v.paint = gradientWord;
            if (edgeGradientWord != 0) v.edgePaint = edgeGradientWord;
        }
        #endregion

        #region ---- pointer ----
        // One handler per event, and the last registration wins. A subclass overrides the virtual;
        // outside code registers a delegate.
        [A_XSDElementProperty("onEnter", "UI")]
        public Func<PointerEvent, bool>? onEnter;
        [A_XSDElementProperty("onExit", "UI")]
        public Func<PointerEvent, bool>? onExit;
        [A_XSDElementProperty("onMove", "UI")]
        public Func<PointerEvent, bool>? onMove;
        [A_XSDElementProperty("onPress", "UI")]
        public Func<PointerEvent, bool>? onPress;
        [A_XSDElementProperty("onRelease", "UI")]
        public Func<PointerEvent, bool>? onRelease;
        [A_XSDElementProperty("onTap", "UI")]
        public Func<PointerEvent, bool>? onTap;

        public void RegisterOnEnter(Func<PointerEvent, bool> handler) => onEnter = handler;
        public void RegisterOnExit(Func<PointerEvent, bool> handler) => onExit = handler;
        public void RegisterOnMove(Func<PointerEvent, bool> handler) => onMove = handler;
        public void RegisterOnPress(Func<PointerEvent, bool> handler) => onPress = handler;
        public void RegisterOnRelease(Func<PointerEvent, bool> handler) => onRelease = handler;
        public void RegisterOnTap(Func<PointerEvent, bool> handler) => onTap = handler;

        [A_XSDElementProperty("onScroll", "UI")]
        public Func<PointerEvent, bool>? onScroll;
        public void RegisterOnScroll(Func<PointerEvent, bool> handler) => onScroll = handler;

        public virtual bool OnPointerEnter(PointerEvent e)
        {
            _pointerOver = true;
            RunClip(hoverClip, ref _hoverRun);
            Interacted();
            return onEnter?.Invoke(e) ?? false;
        }
        public virtual bool OnPointerExit(PointerEvent e)
        {
            _pointerOver = false;
            _pointerDown = false;
            Animations.Direct(_hoverRun, false);
            Interacted();
            return onExit?.Invoke(e) ?? false;
        }
        public virtual bool OnPointerMove(PointerEvent e) => onMove?.Invoke(e) ?? false;
        public virtual bool OnPointerPress(PointerEvent e)
        {
            _pointerDown = true;
            RunClip(pressClip, ref _pressRun);
            Interacted();
            if (draggable && e.button == PointerEvent.leftButton) StartDrag();
            return onPress?.Invoke(e) ?? false;
        }
        public virtual bool OnPointerRelease(PointerEvent e)
        {
            _pointerDown = false;
            Animations.Direct(_pressRun, false);
            Interacted();
            return onRelease?.Invoke(e) ?? false;
        }
        public virtual bool OnPointerTap(PointerEvent e) => onTap?.Invoke(e) ?? false;

        // The wheel, carried in PointerEvent.delta. Walks up like every other phase.
        public virtual bool OnPointerScroll(PointerEvent e) => onScroll?.Invoke(e) ?? false;

        // Decoration drawn inside a control that owns the interaction — a caret, a selection box.
        // Skipped by the hit-test so it does not swallow the click it sits over.
        public bool hitTestable = true;

        // The control that takes the active context when this one is pressed. Itself by default.
        public virtual Control? ActiveContextTarget() => this;

        // False leaves the active control where it was when this one is pressed.
        public virtual bool takesActiveControl => true;

        // context menu
        [A_XSDElementProperty("ContextMenu", "UI", "The menu document, or registered menu, this control offers on right click.")]
        public string? contextMenu;
        [A_XSDElementProperty("StopsContextMenu", "UI", "Ancestors add nothing to a context menu opened on this control.")]
        public bool stopsContextMenu = false;

        // Whether a left press on this control begins a drag.
        [A_XSDElementProperty("Draggable", "UI", "A left press on this control begins a drag.")]
        public bool draggable = false;

        // drag preview opacity; negative uses the UI setting
        [A_XSDElementProperty("DraggingOpacity", "UI", "Opacity of this control's drag preview. Negative uses the UI setting.")]
        public float draggingOpacity = -1f;

        // Claims the drag and tells the parent it lost a child. Also callable directly, for a drag
        // that starts on something other than a plain press.
        public void StartDrag()
        {
            UIEngine.SetDragging(this);
            (parent as Control)?.ChildDraggedOut(this);
        }

        // What the claimant hears while it is being dragged. Delivered straight to it rather than
        // walked up, so neither returns whether it was consumed.
        public Action<PointerEvent>? onDrag;
        public Action<bool>? onDragStop;

        public void RegisterOnDrag(Action<PointerEvent> handler) => onDrag = handler;
        public void RegisterOnDragStop(Action<bool> handler) => onDragStop = handler;

        public virtual void OnDrag(PointerEvent e) => onDrag?.Invoke(e);
        public virtual void OnDragStop(bool accepted) => onDragStop?.Invoke(accepted);

        // A drag arrived over this control, is still over it, and has left it. All three walk up
        // until one returns true, like every other pointer event.
        public virtual bool DraggingOverStart(Control dragged, Vector2 point) => false;
        public virtual bool DraggingOver(Control dragged, Vector2 point) => false;
        public virtual bool DraggingOverEnd(Control dragged) => false;

        // A drag was released on this control. Walks up until one takes it, and what nobody took is
        // what the claimant hears as a refused drop.
        public virtual bool FinishDrag(Control dragged, Vector2 point) => false;

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

        protected override void OnChildDetached(Entity child)
        {
            MarkTreeOrderDirty();
            InvalidateLayout();
        }

        // A control's children are always controls.
        public override Control FindByName(string querryName) => (Control)base.FindByName(querryName);

        // Keeps UIElements in DFS order, which is the order layout walks it in.
        protected void MarkTreeOrderDirty() => Pool.MarkOrderDirty();
        #endregion

        public override void OnDestroy()
        {
            StopClip(ref _clipRun);
            StopClip(ref _hoverRun);
            StopClip(ref _pressRun);
            _binding?.Detach();
            _binding = null;
            Animations.StopAll(this);
            base.OnDestroy();
            UIEngine.Forget(this);
        }

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

        public static Vector3 HexToRGB(string hex)
        {
            if (hex.StartsWith("#")) hex = hex[1..];
            if (hex.Length != 6)
                throw new ArgumentException("Hex color must be 6 characters long.");

            byte r = Convert.ToByte(hex.Substring(0, 2), 16);
            byte g = Convert.ToByte(hex.Substring(2, 2), 16);
            byte b = Convert.ToByte(hex.Substring(4, 2), 16);
            return new Vector3(r / 255f, g / 255f, b / 255f);
        }
    }
}
