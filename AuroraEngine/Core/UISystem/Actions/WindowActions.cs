using ArctisAurora.Core.Registry;
using ArctisAurora.EngineWork;
using ArctisAurora.EngineWork.Rendering;
using Silk.NET.GLFW;

namespace ArctisAurora.Core.UISystem.Actions
{
    // Window chrome for a window GLFW created undecorated: an application draws its own title bar
    // out of ordinary controls and its buttons name these.
    public static unsafe class WindowActions
    {
        [A_XSDActionDependency("Window.Minimize", "UI", "Iconifies the window")]
        public static void Minimize() => AGlfwWindow._glfw.IconifyWindow(AGlfwWindow.windowHandle);

        [A_XSDActionDependency("Window.MaximizeRestore", "UI", "Maximizes the window, or restores it when it already is")]
        public static void MaximizeRestore()
        {
            if (AGlfwWindow._glfw.GetWindowAttrib(AGlfwWindow.windowHandle, WindowAttributeGetter.Maximized))
                AGlfwWindow._glfw.RestoreWindow(AGlfwWindow.windowHandle);
            else
                AGlfwWindow._glfw.MaximizeWindow(AGlfwWindow.windowHandle);
        }

        [A_XSDActionDependency("Window.Close", "UI", "Stops the engine's threads and ends the application")]
        public static void Close() => Engine.engineInstance?.Stop();
    }
}
