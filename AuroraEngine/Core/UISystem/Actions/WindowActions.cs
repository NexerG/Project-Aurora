using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.UISystem;
using ArctisAurora.Core.UISystem.Controls;
using ArctisAurora.Core.UISystem.Controls.Text.Document;
using ArctisAurora.EngineWork;
using ArctisAurora.EngineWork.Rendering;
using Silk.NET.GLFW;

namespace ArctisAurora.Core.UISystem.Actions
{
    // Window chrome for a window GLFW created undecorated: an application draws its own title bar
    // out of ordinary controls and its buttons name these.
    public static unsafe class WindowActions
    {
        // These are bound from XML as zero-argument delegates, so the window comes from the control
        // that fired them: the menu's owner when one is open, otherwise whatever the pointer is on.
        private static RenderWindow Acting() =>
            RenderWindow.Of(ContextMenus.invoker ?? UICollisionHandling.hovering);

        [A_XSDActionDependency("Window.Minimize", "UI", "Iconifies the window")]
        public static void Minimize()
        {
            RenderWindow window = Acting();
            if (window == null) return;

            AGlfwWindow._glfw.IconifyWindow(window.os.handle);
        }

        [A_XSDActionDependency("Window.MaximizeRestore", "UI", "Maximizes the window, or restores it when it already is")]
        public static void MaximizeRestore()
        {
            RenderWindow window = Acting();
            if (window == null) return;

            WindowHandle* handle = window.os.handle;
            if (AGlfwWindow._glfw.GetWindowAttrib(handle, WindowAttributeGetter.Maximized))
                AGlfwWindow._glfw.RestoreWindow(handle);
            else
                AGlfwWindow._glfw.MaximizeWindow(handle);
        }

        // The main window closing is the application closing, so it goes through the shutdown
        // sequence; any other window settles its own notes and goes on its own.
        [A_XSDActionDependency("Window.Close", "UI", "Closes the window, or ends the application when it is the main one")]
        public static void Close()
        {
            RenderWindow window = Acting();
            if (window == null) return;

            if (window == Engine.primary)
            {
                Shutdown.Request();
                return;
            }

            NoteActions.SettleWindow(window, () => Engine.CloseWindow(window));
        }
    }
}
