using ArctisAurora.Core.Registry;
using ArctisAurora.Core.UISystem;
using ArctisAurora.EngineWork;
using ArctisAurora.EngineWork.Rendering;
using Silk.NET.GLFW;

namespace ArctisAurora.Core.UISystem.Actions
{
    // Window chrome for a window GLFW created undecorated: an application draws its own title bar
    // out of ordinary controls and its buttons name these.
    public static unsafe class WindowActions
    {
        // These are bound from XML as zero-argument delegates, so the window comes from the button
        // that fired them — which is whatever the pointer is on at release.
        private static RenderWindow Acting() => RenderWindow.Of(UICollisionHandling.hovering);

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

        [A_XSDActionDependency("Window.Close", "UI", "Closes the window, or ends the application when it is the main one")]
        public static void Close()
        {
            RenderWindow window = Acting();
            if (window == null) return;

            Engine.CloseWindow(window);
        }
    }
}
