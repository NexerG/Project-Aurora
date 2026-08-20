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

            CloseWhenNamed(window, new HashSet<DocumentEditorControl>());
        }

        // Settles every edited note in the window before it goes. One prompt at a time: each answer
        // re-checks the window, so the notes are named in the order they are found and cancelling any
        // of them leaves the window open. A note the user chose not to save is remembered, or the
        // walk would find it again and never get past it.
        private static void CloseWhenNamed(RenderWindow window, HashSet<DocumentEditorControl> discarded)
        {
            DocumentEditorControl unnamed = FirstUnnamed(window.ui.uiRoot, discarded);
            if (unnamed != null)
            {
                unnamed.SaveNamed(
                    () => CloseWhenNamed(window, discarded),
                    () => { discarded.Add(unnamed); CloseWhenNamed(window, discarded); });
                return;
            }

            // A named note is never asked about, so this is the only thing that writes it — the
            // window closing used to drop its edits without a word.
            SaveNamedNotes(window.ui.uiRoot, discarded);
            Engine.CloseWindow(window);
        }

        private static DocumentEditorControl FirstUnnamed(VulkanControl control, HashSet<DocumentEditorControl> discarded)
        {
            if (control == null) return null;
            if (control is DocumentEditorControl editor && editor.needsNaming && !discarded.Contains(editor))
                return editor;

            foreach (Entity child in control.children)
                if (child is VulkanControl childControl && FirstUnnamed(childControl, discarded) is DocumentEditorControl found)
                    return found;

            return null;
        }

        // Only what was actually edited. A first save of a hand-authored note does not reproduce its
        // bytes, so writing untouched ones would rewrite every open file on every close.
        private static void SaveNamedNotes(VulkanControl control, HashSet<DocumentEditorControl> discarded)
        {
            if (control == null) return;
            if (control is DocumentEditorControl editor && !discarded.Contains(editor)
                && editor.session != null && editor.session.isDirty)
                editor.Save();

            foreach (Entity child in control.children)
                if (child is VulkanControl childControl)
                    SaveNamedNotes(childControl, discarded);
        }
    }
}
