using ArctisAurora.Core.Registry;
using ArctisAurora.Core.UISystem.Controls.Containers;
using ArctisAurora.EngineWork;
using ArctisAurora.EngineWork.Rendering;
using Silk.NET.GLFW;
using Silk.NET.Maths;

namespace ArctisAurora.Core.UISystem.Controls
{
    // A stack panel that moves the window while it is dragged. The window is created undecorated,
    // so this is the only thing that can move it.
    [A_XSDType("TitleBar", "UI", AllowedChildren = typeof(IXMLChild_UI))]
    public unsafe class TitleBarControl : StackPanelControl
    {
        private Vector2D<float> grab;

        public override void ResolveOnClick(Vector2D<float> oldPos, Vector2D<float> delta)
        {
            grab = RenderWindow.Of(this).mousePos;
            StartDrag();
            base.ResolveOnClick(oldPos, delta);
        }

        // Moves the window by however far the pointer has drifted from where it grabbed. Moving the
        // window carries the pointer with it, so the drift returns to zero and this converges rather
        // than running away. Raw window pixels, not design space — the window moves in screen units.
        public override void ResolveDrag(Vector2D<float> lastPos, Vector2D<float> delta)
        {
            RenderWindow window = RenderWindow.Of(this);
            WindowHandle* handle = window.os.handle;
            AGlfwWindow._glfw.GetWindowPos(handle, out int x, out int y);
            AGlfwWindow._glfw.SetWindowPos(handle,
                x + (int)(window.mousePos.X - grab.X),
                y + (int)(window.mousePos.Y - grab.Y));

            base.ResolveDrag(lastPos, delta);
        }
    }
}
