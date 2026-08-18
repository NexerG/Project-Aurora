using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Registry.Assets;
using ArctisAurora.EngineWork;
using ArctisAurora.EngineWork.Registry;
using ArctisAurora.EngineWork.Rendering;
using Silk.NET.GLFW;
using Silk.NET.Maths;

namespace ArctisAurora.Core.UISystem.Controls
{
    // Resize edges for a window created undecorated. The content fills the frame edge to edge and
    // four invisible strips overlay the outermost pixels, sitting ahead of it in the child list so
    // the hit-test reaches them first. The engine needs to know nothing about window chrome.
    [A_XSDType("WindowFrame", "UI", AllowedChildren = typeof(IXMLChild_UI), MaxChildren = 1)]
    public unsafe class WindowFrameControl : PanelControl
    {
        private const float band = 4f;
        private const int minWidth = 320;
        private const int minHeight = 240;

        private readonly VulkanControl[] grips = new VulkanControl[4];

        private CursorShape shown = CursorShape.Arrow;
        private bool left, right, top, bottom;
        private Vector2D<float> grab;
        private int grabX, grabY, grabW, grabH;

        // The content child. The grips share the list and are never it.
        private VulkanControl Content
        {
            get
            {
                foreach (Entity e in children)
                    if (e is VulkanControl control && Array.IndexOf(grips, control) < 0) return control;
                return null;
            }
        }

        public override void AddChild(Entity entity)
        {
            if (entity is not VulkanControl control)
                throw new Exception("Child entity must be a VulkanControl");
            if (Content != null)
                throw new Exception("WindowFrameControl supports only one child. Wrap multiple children in a container.");
            entity.parent = this;
            children.Add(entity);
            MarkTreeOrderDirty();
            InvalidateLayout();
        }

        public override Vector2D<float> Measure(Vector2D<float> availableSize)
        {
            if (Content is VulkanControl child) child.Measure(availableSize);

            DesiredSize = availableSize;
            isMeasureDirty = false;
            return DesiredSize;
        }

        public override void Arrange(LayoutRect finalRect)
        {
            arrangedRect = finalRect;
            WriteArrangedTransform(finalRect);
            ClipRect = parent is VulkanControl parentControl
                ? (clipOutOfBounds ? LayoutRect.Intersect(finalRect, parentControl.ClipRect) : parentControl.ClipRect)
                : finalRect;

            EnsureGrips();

            grips[0].Arrange(new LayoutRect(finalRect.x, finalRect.y, band, finalRect.height));
            grips[1].Arrange(new LayoutRect(finalRect.Right - band, finalRect.y, band, finalRect.height));
            grips[2].Arrange(new LayoutRect(finalRect.x, finalRect.y, finalRect.width, band));
            grips[3].Arrange(new LayoutRect(finalRect.x, finalRect.Bottom - band, finalRect.width, band));

            // A maximized window has no edge worth dragging, and an untestable grip hands those
            // pixels back to whatever is underneath rather than swallowing them.
            bool active = !IsMaximized;
            foreach (VulkanControl grip in grips) grip.hitTestable = active;

            if (Content is VulkanControl child) child.Arrange(finalRect);

            isArrangeDirty = false;
        }

        // Inserted ahead of the content, because hit-testing walks children forward and takes the
        // first match. Rebuilt when missing, the way ScrollableControl rebuilds its thumb.
        private void EnsureGrips()
        {
            if (grips[0] != null && children.Contains(grips[0])) return;

            for (int i = 0; i < grips.Length; i++)
            {
                VulkanControl grip = new PanelControl
                {
                    parent = this,
                    maskAsset = AssetRegistries.GetAsset<TextureAsset>("invisible")
                };
                grip.RegisterHover(Hover);
                grip.RegisterOnExit(ClearCursor);
                grip.RegisterOnClick(() => BeginResize(grip));
                grip.RegisterOnDrag((last, delta) => ApplyResize());

                grips[i] = grip;
                children.Insert(i, grip);
            }
            MarkTreeOrderDirty();
        }

        // Only on a change, because ChangeCursor allocates a GLFW cursor it never frees and the
        // hover callback runs every tick.
        private void Hover(Vector2D<float> pos)
        {
            CursorShape shape = ShapeFor(EdgesAt(pos));
            if (shape == shown) return;

            AGlfwWindow.ChangeCursor(shape);
            shown = shape;
        }

        private void ClearCursor()
        {
            if (shown == CursorShape.Arrow) return;

            AGlfwWindow.ChangeCursor(CursorShape.Arrow);
            shown = CursorShape.Arrow;
        }

        private void BeginResize(VulkanControl grip)
        {
            (left, right, top, bottom) = EdgesAt(Pointer());
            if (!(left || right || top || bottom)) return;

            AGlfwWindow._glfw.GetWindowPos(AGlfwWindow.windowHandle, out grabX, out grabY);
            AGlfwWindow._glfw.GetWindowSize(AGlfwWindow.windowHandle, out grabW, out grabH);
            grab = ScreenPos();
            grip.StartDrag();
        }

        // Screen space, so the arithmetic does not shift underneath itself as the window moves. An
        // edge that drags past its minimum stops the far edge rather than pushing the near one.
        private void ApplyResize()
        {
            if (!(left || right || top || bottom)) return;

            Vector2D<float> now = ScreenPos();
            int dx = (int)(now.X - grab.X);
            int dy = (int)(now.Y - grab.Y);

            int x = grabX, y = grabY, w = grabW, h = grabH;

            if (left)
            {
                w = Math.Max(minWidth, grabW - dx);
                x = grabX + (grabW - w);
            }
            else if (right) w = Math.Max(minWidth, grabW + dx);

            if (top)
            {
                h = Math.Max(minHeight, grabH - dy);
                y = grabY + (grabH - h);
            }
            else if (bottom) h = Math.Max(minHeight, grabH + dy);

            AGlfwWindow._glfw.GetWindowPos(AGlfwWindow.windowHandle, out int curX, out int curY);
            if (x != curX || y != curY)
                AGlfwWindow._glfw.SetWindowPos(AGlfwWindow.windowHandle, x, y);
            AGlfwWindow._glfw.SetWindowSize(AGlfwWindow.windowHandle, w, h);
        }

        private static Vector2D<float> Pointer() => WindowControl.ToDesignSpace(InputHandler.mousePos);

        private static Vector2D<float> ScreenPos()
        {
            AGlfwWindow._glfw.GetWindowPos(AGlfwWindow.windowHandle, out int wx, out int wy);
            return new Vector2D<float>(wx + InputHandler.mousePos.X, wy + InputHandler.mousePos.Y);
        }

        private (bool, bool, bool, bool) EdgesAt(Vector2D<float> pos)
        {
            LayoutRect rect = arrangedRect;
            return (pos.X - rect.x <= band, rect.Right - pos.X <= band,
                    pos.Y - rect.y <= band, rect.Bottom - pos.Y <= band);
        }

        private static CursorShape ShapeFor((bool l, bool r, bool t, bool b) e)
        {
            if ((e.l && e.t) || (e.r && e.b)) return CursorShape.NwseResize;
            if ((e.r && e.t) || (e.l && e.b)) return CursorShape.NeswResize;
            if (e.l || e.r) return CursorShape.HResize;
            if (e.t || e.b) return CursorShape.VResize;
            return CursorShape.Arrow;
        }

        private static bool IsMaximized =>
            AGlfwWindow._glfw.GetWindowAttrib(AGlfwWindow.windowHandle, WindowAttributeGetter.Maximized);
    }
}
