using ArctisAurora.Core.Registry;
using ArctisAurora.Core.UISystem.Controls.Containers;
using ArctisAurora.EngineWork;
using ArctisAurora.EngineWork.Rendering;
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
            grab = InputHandler.mousePos;
            StartDrag();
            base.ResolveOnClick(oldPos, delta);
        }

        // Moves the window by however far the pointer has drifted from where it grabbed. Moving the
        // window carries the pointer with it, so the drift returns to zero and this converges rather
        // than running away. Raw window pixels, not design space — the window moves in screen units.
        public override void ResolveDrag(Vector2D<float> lastPos, Vector2D<float> delta)
        {
            AGlfwWindow._glfw.GetWindowPos(AGlfwWindow.windowHandle, out int x, out int y);
            AGlfwWindow._glfw.SetWindowPos(AGlfwWindow.windowHandle,
                x + (int)(InputHandler.mousePos.X - grab.X),
                y + (int)(InputHandler.mousePos.Y - grab.Y));

            base.ResolveDrag(lastPos, delta);
        }
    }
}
