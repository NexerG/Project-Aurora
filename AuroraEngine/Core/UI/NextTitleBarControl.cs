using ArctisAurora.Core.Registry;
using ArctisAurora.EngineWork;
using ArctisAurora.EngineWork.Rendering;
using Silk.NET.GLFW;

namespace ArctisAurora.Core.UI
{
    // A stack panel that moves the window while it is dragged. The window is created undecorated,
    // so this is the only thing that can move it. The move itself is Windows', not ours — snap,
    // drag-to-restore and the snap preview come with it and nothing here implements them.
    [A_XSDType("NextTitleBar", "UI")]
    public unsafe class NextTitleBarControl : NextStackPanelControl
    {
        public override bool OnPointerPress(PointerEvent e)
        {
            RenderWindow window = UIEngine.WindowOf(this);
            if (e.button != PointerEvent.leftButton || window == null)
                return base.OnPointerPress(e);

            window.os.DragByCaption(Pump);

            // The OS loop consumes the button-up that ends it, so GLFW never reports one and the
            // key tracker would hold the button down forever.
            InputHandler.instance.ProcessMouseClick(window.os.handle, MouseButton.Left, InputAction.Release, 0);

            // The tick this ran on lasted as long as the drag did, and key repeat, the tap window
            // and the caret blink all count seconds off the tick that follows it.
            Engine.mainSystem.ResyncClock();

            base.OnPointerPress(e);
            return true;
        }

        // Everything main owes the OS loop: a snap resizes the window, whose GLFW callback fits the
        // root, and the arrange writes straight into the pool the render thread is already reading.
        private static void Pump()
        {
            UIEngine.ResolveLayout();
            UIEngine.BuildDrawLists();
        }
    }
}
