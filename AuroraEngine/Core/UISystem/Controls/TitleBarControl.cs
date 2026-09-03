using ArctisAurora.Core.Registry;
using ArctisAurora.Core.UISystem.Controls.Containers;
using ArctisAurora.EngineWork;
using ArctisAurora.EngineWork.Rendering;
using Silk.NET.GLFW;
using Silk.NET.Maths;

namespace ArctisAurora.Core.UISystem.Controls
{
    // A stack panel that moves the window while it is dragged. The window is created undecorated,
    // so this is the only thing that can move it. The move itself is Windows', not ours — snap,
    // drag-to-restore and the snap preview come with it and nothing here implements them.
    [A_XSDType("TitleBar", "UI", AllowedChildren = typeof(IXMLChild_UI))]
    public unsafe class TitleBarControl : StackPanelControl
    {
        public override void ResolveOnClick(Vector2D<float> oldPos, Vector2D<float> delta)
        {
            RenderWindow window = RenderWindow.Of(this);

            window.os.DragByCaption(Pump);

            // The OS loop consumes the button-up that ends it, so GLFW never reports one and the
            // key tracker would hold the button down forever.
            InputHandler.instance.ProcessMouseClick(window.os.handle, MouseButton.Left, InputAction.Release, 0);

            // The tick this ran on lasted as long as the drag did, and key repeat, the tap window
            // and the caret blink all count seconds off the tick that follows it.
            Engine.mainSystem.ResyncClock();

            base.ResolveOnClick(oldPos, delta);
        }

        // Everything main owes the OS loop: a snap resizes the window, whose GLFW callback fits the
        // root, and the arrange writes straight into the pool the render thread is already reading.
        private static void Pump()
        {
            UILayout.ResolveLayout();
            UILayout.RefreshWindowRanges();
        }
    }
}
